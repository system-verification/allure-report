// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Models;
using Gauge.Messages;
using static Allure.GaugePlugin.Tests.WriterFixture;

namespace Allure.GaugePlugin.Tests;

public sealed class AllureResultWriterTests
{
    [Fact]
    public void PassingScenario_WritesResultWithSuiteHierarchy()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartSpec();
        f.RunPassingScenario();

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Passed, result.Status);
        Assert.Equal(ScenarioName, result.Name);
        Assert.Equal("specs/auth/login:7", result.FullName);
        Assert.Equal("Do thing", Assert.Single(result.Steps!).Name);
        Assert.Contains(result.Labels!, l => l is { Name: "suite", Value: SpecName });
        Assert.Contains(result.Labels!, l => l is { Name: "parentSuite", Value: "specs/auth" });
        Assert.Contains(result.Labels!, l => l is { Name: "feature", Value: SpecName });
    }

    [Fact]
    public void Labels_UseRunnerIdForThread_AndDoNotClaimALanguage()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario(runnerId: 3);
        f.FinishScenario(ScenarioItem(Scenario()), runnerId: 3);

        var result = f.SingleResult();
        Assert.Contains(result.Labels!, l => l is { Name: "thread", Value: "runner-3" });
        Assert.DoesNotContain(result.Labels!, l => l.Name == "language");
    }

    [Fact]
    public void Tags_MapToAllureLabelsAndLinks()
    {
        using var f = new WriterFixture(o => o with { IssueLinkPattern = "https://jira.example/browse/{}" });
        f.StartLaunch();
        f.StartSpec(f.Spec("smoke", "epic:Auth"));
        var item = ScenarioItem(Scenario(ExecutionStatus.Passed, "severity:critical", "issue:ABC-1", "tms:TC-9", "feature:Sign in"));
        f.StartScenario(item);
        f.FinishScenario(item);

        var result = f.SingleResult();
        Assert.Contains(result.Labels!, l => l is { Name: "tag", Value: "smoke" });
        Assert.Contains(result.Labels!, l => l is { Name: "epic", Value: "Auth" });
        Assert.Contains(result.Labels!, l => l is { Name: "severity", Value: "critical" });
        Assert.Contains(result.Labels!, l => l is { Name: "feature", Value: "Sign in" });
        Assert.DoesNotContain(result.Labels!, l => l is { Name: "feature", Value: SpecName });
        Assert.Contains(result.Links!, l => l is { Type: "issue", Name: "ABC-1", Url: "https://jira.example/browse/ABC-1" });
        Assert.Contains(result.Links!, l => l is { Type: "tms", Name: "TC-9" });
    }

    [Fact]
    public void Concept_StatusComesFromConceptResult_NotFromEarlierScenarioFailure()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();

        // A continue-on-failure step fails first...
        f.StartStep("Soft check");
        f.FinishStep("Soft check", Failed("soft"));

        // ...but the following concept passes.
        f.StartConcept("Log in as admin");
        f.StartStep("Enter credentials");
        f.FinishStep("Enter credentials", Passed());
        f.FinishConcept("Log in as admin", Passed());

        f.FinishScenario(ScenarioItem(Scenario(ExecutionStatus.Failed)));

        var result = f.SingleResult();
        var concept = result.Steps![1];
        Assert.Equal("Log in as admin", concept.Name);
        Assert.Equal(AllureStatus.Passed, concept.Status);
        Assert.Equal("Enter credentials", Assert.Single(concept.Steps!).Name);
        Assert.Equal(AllureStatus.Failed, result.Status);
        Assert.Equal("soft", result.StatusDetails!.Message);
    }

    [Fact]
    public void NestedConcepts_AreNestedAndFailureIsReported()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartConcept("Outer");
        f.StartConcept("Inner");
        f.StartStep("Leaf");
        f.FinishStep("Leaf", Failed());
        f.FinishConcept("Inner", Failed());
        f.FinishConcept("Outer", Failed());
        f.FinishScenario(ScenarioItem(Scenario(ExecutionStatus.Failed)));

        var outer = Assert.Single(f.SingleResult().Steps!);
        var inner = Assert.Single(outer.Steps!);
        Assert.Equal("Leaf", Assert.Single(inner.Steps!).Name);
        Assert.Equal(AllureStatus.Failed, outer.Status);
        Assert.Equal(AllureStatus.Failed, inner.Status);
    }

    [Fact]
    public void TableDrivenScenario_IsWrittenOnFinish_WithRowParameters()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartSpec();

        foreach (var user in new[] { "alice", "bob" })
        {
            var item = TableDrivenItem(user);
            f.RunPassingScenario(item);
        }

        var results = f.ReadResults();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(AllureStatus.Passed, r.Status));
        Assert.Equal(["alice", "bob"], results.Select(r => Assert.Single(r.Parameters!).Value).Order());
        Assert.NotEqual(results[0].HistoryId, results[1].HistoryId);
        Assert.Equal(results[0].TestCaseId, results[1].TestCaseId);
    }

    [Fact]
    public void SpecDataTable_RowValuesBecomeParameters()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        var spec = f.Spec();
        var table = new ProtoTable { Headers = Row("user", "role") };
        table.Rows.Add(Row("alice", "admin"));
        table.Rows.Add(Row("bob", "viewer"));
        spec.Items.Add(new ProtoItem { ItemType = ProtoItem.Types.ItemType.Table, Table = table });
        f.StartSpec(spec);

        var item = new ProtoItem
        {
            ItemType = ProtoItem.Types.ItemType.TableDrivenScenario,
            TableDrivenScenario = new ProtoTableDrivenScenario { Scenario = Scenario(), IsSpecTableDriven = true, TableRowIndex = 1 },
        };
        f.RunPassingScenario(item);

        var parameters = f.SingleResult().Parameters!;
        Assert.Contains(parameters, p => p is { Name: "user", Value: "bob" });
        Assert.Contains(parameters, p => p is { Name: "role", Value: "viewer" });
    }

    [Fact]
    public void StaticStepParameters_AreNotReportedAsTestParameters()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        var scenario = Scenario();
        var step = new ProtoStep { ActualText = "Open \"home\"" };
        step.Fragments.Add(new Fragment { FragmentType = Fragment.Types.FragmentType.Parameter, Parameter = new Parameter { Value = "home" } });
        scenario.ScenarioItems.Add(new ProtoItem { ItemType = ProtoItem.Types.ItemType.Step, Step = step });

        f.RunPassingScenario(ScenarioItem(scenario));

        Assert.Null(f.SingleResult().Parameters);
    }

    [Fact]
    public void SkippedStepWithoutExecutionResult_IsClosedAndSkipped()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("Skipped step");
        f.Clock.Advance(50);
        f.FinishStep("Skipped step", new ProtoStepExecutionResult { Skipped = true, SkippedReason = "not implemented" });
        f.FinishScenario(ScenarioItem(Scenario(ExecutionStatus.Skipped)));

        var step = Assert.Single(f.SingleResult().Steps!);
        Assert.Equal(AllureStatus.Skipped, step.Status);
        Assert.Equal("not implemented", step.StatusDetails!.Message);
        Assert.True(step.Stop >= step.Start && step.Stop != 0);
    }

    [Fact]
    public void StepDuration_UsesGaugeExecutionTime()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("Slow step");
        f.Clock.Advance(500); // event delivery delay on top of 200 ms of real work
        f.FinishStep("Slow step", Passed(ms: 200));
        f.FinishScenario(ScenarioItem(Scenario()));

        var step = Assert.Single(f.SingleResult().Steps!);
        Assert.Equal(200, step.Stop - step.Start);
    }

    [Fact]
    public void ScenarioHookFailure_IsBroken()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        var scenario = Scenario(ExecutionStatus.Failed);
        scenario.PreHookFailure = new ProtoHookFailure { ErrorMessage = "db down", StackTrace = "at hook" };
        scenario.PreHookMessages.Add("connecting");
        f.FinishScenario(ScenarioItem(scenario));

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Broken, result.Status);
        Assert.Equal("db down", result.StatusDetails!.Message);
        var attachment = Assert.Single(result.Attachments!);
        Assert.Equal("text/plain", attachment.Type);
        Assert.Equal("connecting", File.ReadAllText(Path.Combine(f.Options.ResultsDirectory, attachment.Source)));
    }

    [Fact]
    public void StepFailure_TakesPrecedenceOverPostHookFailure()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("Check");
        f.FinishStep("Check", Failed("assert"));
        var scenario = Scenario(ExecutionStatus.Failed);
        scenario.PostHookFailure = new ProtoHookFailure { ErrorMessage = "cleanup" };
        f.FinishScenario(ScenarioItem(scenario));

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Failed, result.Status);
        Assert.Equal("assert", result.StatusDetails!.Message);
    }

    [Fact]
    public void SkippedScenario_ReportsSkipReason()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        var scenario = Scenario(ExecutionStatus.Skipped);
        scenario.SkipErrors.Add("Step implementation not found");
        f.FinishScenario(ScenarioItem(scenario));

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Skipped, result.Status);
        Assert.Equal("Step implementation not found", result.StatusDetails!.Message);
    }

    [Fact]
    public void BeforeSpecHookFailure_WritesBrokenResult()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        var spec = f.Spec();
        spec.PreHookFailures.Add(new ProtoHookFailure { ErrorMessage = "browser failed to start" });
        f.StartSpec(spec);

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Broken, result.Status);
        Assert.Equal($"Before spec hook: {SpecName}", result.Name);
        Assert.Equal("browser failed to start", result.StatusDetails!.Message);
    }

    [Fact]
    public void SuiteHookFailures_WriteBrokenResults()
    {
        using var f = new WriterFixture();
        f.StartLaunch(new ProtoSuiteResult { PreHookFailure = new ProtoHookFailure { ErrorMessage = "before" } });
        f.FinishLaunch(new ProtoSuiteResult { PostHookFailure = new ProtoHookFailure { ErrorMessage = "after" } });

        var results = f.ReadResults();
        Assert.Equal(["after", "before"], results.Select(r => r.StatusDetails!.Message).Order());
        Assert.All(results, r => Assert.Equal(AllureStatus.Broken, r.Status));
    }

    [Fact]
    public void UnfinishedScenario_IsFlushedAsBrokenAtExecutionEnd()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("Hangs");
        f.FinishLaunch();

        var result = f.SingleResult();
        Assert.Equal(AllureStatus.Broken, result.Status);
        var step = Assert.Single(result.Steps!);
        Assert.Equal(AllureStatus.Broken, step.Status);
        Assert.NotEqual(0, step.Stop);
    }

    [Fact]
    public void ParallelStreams_CleanOnceAndFlushOnlyAfterLastStreamEnds()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartLaunch();
        f.RunPassingScenario();
        f.StartLaunch(); // a late stream must not wipe results already written
        f.FinishLaunch();
        f.FinishLaunch();
        Assert.Single(f.ReadResults());
        Assert.False(File.Exists(Path.Combine(f.Options.ResultsDirectory, "environment.properties")));

        f.FinishLaunch();
        Assert.True(File.Exists(Path.Combine(f.Options.ResultsDirectory, "environment.properties")));
    }

    [Fact]
    public void UnexecutedSteps_AreAddedAsSkipped()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("First");
        f.FinishStep("First", Failed());

        var scenario = Scenario(ExecutionStatus.Failed);
        scenario.ScenarioItems.Add(new ProtoItem { ItemType = ProtoItem.Types.ItemType.Step, Step = new ProtoStep { ActualText = "First", StepExecutionResult = Failed() } });
        scenario.ScenarioItems.Add(new ProtoItem { ItemType = ProtoItem.Types.ItemType.Step, Step = new ProtoStep { ActualText = "Second" } });
        f.FinishScenario(ScenarioItem(scenario));

        var steps = f.SingleResult().Steps!;
        Assert.Equal(["First", "Second"], steps.Select(s => s.Name));
        Assert.Equal(AllureStatus.Skipped, steps[1].Status);
    }

    [Fact]
    public void UnexecutedSteps_AreNotAddedWhenGaugeSendsNoStepResults()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("First");
        f.FinishStep("First", Passed());
        var scenario = Scenario();
        scenario.ScenarioItems.Add(new ProtoItem { ItemType = ProtoItem.Types.ItemType.Step, Step = new ProtoStep { ActualText = "First" } });
        f.FinishScenario(ScenarioItem(scenario));

        Assert.Single(f.SingleResult().Steps!);
    }

    [Fact]
    public void StepArtifacts_MessagesAndTablesAreAttached()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        f.StartScenario();
        var table = new ProtoTable { Headers = Row("name", "note") };
        table.Rows.Add(Row("a", "x,y"));
        var request = new ExecuteStepRequest { ActualStepText = "Create users <table>", ParsedStepText = "Create users {}" };
        request.Parameters.Add(new Parameter { ParameterType = Parameter.Types.ParameterType.Table, Name = "table", Table = table });
        f.StartStep("Create users <table>", request);
        var result = Passed();
        result.ExecutionResult.Message.Add("created 1 user");
        f.FinishStep("Create users <table>", result);
        f.FinishScenario(ScenarioItem(Scenario()));

        var step = Assert.Single(f.SingleResult().Steps!);
        var csv = Assert.Single(step.Attachments!, a => a.Type == "text/csv");
        Assert.Equal("name,note\na,\"x,y\"", File.ReadAllText(Path.Combine(f.Options.ResultsDirectory, csv.Source)));
        var messages = Assert.Single(step.Attachments!, a => a.Type == "text/plain");
        Assert.Equal("created 1 user", File.ReadAllText(Path.Combine(f.Options.ResultsDirectory, messages.Source)));
    }

    [Fact]
    public void Screenshots_AreCopiedFromGaugeScreenshotDirectory()
    {
        using var f = new WriterFixture();
        Directory.CreateDirectory(f.Options.ScreenshotsDirectory);
        File.WriteAllText(Path.Combine(f.Options.ScreenshotsDirectory, "shot.png"), "png");
        f.StartLaunch();
        f.StartScenario();
        f.StartStep("Look");
        var result = Failed();
        result.ExecutionResult.FailureScreenshotFile = "shot.png";
        result.ExecutionResult.ScreenshotFiles.Add("missing.png");
        f.FinishStep("Look", result);
        f.FinishScenario(ScenarioItem(Scenario(ExecutionStatus.Failed)));

        var attachment = Assert.Single(Assert.Single(f.SingleResult().Steps!).Attachments!);
        Assert.Equal("image/png", attachment.Type);
        Assert.True(File.Exists(Path.Combine(f.Options.ResultsDirectory, attachment.Source)));
    }

    [Fact]
    public void Cleanup_RemovesPreviousRunButKeepsHistory_AndCanBeDisabled()
    {
        using var f = new WriterFixture();
        var dir = f.Options.ResultsDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "old-result.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "old-container.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "allure-history.jsonl"), "");

        new WriterFixture(o => o with { ProjectRoot = f.Root, ResultsDirectory = dir, CleanResults = false }).Writer
            .StartLaunch(new ExecutionStartingRequest());
        Assert.True(File.Exists(Path.Combine(dir, "old-result.json")));

        f.StartLaunch();
        Assert.Equal(["allure-history.jsonl"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void AllureConfig_IsNotCreatedByDefault()
    {
        using var f = new WriterFixture();
        f.StartLaunch();
        Assert.False(File.Exists(Path.Combine(f.Root, "allurerc.json")));
    }

    [Fact]
    public void AllureConfig_WhenEnabled_IsCreatedAndKeptInSyncButUserFilesAreUntouched()
    {
        using var f = new WriterFixture(o => o with { CreateConfig = true });
        var config = Path.Combine(f.Root, "allurerc.json");

        File.WriteAllText(config, """{ "historyPath": "old/allure-history.jsonl", "appendHistory": true }""");
        f.StartLaunch();
        Assert.Contains("\"reports/allure-results/allure-history.jsonl\"", File.ReadAllText(config));

        const string custom = """{ "name": "My report", "historyPath": "x.jsonl", "appendHistory": true }""";
        File.WriteAllText(config, custom);
        new WriterFixture(o => o with { ProjectRoot = f.Root, CreateConfig = true }).Writer.StartLaunch(new ExecutionStartingRequest());
        Assert.Equal(custom, File.ReadAllText(config));
    }

    private static ProtoTableRow Row(params string[] cells)
    {
        var row = new ProtoTableRow();
        row.Cells.AddRange(cells);
        return row;
    }

    private static ProtoItem TableDrivenItem(string user)
    {
        var scenarioTable = new ProtoTable { Headers = Row("user") };
        scenarioTable.Rows.Add(Row("alice"));
        scenarioTable.Rows.Add(Row("bob"));
        var rowTable = new ProtoTable { Headers = Row("user") };
        rowTable.Rows.Add(Row(user));

        return new ProtoItem
        {
            ItemType = ProtoItem.Types.ItemType.TableDrivenScenario,
            TableDrivenScenario = new ProtoTableDrivenScenario
            {
                Scenario = Scenario(),
                IsScenarioTableDriven = true,
                ScenarioTableRowIndex = user == "alice" ? 0 : 1,
                ScenarioDataTable = scenarioTable,
                ScenarioTableRow = rowTable,
            },
        };
    }
}
