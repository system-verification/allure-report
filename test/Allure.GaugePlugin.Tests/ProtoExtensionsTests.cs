// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Configuration;
using Allure.GaugePlugin.Extensions;
using Gauge.Messages;

namespace Allure.GaugePlugin.Tests;

public sealed class ProtoExtensionsTests
{
    private static ExecuteStepRequest Step(string parsed, params Parameter[] parameters)
    {
        var step = new ExecuteStepRequest { ActualStepText = "actual", ParsedStepText = parsed };
        step.Parameters.AddRange(parameters);
        return step;
    }

    private static Parameter Value(string value) => new() { Value = value };

    [Fact]
    public void ReplacesPlaceholdersWithQuotedValues()
        => Assert.Equal("Login as `bob` with `secret`", Step("Login as {} with {}", Value("bob"), Value("secret")).GetStepName());

    [Fact]
    public void ValueContainingPlaceholder_IsNotReplacedByNextParameter()
        => Assert.Equal("Send `{}` and `x`", Step("Send {} and {}", Value("{}"), Value("x")).GetStepName());

    [Fact]
    public void MoreParametersThanPlaceholders_DoesNotThrow()
        => Assert.Equal("Only `a` here", Step("Only {} here", Value("a"), Value("b")).GetStepName());

    [Fact]
    public void TableParameter_UsesName()
        => Assert.Equal("Users `table`", Step("Users {}",
            new Parameter { ParameterType = Parameter.Types.ParameterType.Table, Name = "table", Value = "ignored" }).GetStepName());

    [Fact]
    public void NoParameters_UsesActualText()
        => Assert.Equal("actual", Step("Plain step").GetStepName());

    [Fact]
    public void ProtoStep_BuildsNameFromFragments()
    {
        var step = new ProtoStep { ActualText = "Open \"home\" page" };
        step.Fragments.Add(new Fragment { FragmentType = Fragment.Types.FragmentType.Text, Text = "Open " });
        step.Fragments.Add(new Fragment { FragmentType = Fragment.Types.FragmentType.Parameter, Parameter = Value("home") });
        step.Fragments.Add(new Fragment { FragmentType = Fragment.Types.FragmentType.Text, Text = " page" });

        Assert.Equal("Open `home` page", step.GetStepName());
    }
}

public sealed class AllureOptionsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "project"));

    private static AllureOptions Load(params (string Name, string Value)[] variables)
    {
        var all = variables.Append(("GAUGE_PROJECT_ROOT", Root)).ToDictionary(v => v.Item1, v => v.Item2);
        return AllureOptions.FromEnvironment(name => all.GetValueOrDefault(name));
    }

    [Fact]
    public void Defaults()
    {
        var options = Load();
        Assert.Equal(Path.Combine(Root, "reports", "allure-results"), options.ResultsDirectory);
        Assert.True(options.Enabled);
        Assert.True(options.CleanResults);
        Assert.False(options.CreateConfig);
    }

    [Fact]
    public void ResultsDirectory_IsRelativeToProjectRoot()
        => Assert.Equal(Path.Combine(Root, "out", "allure"), Load(("ALLURE_RESULTS_DIR", "out/allure")).ResultsDirectory);

    [Fact]
    public void LegacyReportDirVariable_IsStillHonoured()
        => Assert.Equal(Path.Combine(Root, "legacy"), Load(("ALLURE_REPORT_DIR", "legacy")).ResultsDirectory);

    [Fact]
    public void DefaultFollowsGaugeReportsDir()
        => Assert.Equal(Path.Combine(Root, "out", "allure-results"), Load(("gauge_reports_dir", "out")).ResultsDirectory);

    [Theory]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("true", true)]
    [InlineData("garbage", true)]
    public void Enabled_ParsesBooleans(string value, bool expected)
        => Assert.Equal(expected, Load(("ALLURE_ENABLED", value)).Enabled);
}
