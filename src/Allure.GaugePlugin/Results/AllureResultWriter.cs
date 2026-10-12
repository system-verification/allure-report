// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Configuration;
using Allure.GaugePlugin.Extensions;
using Allure.GaugePlugin.Models;
using Gauge.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Allure.GaugePlugin.Results;

internal sealed class AllureResultWriter(AllureOptions options, TimeProvider timeProvider)
{
    private const string InterruptedMessage = "Execution ended before this scenario finished";

    private readonly string _outputDir = options.ResultsDirectory;

    private readonly ConcurrentDictionary<SpecKey, SpecContext> _specs = new();
    private readonly ConcurrentDictionary<ScenarioKey, ScenarioContext> _scenarios = new();
    private readonly ConcurrentDictionary<StepKey, AllureStep> _steps = new();

    private readonly object _launchLock = new();
    private int _activeLaunches;
    private bool _initialized;

    public string OutputDirectory => _outputDir;

    private long Now => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    public void StartLaunch(ExecutionStartingRequest request)
    {
        lock (_launchLock)
        {
            _activeLaunches++;
            if (!_initialized)
            {
                _initialized = true;
                Directory.CreateDirectory(_outputDir);

                // Clean previous run's results so Allure doesn't treat them as retries.
                if (options.CleanResults)
                    CleanResults();

                if (options.CreateConfig)
                    WriteAllureConfig();
            }
        }

        var suite = request.SuiteResult;
        if (suite?.PreHookFailure is { } failure)
            WriteHookResult("Before suite hook", "gauge:before-suite", failure, suite.PreHookMessages,
                suite.PreHookScreenshotFiles, SuiteHookLabels(request.CurrentExecutionInfo), null);
    }

    public void FinishLaunch(ExecutionEndingRequest request)
    {
        var suite = request.SuiteResult;
        if (suite?.PostHookFailure is { } failure)
            WriteHookResult("After suite hook", "gauge:after-suite", failure, suite.PostHookMessages,
                suite.PostHookScreenshotFiles, SuiteHookLabels(request.CurrentExecutionInfo), null);

        lock (_launchLock)
        {
            if (--_activeLaunches > 0)
                return;
        }

        FlushPending(InterruptedMessage);
        WriteEnvironment(request);
    }

    public void StartSpec(SpecExecutionStartingRequest request)
    {
        var info = request.CurrentExecutionInfo;
        var spec = request.SpecResult.ProtoSpec;
        var dataTable = spec.Items.FirstOrDefault(i => i.ItemType == ProtoItem.Types.ItemType.Table)?.Table;
        var context = new SpecContext(spec.SpecHeading, spec.FileName, spec.Tags.ToList(), dataTable);
        _specs[GetSpecKey(info)] = context;

        WriteSpecHookResults(info, context, spec.PreHookFailures, spec.PreHookMessages, spec.PreHookScreenshotFiles, "Before spec hook", "before-spec");
    }

    public void FinishSpec(SpecExecutionEndingRequest request)
    {
        var info = request.CurrentExecutionInfo;
        var spec = request.SpecResult.ProtoSpec;
        _specs.TryRemove(GetSpecKey(info), out var context);
        context ??= new SpecContext(spec.SpecHeading, spec.FileName, spec.Tags.ToList(), null);

        WriteSpecHookResults(info, context, spec.PostHookFailures, spec.PostHookMessages, spec.PostHookScreenshotFiles, "After spec hook", "after-spec");
    }

