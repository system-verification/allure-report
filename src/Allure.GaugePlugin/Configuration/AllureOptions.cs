// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using System;
using System.IO;

namespace Allure.GaugePlugin.Configuration;

internal sealed record AllureOptions
{
    public required string ProjectRoot { get; init; }
    public required string ResultsDirectory { get; init; }
    public string ScreenshotsDirectory { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool CleanResults { get; init; } = true;
    public bool CreateConfig { get; init; }
    public string? IssueLinkPattern { get; init; }
    public string? TmsLinkPattern { get; init; }

    public static AllureOptions FromEnvironment(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        string? Get(string name) => string.IsNullOrWhiteSpace(getVariable(name)) ? null : getVariable(name)!.Trim();
        bool GetBool(string name, bool defaultValue) => Get(name) is { } value ? bool.TryParse(value, out var b) ? b : defaultValue : defaultValue;

        var projectRoot = Path.GetFullPath(Get("GAUGE_PROJECT_ROOT") ?? ".");
        var reportsDir = Path.Combine(projectRoot, Get("gauge_reports_dir") ?? "reports");
        // ALLURE_REPORT_DIR is the legacy name, kept for backwards compatibility.
        var resultsDir = Get("ALLURE_RESULTS_DIR") ?? Get("ALLURE_REPORT_DIR");
        var screenshotsDir = Get("gauge_screenshots_dir");

        return new AllureOptions
        {
            ProjectRoot = projectRoot,
            ResultsDirectory = Path.GetFullPath(resultsDir is null
                ? Path.Combine(reportsDir, "allure-results")
                : Path.Combine(projectRoot, resultsDir)),
            ScreenshotsDirectory = screenshotsDir is null ? "" : Path.Combine(projectRoot, screenshotsDir),
            Enabled = GetBool("ALLURE_ENABLED", true),
            CleanResults = GetBool("ALLURE_CLEAN_RESULTS", true),
            CreateConfig = GetBool("ALLURE_CREATE_CONFIG", false),
            IssueLinkPattern = Get("ALLURE_LINK_ISSUE_PATTERN"),
            TmsLinkPattern = Get("ALLURE_LINK_TMS_PATTERN"),
        };
    }
}
