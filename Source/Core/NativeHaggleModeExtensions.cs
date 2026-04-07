using System;
using System.Collections.Generic;
using System.Linq;

namespace TWXProxy.Core;

public sealed class NativeHaggleModeInfo
{
    public NativeHaggleModeInfo(string id, string displayName, bool isBuiltIn)
    {
        Id = NativeHaggleModes.Normalize(id);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        IsBuiltIn = isBuiltIn;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public bool IsBuiltIn { get; }
}

internal abstract class NativeHaggleModeExtension
{
    protected NativeHaggleModeExtension(string id, string displayName)
    {
        ModeInfo = new NativeHaggleModeInfo(id, displayName, isBuiltIn: false);
    }

    public NativeHaggleModeInfo ModeInfo { get; }

    public abstract long ComputeBid(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session, long offer);

    public virtual void OnOutcome(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session, bool success, string reason)
    {
    }

    public virtual string DescribeState(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session) => "modeState=n/a";
}

internal static class NativeHaggleModeCatalog
{
    private static readonly IReadOnlyList<NativeHaggleModeInfo> BuiltIns = new[]
    {
        new NativeHaggleModeInfo(NativeHaggleModes.ClampHeuristic, "EPHaggle", isBuiltIn: true),
        new NativeHaggleModeInfo(NativeHaggleModes.ServerDerived, "Enhanced Haggle", isBuiltIn: true),
        new NativeHaggleModeInfo(NativeHaggleModes.BlendHeuristic, "Blend Heuristic", isBuiltIn: true),
        new NativeHaggleModeInfo(NativeHaggleModes.Baseline, "Baseline", isBuiltIn: true),
    };

    public static IReadOnlyList<NativeHaggleModeInfo> GetBuiltIns() => BuiltIns;

    public static IReadOnlyList<NativeHaggleModeInfo> GetAvailableModes(IEnumerable<NativeHaggleModeExtension> extensions)
    {
        var modes = new List<NativeHaggleModeInfo>(BuiltIns);
        modes.AddRange(extensions
            .Select(extension => extension.ModeInfo)
            .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase));
        return modes;
    }

    public static string GetDisplayName(string? modeId, IEnumerable<NativeHaggleModeExtension>? extensions = null)
    {
        string normalized = NativeHaggleModes.Normalize(modeId);

        NativeHaggleModeInfo? builtIn = BuiltIns.FirstOrDefault(info =>
            string.Equals(info.Id, normalized, StringComparison.OrdinalIgnoreCase));
        if (builtIn != null)
            return builtIn.DisplayName;

        NativeHaggleModeInfo? extension = extensions?.Select(item => item.ModeInfo).FirstOrDefault(info =>
            string.Equals(info.Id, normalized, StringComparison.OrdinalIgnoreCase));
        return extension?.DisplayName ?? normalized;
    }
}