    public void StartScenario(ScenarioExecutionStartingRequest request)
    {
        var info = request.CurrentExecutionInfo;
        var (scenario, tableDriven) = Unwrap(request.ScenarioResult.ProtoItem);

        var specContext = _specs.GetValueOrDefault(GetSpecKey(info));
        var specName = specContext?.Name ?? info.CurrentSpec.Name;
        var specPath = GetRelativeSpecPath(specContext?.FileName ?? info.CurrentSpec.FileName);
        var fullName = $"{specPath}:{scenario.Span?.Start ?? 0}";
        var parameters = GetTableParameters(tableDriven, specContext?.DataTable);

        var labels = BaseLabels(info, specName, specPath);
        var links = new List<AllureLink>();
        ApplyTags((specContext?.Tags ?? []).Concat(scenario.Tags), labels, links);
        if (!labels.Any(l => l.Name == "feature"))
            labels.Add(Label("feature", specName));

        var testResult = new AllureTestResult
        {
            Uuid = Guid.NewGuid().ToString(),
            FullName = fullName,
            Name = scenario.ScenarioHeading,
            HistoryId = ComputeHash(fullName + string.Join(",", parameters.Select(p => $"{p.Name}={p.Value}"))),
            TestCaseId = ComputeHash(fullName),
            Labels = labels,
            Links = links,
            Parameters = parameters.Count > 0 ? parameters : null,
            Start = Now,
            Steps = [],
        };

        _scenarios[GetScenarioKey(info)] = new ScenarioContext(testResult);
    }

    public void FinishScenario(ScenarioExecutionEndingRequest request)
    {
        if (!_scenarios.TryRemove(GetScenarioKey(request.CurrentExecutionInfo), out var context))
            return;

        var (scenario, _) = Unwrap(request.ScenarioResult.ProtoItem);
        var testResult = context.Result;

        lock (context)
        {
            testResult.Stop = Now;

            AppendUnexecutedSteps(testResult, scenario);
            AddHookArtifacts(testResult, "Before scenario hook", scenario.PreHookMessages, scenario.PreHookScreenshotFiles, scenario.PreHookFailure);
            AddHookArtifacts(testResult, "After scenario hook", scenario.PostHookMessages, scenario.PostHookScreenshotFiles, scenario.PostHookFailure);

            switch (scenario.ExecutionStatus)
            {
                case ExecutionStatus.Passed:
                    testResult.Status = AllureStatus.Passed;
                    break;

                case ExecutionStatus.Failed:
                    var hookFailure = scenario.PreHookFailure ?? scenario.PostHookFailure;
                    if (context.FirstFailure is { } first)
                    {
                        testResult.Status = first.Status;
                        testResult.StatusDetails = first.Details;
                    }
                    else if (hookFailure is not null)
                    {
                        // A failing hook is a setup/teardown problem rather than a test failure.
                        testResult.Status = AllureStatus.Broken;
                        testResult.StatusDetails = Details(hookFailure);
                    }
                    else
                    {
                        testResult.Status = AllureStatus.Failed;
                    }
                    break;

                case ExecutionStatus.Skipped:
                case ExecutionStatus.Notexecuted:
                    testResult.Status = AllureStatus.Skipped;
                    if (scenario.SkipErrors.Count > 0)
                        testResult.StatusDetails = new AllureStatusDetails { Message = string.Join(Environment.NewLine, scenario.SkipErrors) };
                    break;

                default:
                    testResult.Status = AllureStatus.Broken;
                    break;
            }
        }

        WriteResultFile(testResult);
    }

    public void StartConcept(ConceptExecutionStartingRequest request)
    {
        if (!_scenarios.TryGetValue(GetScenarioKey(request.CurrentExecutionInfo), out var context))
            return;

        var concept = new AllureStep
        {
            Name = request.CurrentExecutionInfo.CurrentStep.Step.GetStepName(),
            Start = Now,
            Steps = [],
        };

        lock (context)
        {
            context.CurrentSteps.Add(concept);
            context.Concepts.Push(concept);
        }
    }

    public void FinishConcept(ConceptExecutionEndingRequest request)
    {
        if (!_scenarios.TryGetValue(GetScenarioKey(request.CurrentExecutionInfo), out var context))
            return;

        lock (context)
        {
            if (!context.Concepts.TryPop(out var concept))
                return;

            concept.Stop = Now;

            var executionResult = request.StepResult?.ProtoItem?.Concept?.ConceptExecutionResult;
            if (executionResult is not null)
                ApplyResult(concept, executionResult);
            else
                concept.Status = WorstStatus(concept.Steps!);
        }
    }

