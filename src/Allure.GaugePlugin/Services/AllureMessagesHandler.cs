// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Configuration;
using Allure.GaugePlugin.Results;
using Gauge.Messages;
using Grpc.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Allure.GaugePlugin.Services;

class AllureMessagesHandler(AllureResultWriter writer, AllureOptions options, CancellationTokenSource shutdownCts) : Reporter.ReporterBase
{
    private static readonly Task<Empty> EmptyResult = Task.FromResult(new Empty());

    public override Task<Empty> NotifyExecutionStarting(ExecutionStartingRequest request, ServerCallContext context)
        => Handle(() => writer.StartLaunch(request));

    public override Task<Empty> NotifyExecutionEnding(ExecutionEndingRequest request, ServerCallContext context)
        => Handle(() => writer.FinishLaunch(request));

    public override Task<Empty> NotifySpecExecutionStarting(SpecExecutionStartingRequest request, ServerCallContext context)
        => Handle(() => { if (request.SpecResult?.ProtoSpec != null) writer.StartSpec(request); });

    public override Task<Empty> NotifySpecExecutionEnding(SpecExecutionEndingRequest request, ServerCallContext context)
        => Handle(() => { if (request.SpecResult?.ProtoSpec != null) writer.FinishSpec(request); });

    public override Task<Empty> NotifyScenarioExecutionStarting(ScenarioExecutionStartingRequest request, ServerCallContext context)
        => Handle(() => { if (request.ScenarioResult?.ProtoItem != null) writer.StartScenario(request); });

    public override Task<Empty> NotifyScenarioExecutionEnding(ScenarioExecutionEndingRequest request, ServerCallContext context)
        => Handle(() => { if (request.ScenarioResult?.ProtoItem != null) writer.FinishScenario(request); });

    public override Task<Empty> NotifyConceptExecutionStarting(ConceptExecutionStartingRequest request, ServerCallContext context)
        => Handle(() => writer.StartConcept(request));

    public override Task<Empty> NotifyConceptExecutionEnding(ConceptExecutionEndingRequest request, ServerCallContext context)
        => Handle(() => writer.FinishConcept(request));

    public override Task<Empty> NotifyStepExecutionStarting(StepExecutionStartingRequest request, ServerCallContext context)
        => Handle(() => { if (request.StepResult != null) writer.StartStep(request); });

    public override Task<Empty> NotifyStepExecutionEnding(StepExecutionEndingRequest request, ServerCallContext context)
        => Handle(() => { if (request.StepResult != null) writer.FinishStep(request); });

    public override Task<Empty> NotifySuiteResult(SuiteExecutionResult request, ServerCallContext context)
        => EmptyResult;

    public override Task<Empty> Kill(KillProcessRequest request, ServerCallContext context)
    {
        try
        {
            return Handle(() =>
            {
                writer.FlushPending("Gauge stopped the plugin before this scenario finished");
                Console.WriteLine($"Allure results written to: {writer.OutputDirectory}");
            });
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    private Task<Empty> Handle(Action action, [CallerMemberName] string? caller = null)
    {
        if (!options.Enabled)
            return EmptyResult;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            // A reporting problem must never break the test run.
            Console.Error.WriteLine($"[allure-report] Error in {caller}: {ex}");
        }
        return EmptyResult;
    }
}
