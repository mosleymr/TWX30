using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

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

    public static IReadOnlyList<NativeHaggleModeInfo> GetAvailableModes(IEnumerable<NativeHaggleModeInfo> extensionModes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modes = new List<NativeHaggleModeInfo>(BuiltIns.Count + 4);

        foreach (NativeHaggleModeInfo mode in BuiltIns)
        {
            if (seen.Add(mode.Id))
                modes.Add(mode);
        }

        foreach (NativeHaggleModeInfo mode in extensionModes.OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(mode.Id))
                modes.Add(mode);
        }

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

public static class NativeHaggleModeDiscovery
{
    private sealed class DiscoveryLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _sharedAssemblyName;

        public DiscoveryLoadContext(string assemblyPath, string sharedAssemblyName)
            : base($"TWXHaggleDiscovery:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
            _sharedAssemblyName = sharedAssemblyName;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, _sharedAssemblyName, StringComparison.OrdinalIgnoreCase))
                return null;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    public static IReadOnlyList<NativeHaggleModeInfo> DiscoverFromDirectories(IEnumerable<string> moduleDirectories)
    {
        var discovered = new Dictionary<string, NativeHaggleModeInfo>(StringComparer.OrdinalIgnoreCase);
        string sharedAssemblyName = typeof(NativeHaggleModeDiscovery).Assembly.GetName().Name ?? "TWXProxy";

        foreach (string assemblyPath in EnumerateCandidateAssemblies(moduleDirectories))
        {
            DiscoveryLoadContext? loadContext = null;

            try
            {
                loadContext = new DiscoveryLoadContext(assemblyPath, sharedAssemblyName);
                Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

                foreach (Type type in GetLoadableTypes(assembly))
                {
                    if (!typeof(NativeHaggleModeExtension).IsAssignableFrom(type) ||
                        type.IsAbstract ||
                        type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null) == null)
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type, nonPublic: true) is not NativeHaggleModeExtension extension)
                        continue;

                    NativeHaggleModeInfo info = extension.ModeInfo;
                    if (!NativeHaggleModes.IsBuiltIn(info.Id))
                        discovered[info.Id] = info;

                    if (extension is IDisposable disposable)
                        disposable.Dispose();
                }
            }
            catch (BadImageFormatException)
            {
            }
            catch
            {
            }
            finally
            {
                loadContext?.Unload();
            }
        }

        return NativeHaggleModeCatalog.GetAvailableModes(discovered.Values);
    }

    private static IEnumerable<string> EnumerateCandidateAssemblies(IEnumerable<string> moduleDirectories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in moduleDirectories.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string fullDirectory;
            try
            {
                fullDirectory = Path.GetFullPath(directory);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(fullDirectory))
                continue;

            foreach (string assembly in Directory.EnumerateFiles(fullDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(assembly))
                    yield return assembly;
            }

            foreach (string childDir in Directory.EnumerateDirectories(fullDirectory))
            {
                foreach (string assembly in Directory.EnumerateFiles(childDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    if (seen.Add(assembly))
                        yield return assembly;
                }
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null)!;
        }
    }
}