    public void StartStep(StepExecutionStartingRequest request)
    {
        var info = request.CurrentExecutionInfo;
        var executeStep = info.CurrentStep.Step;

        var step = new AllureStep
        {
            Name = executeStep.GetStepName(),
            Start = Now,
        };

        foreach (var parameter in executeStep.Parameters.Where(p => p.IsTable() && p.Table is not null))
            step.Attachments = step.Attachments.With(WriteCsvAttachment(string.IsNullOrEmpty(parameter.Name) ? "table" : parameter.Name, parameter.Table));

        if (_scenarios.TryGetValue(GetScenarioKey(info), out var context))
        {
            lock (context)
            {
                context.CurrentSteps.Add(step);
            }
        }

        _steps[GetStepKey(info)] = step;
    }

    public void FinishStep(StepExecutionEndingRequest request)
    {
        var info = request.CurrentExecutionInfo;
        if (!_steps.TryRemove(GetStepKey(info), out var step))
            return;

        var stop = Now;
        var protoStep = request.StepResult?.ProtoItem?.Step;
        var stepResult = protoStep?.StepExecutionResult;
        var executionResult = stepResult?.ExecutionResult;

        step.Stop = stop;
        // Gauge's own timing is more accurate than event arrival; never start before the recorded start
        // so the step stays nested inside its parent on the timeline.
        if (executionResult is { ExecutionTime: > 0 })
            step.Start = Math.Max(step.Start, stop - executionResult.ExecutionTime);

        _scenarios.TryGetValue(GetScenarioKey(info), out var context);

        if (stepResult is null)
        {
            step.Status = AllureStatus.Broken;
            step.StatusDetails = new AllureStatusDetails { Message = "Gauge reported no execution result for this step" };
        }
        else
        {
            ApplyResult(step, stepResult);
        }

        if (context is not null && step.Status is AllureStatus.Failed or AllureStatus.Broken)
        {
            lock (context)
            {
                context.FirstFailure ??= (step.Status, step.StatusDetails);
            }
        }

        if (executionResult is not null)
        {
            foreach (var screenshot in executionResult.ScreenshotFiles)
                step.Attachments = step.Attachments.With(CopyScreenshot(screenshot, "screenshot"));
            step.Attachments = step.Attachments.With(CopyScreenshot(executionResult.FailureScreenshotFile, "failure screenshot"));
            step.Attachments = step.Attachments.With(WriteTextAttachment("messages", executionResult.Message));
        }

        if (protoStep is not null)
        {
            AddHookArtifacts(step, "Before step hook", protoStep.PreHookMessages, protoStep.PreHookScreenshotFiles, stepResult?.PreHookFailure);
            AddHookArtifacts(step, "After step hook", protoStep.PostHookMessages, protoStep.PostHookScreenshotFiles, stepResult?.PostHookFailure);
        }
    }

    /// <summary>
    /// Writes scenarios that never received an ending event (e.g. Gauge was aborted) as broken,
    /// so they are not silently missing from the report.
    /// </summary>
    public void FlushPending(string reason)
    {
        foreach (var key in _scenarios.Keys)
        {
            if (!_scenarios.TryRemove(key, out var context))
                continue;

            lock (context)
            {
                var testResult = context.Result;
                testResult.Stop = Now;
                CloseOpenSteps(testResult.Steps, testResult.Stop, reason);
                testResult.Status = AllureStatus.Broken;
                testResult.StatusDetails ??= new AllureStatusDetails { Message = reason };
            }

            WriteResultFile(context.Result);
        }

        _steps.Clear();
    }

    private static void ApplyResult(AllureStep step, ProtoStepExecutionResult result)
    {
        var executionResult = result.ExecutionResult;

        if (result.Skipped)
        {
            step.Status = AllureStatus.Skipped;
            if (!string.IsNullOrEmpty(result.SkippedReason))
                step.StatusDetails = new AllureStatusDetails { Message = result.SkippedReason };
        }
        else if (result.PreHookFailure is { } preHookFailure)
        {
            step.Status = AllureStatus.Broken;
            step.StatusDetails = Details(preHookFailure);
        }
        else if (executionResult is { Failed: true })
        {
            step.Status = AllureStatus.Failed;
            step.StatusDetails = new AllureStatusDetails { Message = executionResult.ErrorMessage, Trace = executionResult.StackTrace };
        }
        else if (result.PostHookFailure is { } postHookFailure)
        {
            step.Status = AllureStatus.Broken;
            step.StatusDetails = Details(postHookFailure);
        }
        else
        {
            step.Status = AllureStatus.Passed;
        }
    }

