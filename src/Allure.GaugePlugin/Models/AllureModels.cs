// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Allure.GaugePlugin.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AllureStatus>))]
public enum AllureStatus
{
    [JsonStringEnumMemberName("passed")] Passed,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("skipped")] Skipped,
    [JsonStringEnumMemberName("broken")] Broken,
}

public class AllureTestResult
{
    public string Uuid { get; set; } = "";
    public string? HistoryId { get; set; }
    public string? TestCaseId { get; set; }
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AllureLink>? Links { get; set; }
    public List<AllureLabel>? Labels { get; set; }
    public List<AllureParameter>? Parameters { get; set; }
    public AllureStatus Status { get; set; } = AllureStatus.Broken;
    public AllureStatusDetails? StatusDetails { get; set; }
    public long Start { get; set; }
    public long Stop { get; set; }
    public List<AllureStep>? Steps { get; set; }
    public List<AllureAttachment>? Attachments { get; set; }
}

public class AllureStep
{
    public string Name { get; set; } = "";
    public AllureStatus Status { get; set; } = AllureStatus.Broken;
    public AllureStatusDetails? StatusDetails { get; set; }
    public long Start { get; set; }
    public long Stop { get; set; }
    public List<AllureStep>? Steps { get; set; }
    public List<AllureAttachment>? Attachments { get; set; }
}

public class AllureLabel
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class AllureLink
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public class AllureParameter
{
    public string Name { get; set; } = "";
    public string? Value { get; set; }
}

public class AllureAttachment
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
}

public class AllureStatusDetails
{
    public string? Message { get; set; }
    public string? Trace { get; set; }
}

[JsonSerializable(typeof(AllureTestResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AllureJsonContext : JsonSerializerContext
{
}
