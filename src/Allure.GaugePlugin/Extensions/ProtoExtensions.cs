// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Gauge.Messages;
using System;
using System.Text;

namespace Allure.GaugePlugin.Extensions;

internal static class ProtoExtensions
{
    public static string GetStepName(this ExecuteStepRequest step)
    {
        var parsed = step.ParsedStepText;
        if (step.Parameters.Count == 0 || string.IsNullOrEmpty(parsed))
            return step.ActualStepText;

        var sb = new StringBuilder();
        var position = 0;

        foreach (var parameter in step.Parameters)
        {
            // Search only in the remaining template so values containing "{}" are never re-scanned.
            var index = parsed.IndexOf("{}", position, StringComparison.Ordinal);
            if (index < 0)
                break;

            sb.Append(parsed, position, index - position);
            AppendParameter(sb, parameter);
            position = index + 2;
        }

        sb.Append(parsed, position, parsed.Length - position);
        return sb.ToString();
    }

    public static string GetStepName(this ProtoStep step)
    {
        if (step.Fragments.Count == 0)
            return step.ActualText;

        var sb = new StringBuilder();
        foreach (var fragment in step.Fragments)
        {
            if (fragment.FragmentType == Fragment.Types.FragmentType.Parameter && fragment.Parameter is not null)
                AppendParameter(sb, fragment.Parameter);
            else
                sb.Append(fragment.Text);
        }
        return sb.ToString();
    }

    public static bool IsTable(this Parameter parameter) => parameter.ParameterType is
        Parameter.Types.ParameterType.Table or
        Parameter.Types.ParameterType.SpecialTable;

    private static void AppendParameter(StringBuilder sb, Parameter parameter)
    {
        var value = parameter.IsTable() ? parameter.Name : parameter.Value;
        if (!string.IsNullOrEmpty(value))
            sb.Append('`').Append(value).Append('`');
    }
}