    private static void AppendUnexecutedSteps(AllureTestResult testResult, ProtoScenario scenario)
    {
        static bool IsExecutable(ProtoItem item) => item.ItemType is ProtoItem.Types.ItemType.Step or ProtoItem.Types.ItemType.Concept;
        static bool HasResult(ProtoItem item) => item.ItemType == ProtoItem.Types.ItemType.Step
            ? item.Step?.StepExecutionResult is not null
            : item.Concept?.ConceptExecutionResult is not null;

        // Only trust missing results when Gauge populated results for the steps that did run;
        // otherwise every step would be duplicated as skipped.
        if (!scenario.Contexts.Concat(scenario.ScenarioItems).Where(IsExecutable).Any(HasResult))
            return;

        foreach (var item in scenario.ScenarioItems.Where(i => IsExecutable(i) && !HasResult(i)))
        {
            var protoStep = item.ItemType == ProtoItem.Types.ItemType.Step ? item.Step : item.Concept?.ConceptStep;
            if (protoStep is null)
                continue;

            testResult.Steps!.Add(new AllureStep
            {
                Name = protoStep.GetStepName(),
                Status = AllureStatus.Skipped,
                StatusDetails = new AllureStatusDetails { Message = "Not executed" },
                Start = testResult.Stop,
                Stop = testResult.Stop,
            });
        }
    }

    private static void CloseOpenSteps(List<AllureStep>? steps, long stop, string reason)
    {
        if (steps is null)
            return;

        foreach (var step in steps)
        {
            CloseOpenSteps(step.Steps, stop, reason);
            if (step.Stop != 0)
                continue;

            step.Stop = stop;
            step.Status = AllureStatus.Broken;
            step.StatusDetails ??= new AllureStatusDetails { Message = reason };
        }
    }

    private static AllureStatus WorstStatus(List<AllureStep> steps)
    {
        if (steps.Any(s => s.Status == AllureStatus.Failed)) return AllureStatus.Failed;
        if (steps.Any(s => s.Status == AllureStatus.Broken)) return AllureStatus.Broken;
        if (steps.Count > 0 && steps.All(s => s.Status == AllureStatus.Skipped)) return AllureStatus.Skipped;
        return AllureStatus.Passed;
    }

    private static (ProtoScenario Scenario, ProtoTableDrivenScenario? TableDriven) Unwrap(ProtoItem item)
        => item is { ItemType: ProtoItem.Types.ItemType.TableDrivenScenario, TableDrivenScenario: { } tableDriven }
            ? (tableDriven.Scenario, tableDriven)
            : (item.Scenario, null);

    private static List<AllureParameter> GetTableParameters(ProtoTableDrivenScenario? tableDriven, ProtoTable? specDataTable)
    {
        var parameters = new List<AllureParameter>();
        if (tableDriven is null)
            return parameters;

        if (tableDriven.IsSpecTableDriven)
        {
            var row = specDataTable?.Rows.ElementAtOrDefault(tableDriven.TableRowIndex);
            if (!AddRow(parameters, specDataTable?.Headers, row))
                parameters.Add(new AllureParameter { Name = "Data table row", Value = (tableDriven.TableRowIndex + 1).ToString() });
        }

        if (tableDriven.IsScenarioTableDriven)
        {
            var rowTable = tableDriven.ScenarioTableRow;
            var added = rowTable is { Rows.Count: > 0 }
                ? AddRow(parameters, rowTable.Headers ?? tableDriven.ScenarioDataTable?.Headers, rowTable.Rows[0])
                : AddRow(parameters, tableDriven.ScenarioDataTable?.Headers,
                    tableDriven.ScenarioDataTable?.Rows.ElementAtOrDefault(tableDriven.ScenarioTableRowIndex));
            if (!added)
                parameters.Add(new AllureParameter { Name = "Scenario table row", Value = (tableDriven.ScenarioTableRowIndex + 1).ToString() });
        }

        return parameters;
    }

