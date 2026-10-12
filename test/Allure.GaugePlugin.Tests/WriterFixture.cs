// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Configuration;
using Allure.GaugePlugin.Models;
using Allure.GaugePlugin.Results;
using Gauge.Messages;
using System.Text.Json;

namespace Allure.GaugePlugin.Tests;

internal sealed class ManualClock : TimeProvider
{
    public long Milliseconds { get; set; } = 1_700_000_000_000;
    public void Advance(long ms) => Milliseconds += ms;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Milliseconds);
}

/// <summary>Drives an <see cref="AllureResultWriter"/> with Gauge-shaped messages against a temp project.</summary>
internal sealed class WriterFixture : IDisposable
{
    public const string SpecName = "Login";
    public const string ScenarioName = "Valid login";

    public string Root { get; } = Directory.CreateTempSubdirectory("allure-gauge-").FullName;
    public string SpecFile => Path.Combine(Root, "specs", "auth", "login.spec");
    public AllureOptions Options { get; }
    public ManualClock Clock { get; } = new();
    public AllureResultWriter Writer { get; }

    public WriterFixture(Func<AllureOptions, AllureOptions>? configure = null)
    {
        var options = new AllureOptions
        {
            ProjectRoot = Root,
            ResultsDirectory = Path.Combine(Root, "reports", "allure-results"),
            ScreenshotsDirectory = Path.Combine(Root, "screenshots"),
        };
        Options = configure?.Invoke(options) ?? options;
        Writer = new AllureResultWriter(Options, Clock);
    }

    public ExecutionInfo Info(string? stepText = null, ExecuteStepRequest? step = null, int runnerId = 0) => new()
    {
        CurrentSpec = new SpecInfo { Name = SpecName, FileName = SpecFile },
        CurrentScenario = new ScenarioInfo { Name = ScenarioName },
        CurrentStep = new StepInfo { Step = step ?? new ExecuteStepRequest { ActualStepText = stepText ?? "", ParsedStepText = stepText ?? "" } },
        RunnerId = runnerId,
    };

    public void StartLaunch(ProtoSuiteResult? suite = null)
        => Writer.StartLaunch(new ExecutionStartingRequest { CurrentExecutionInfo = Info(), SuiteResult = suite ?? new ProtoSuiteResult() });

    public void FinishLaunch(ProtoSuiteResult? suite = null)
        => Writer.FinishLaunch(new ExecutionEndingRequest { CurrentExecutionInfo = Info(), SuiteResult = suite ?? new ProtoSuiteResult() });

    public void StartSpec(ProtoSpec? spec = null)
        => Writer.StartSpec(new SpecExecutionStartingRequest
        {
            CurrentExecutionInfo = Info(),
            SpecResult = new ProtoSpecResult { ProtoSpec = spec ?? Spec() },
        });

    public void FinishSpec(ProtoSpec? spec = null)
        => Writer.FinishSpec(new SpecExecutionEndingRequest
        {
            CurrentExecutionInfo = Info(),
            SpecResult = new ProtoSpecResult { ProtoSpec = spec ?? Spec() },
        });

    public ProtoSpec Spec(params string[] tags)
    {
        var spec = new ProtoSpec { SpecHeading = SpecName, FileName = SpecFile };
        spec.Tags.AddRange(tags);
        return spec;
    }

    public void StartScenario(ProtoItem? item = null, int runnerId = 0)
        => Writer.StartScenario(new ScenarioExecutionStartingRequest
        {
            CurrentExecutionInfo = Info(runnerId: runnerId),
            ScenarioResult = new ProtoScenarioResult { ProtoItem = item ?? ScenarioItem(Scenario()) },
        });

    public void FinishScenario(ProtoItem item, int runnerId = 0)
        => Writer.FinishScenario(new ScenarioExecutionEndingRequest
        {
            CurrentExecutionInfo = Info(runnerId: runnerId),
            ScenarioResult = new ProtoScenarioResult { ProtoItem = item },
        });

    public static ProtoScenario Scenario(ExecutionStatus status = ExecutionStatus.Passed, params string[] tags)
    {
        var scenario = new ProtoScenario { ScenarioHeading = ScenarioName, ExecutionStatus = status, Span = new Span { Start = 7 } };
        scenario.Tags.AddRange(tags);
        return scenario;
    }

    public static ProtoItem ScenarioItem(ProtoScenario scenario)
        => new() { ItemType = ProtoItem.Types.ItemType.Scenario, Scenario = scenario };

    public void StartStep(string text, ExecuteStepRequest? step = null)
        => Writer.StartStep(new StepExecutionStartingRequest { CurrentExecutionInfo = Info(text, step), StepResult = new ProtoStepResult() });

    public void FinishStep(string text, ProtoStepExecutionResult? result, ProtoStep? protoStep = null)
    {
        protoStep ??= new ProtoStep { ActualText = text };
        protoStep.StepExecutionResult = result;
        Writer.FinishStep(new StepExecutionEndingRequest
        {
            CurrentExecutionInfo = Info(text),
            StepResult = new ProtoStepResult { ProtoItem = new ProtoItem { ItemType = ProtoItem.Types.ItemType.Step, Step = protoStep } },
        });
    }

    public void StartConcept(string text)
        => Writer.StartConcept(new ConceptExecutionStartingRequest { CurrentExecutionInfo = Info(text) });

    public void FinishConcept(string text, ProtoStepExecutionResult? result)
        => Writer.FinishConcept(new ConceptExecutionEndingRequest
        {
            CurrentExecutionInfo = Info(text),
            StepResult = new ProtoStepResult
            {
                ProtoItem = new ProtoItem
                {
                    ItemType = ProtoItem.Types.ItemType.Concept,
                    Concept = new ProtoConcept { ConceptStep = new ProtoStep { ActualText = text }, ConceptExecutionResult = result },
                },
            },
        });

    public static ProtoStepExecutionResult Passed(long ms = 0)
        => new() { ExecutionResult = new ProtoExecutionResult { ExecutionTime = ms } };

    public static ProtoStepExecutionResult Failed(string message = "boom")
        => new() { ExecutionResult = new ProtoExecutionResult { Failed = true, ErrorMessage = message, StackTrace = "at x" } };

    public void RunPassingScenario(ProtoItem? start = null, ProtoItem? end = null)
    {
        StartScenario(start);
        StartStep("Do thing");
        FinishStep("Do thing", Passed());
        FinishScenario(end ?? start ?? ScenarioItem(Scenario()));
    }

    public List<AllureTestResult> ReadResults()
        => Directory.Exists(Options.ResultsDirectory)
            ? Directory.EnumerateFiles(Options.ResultsDirectory, "*-result.json")
                .Select(f => JsonSerializer.Deserialize(File.ReadAllText(f), AllureJsonContext.Default.AllureTestResult)!)
                .ToList()
            : [];

    public AllureTestResult SingleResult() => Assert.Single(ReadResults());

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}