    private static bool AddRow(List<AllureParameter> parameters, ProtoTableRow? headers, ProtoTableRow? row)
    {
        if (headers is null || row is null || headers.Cells.Count == 0)
            return false;

        for (var i = 0; i < headers.Cells.Count; i++)
            parameters.Add(new AllureParameter { Name = headers.Cells[i], Value = row.Cells.ElementAtOrDefault(i) });
        return true;
    }

    private void ApplyTags(IEnumerable<string> tags, List<AllureLabel> labels, List<AllureLink> links)
    {
        foreach (var tag in tags)
        {
            var separator = tag.IndexOf(':');
            var prefix = separator > 0 ? tag[..separator].Trim().ToLowerInvariant() : "";
            var value = separator > 0 ? tag[(separator + 1)..].Trim() : "";

            if (value.Length == 0)
            {
                labels.Add(Label("tag", tag));
                continue;
            }

            switch (prefix)
            {
                case "severity" or "owner" or "epic" or "feature" or "story" or "layer" or "lead":
                    labels.Add(Label(prefix, value));
                    break;
                case "issue":
                    links.Add(Link("issue", value, options.IssueLinkPattern));
                    break;
                case "tms":
                    links.Add(Link("tms", value, options.TmsLinkPattern));
                    break;
                default:
                    labels.Add(Label("tag", tag));
                    break;
            }
        }
    }

    private static AllureLink Link(string type, string name, string? pattern) => new()
    {
        Type = type,
        Name = name,
        Url = pattern is null ? name : pattern.Replace("{}", Uri.EscapeDataString(name)),
    };

    private List<AllureLabel> BaseLabels(ExecutionInfo? info, string suite, string? specPath)
    {
        var labels = new List<AllureLabel>
        {
            Label("host", Environment.MachineName),
            // Gauge runs parallel streams as separate runners; the runner id gives a meaningful timeline.
            Label("thread", $"runner-{info?.RunnerId ?? 0}"),
            Label("framework", "gauge"),
            Label("suite", suite),
        };

        var directory = specPath is null ? null : Path.GetDirectoryName(specPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(directory))
            labels.Add(Label("parentSuite", directory));
        if (!string.IsNullOrEmpty(specPath))
            labels.Add(Label("package", specPath.Replace('/', '.')));

        return labels;
    }

    private List<AllureLabel> SuiteHookLabels(ExecutionInfo? info) => BaseLabels(info, "Suite hooks", null);

    private static AllureLabel Label(string name, string value) => new() { Name = name, Value = value };

    private void WriteSpecHookResults(ExecutionInfo info, SpecContext spec, IEnumerable<ProtoHookFailure> failures,
        IEnumerable<string> messages, IEnumerable<string> screenshots, string title, string suffix)
    {
        var specPath = GetRelativeSpecPath(spec.FileName);
        foreach (var failure in failures)
        {
            var parameters = spec.DataTable is null ? null : new List<AllureParameter>
            {
                new() { Name = "Data table row", Value = (failure.TableRowIndex + 1).ToString() },
            };
            WriteHookResult($"{title}: {spec.Name}", $"{specPath}:{suffix}", failure, messages, screenshots,
                BaseLabels(info, spec.Name, specPath), parameters);
        }
    }

    private void WriteHookResult(string name, string fullName, ProtoHookFailure failure, IEnumerable<string> messages,
        IEnumerable<string> screenshots, List<AllureLabel> labels, List<AllureParameter>? parameters)
    {
        var now = Now;
        var testResult = new AllureTestResult
        {
            Uuid = Guid.NewGuid().ToString(),
            Name = name,
            FullName = fullName,
            HistoryId = ComputeHash(fullName + string.Join(",", parameters?.Select(p => $"{p.Name}={p.Value}") ?? [])),
            TestCaseId = ComputeHash(fullName),
            Labels = labels,
            Parameters = parameters,
            Status = AllureStatus.Broken,
            StatusDetails = Details(failure),
            Start = now,
            Stop = now,
        };

        testResult.Attachments = testResult.Attachments.With(WriteTextAttachment("messages", messages));
        foreach (var screenshot in screenshots)
            testResult.Attachments = testResult.Attachments.With(CopyScreenshot(screenshot, "screenshot"));
        testResult.Attachments = testResult.Attachments.With(CopyScreenshot(failure.FailureScreenshotFile, "failure screenshot"));

        WriteResultFile(testResult);
    }

    private void AddHookArtifacts(AllureTestResult target, string hookName, IEnumerable<string> messages,
        IEnumerable<string> screenshots, ProtoHookFailure? failure)
    {
        target.Attachments = target.Attachments.With(WriteTextAttachment($"{hookName} messages", messages));
        foreach (var screenshot in screenshots)
            target.Attachments = target.Attachments.With(CopyScreenshot(screenshot, $"{hookName} screenshot"));
        target.Attachments = target.Attachments.With(CopyScreenshot(failure?.FailureScreenshotFile, $"{hookName} failure screenshot"));
    }

    private void AddHookArtifacts(AllureStep target, string hookName, IEnumerable<string> messages,
        IEnumerable<string> screenshots, ProtoHookFailure? failure)
    {
        target.Attachments = target.Attachments.With(WriteTextAttachment($"{hookName} messages", messages));
        foreach (var screenshot in screenshots)
            target.Attachments = target.Attachments.With(CopyScreenshot(screenshot, $"{hookName} screenshot"));
        target.Attachments = target.Attachments.With(CopyScreenshot(failure?.FailureScreenshotFile, $"{hookName} failure screenshot"));
    }

    private static AllureStatusDetails Details(ProtoHookFailure failure)
        => new() { Message = failure.ErrorMessage, Trace = failure.StackTrace };

    private AllureAttachment? CopyScreenshot(string? screenshotFile, string name)
    {
        try
        {
            if (string.IsNullOrEmpty(screenshotFile))
                return null;

            var sourcePath = Path.Combine(options.ScreenshotsDirectory, screenshotFile);
            if (!File.Exists(sourcePath))
                return null;

            var extension = Path.GetExtension(screenshotFile);
            var attachmentName = $"{Guid.NewGuid()}-attachment{extension}";
            File.Copy(sourcePath, Path.Combine(_outputDir, attachmentName), true);

            return new AllureAttachment { Name = name, Source = attachmentName, Type = GetMimeType(extension) };
        }
        catch (IOException)
        {
            // A missing screenshot must never fail the report.
            return null;
        }
    }

    private AllureAttachment? WriteTextAttachment(string name, IEnumerable<string> lines)
    {
        var content = string.Join(Environment.NewLine, lines);
        if (content.Length == 0)
            return null;

        var attachmentName = $"{Guid.NewGuid()}-attachment.txt";
        File.WriteAllText(Path.Combine(_outputDir, attachmentName), content);
        return new AllureAttachment { Name = name, Source = attachmentName, Type = "text/plain" };
    }

    private AllureAttachment? WriteCsvAttachment(string name, ProtoTable table)
    {
        static string Row(ProtoTableRow? row) => row is null ? "" : string.Join(",", row.Cells.Select(EscapeCsv));
        static string EscapeCsv(string cell) => cell.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{cell.Replace("\"", "\"\"")}\""
            : cell;

        var lines = new[] { Row(table.Headers) }.Concat(table.Rows.Select(Row));
        var attachmentName = $"{Guid.NewGuid()}-attachment.csv";
        File.WriteAllText(Path.Combine(_outputDir, attachmentName), string.Join("\n", lines));
        return new AllureAttachment { Name = name, Source = attachmentName, Type = "text/csv" };
    }

    private void WriteResultFile(AllureTestResult testResult)
    {
        var filePath = Path.Combine(_outputDir, $"{testResult.Uuid}-result.json");
        var json = JsonSerializer.Serialize(testResult, AllureJsonContext.Default.AllureTestResult);
        File.WriteAllText(filePath, json);
    }

    private void CleanResults()
    {
        foreach (var pattern in new[] { "*-result.json", "*-container.json", "*-attachment*", "environment.properties" })
        {
            foreach (var file in Directory.EnumerateFiles(_outputDir, pattern))
                File.Delete(file);
        }
    }

    private void WriteEnvironment(ExecutionEndingRequest request)
    {
        Directory.CreateDirectory(_outputDir);

        var suite = request.SuiteResult;
        var sb = new StringBuilder();
        var projectName = suite?.ProjectName is { Length: > 0 } name ? name : request.CurrentExecutionInfo?.ProjectName;
        if (!string.IsNullOrEmpty(projectName))
            sb.AppendLine($"project={projectName}");
        if (!string.IsNullOrEmpty(suite?.Environment))
            sb.AppendLine($"environment={suite.Environment}");
        if (!string.IsNullOrEmpty(suite?.Tags))
            sb.AppendLine($"tags={suite.Tags}");
        sb.AppendLine("framework=gauge");

        File.WriteAllText(Path.Combine(_outputDir, "environment.properties"), sb.ToString());
    }

    /// <summary>
    /// Creates an allurerc.json enabling history. A file the user has customised is never touched;
    /// a previously generated one is updated when the results directory changes.
    /// </summary>
    private void WriteAllureConfig()
    {
        var configFile = Path.Combine(options.ProjectRoot, "allurerc.json");
        var historyPath = Path.GetRelativePath(options.ProjectRoot, Path.Combine(_outputDir, "allure-history.jsonl")).Replace('\\', '/');

        if (File.Exists(configFile))
        {
            if (!TryReadGeneratedHistoryPath(configFile, out var currentPath) || currentPath == historyPath)
                return;
        }

        File.WriteAllText(configFile, $$"""
            {
              "historyPath": "{{historyPath}}",
              "appendHistory": true
            }
            """);
    }

    private static bool TryReadGeneratedHistoryPath(string configFile, out string? historyPath)
    {
        historyPath = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configFile));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var names = document.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
            if (!names.SequenceEqual(["appendHistory", "historyPath"]))
                return false;

            historyPath = document.RootElement.GetProperty("historyPath").GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string GetRelativeSpecPath(string specFileName)
    {
        if (string.IsNullOrEmpty(specFileName))
            return "";

        var normalized = Path.GetRelativePath(options.ProjectRoot, specFileName)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return normalized.EndsWith(".spec", StringComparison.OrdinalIgnoreCase) ? normalized[..^5] : normalized;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };

    private static SpecKey GetSpecKey(ExecutionInfo info)
        => new(info.CurrentSpec.Name, info.CurrentSpec.FileName, info.RunnerId);

    private static ScenarioKey GetScenarioKey(ExecutionInfo info)
        => new(GetSpecKey(info), info.CurrentScenario.Name);

    private static StepKey GetStepKey(ExecutionInfo info)
        => new(GetScenarioKey(info), info.CurrentStep.Step.ActualStepText);

    private sealed class ScenarioContext(AllureTestResult result)
    {
        public AllureTestResult Result { get; } = result;
        public Stack<AllureStep> Concepts { get; } = new();
        public (AllureStatus Status, AllureStatusDetails? Details)? FirstFailure { get; set; }
        public List<AllureStep> CurrentSteps => Concepts.TryPeek(out var concept) ? concept.Steps! : Result.Steps!;
    }

    record SpecContext(string Name, string FileName, List<string> Tags, ProtoTable? DataTable);
    record SpecKey(string Name, string FileName, int RunnerId);
    record ScenarioKey(SpecKey SpecKey, string Name);
    record StepKey(ScenarioKey ScenarioKey, string Name);
}

internal static class AttachmentListExtensions
{
    public static List<AllureAttachment>? With(this List<AllureAttachment>? attachments, AllureAttachment? attachment)
    {
        if (attachment is null)
            return attachments;

        attachments ??= [];
        attachments.Add(attachment);
        return attachments;
    }
}
