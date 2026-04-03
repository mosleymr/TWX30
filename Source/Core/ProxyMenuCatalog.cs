using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TWXProxy.Core;

public sealed record QuickLoadEntry(string GroupName, string DisplayName, string RelativePath);

public sealed record QuickLoadGroup(string Name, IReadOnlyList<QuickLoadEntry> Entries);

public static class ProxyMenuCatalog
{
    private static readonly HashSet<string> ExcludedQuickDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".",
        "..",
        "include",
        "mombot",
        "mombot3",
        "mombot4p",
        "qubot",
        "zedbot",
    };

    public static IReadOnlyList<QuickLoadGroup> BuildQuickLoadGroups(string? programDir, string? scriptDirectory)
    {
        string scriptsRoot = ResolveScriptsRoot(programDir, scriptDirectory);
        if (!Directory.Exists(scriptsRoot))
            return Array.Empty<QuickLoadGroup>();

        var quickLoadMap = ReadQuickLoadSection(programDir);
        var groups = new SortedDictionary<string, List<QuickLoadEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(scriptsRoot))
        {
            string fileName = Path.GetFileName(file);
            string groupName = ResolveVirtualQuickGroup(fileName, quickLoadMap);
            AddQuickEntry(groups, groupName, fileName, fileName);
        }

        foreach (string directory in Directory.EnumerateDirectories(scriptsRoot))
        {
            string directoryName = Path.GetFileName(directory);
            if (ExcludedQuickDirectories.Contains(directoryName))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string fileName = Path.GetFileName(file);
                string relativePath = directoryName + "/" + fileName;
                AddQuickEntry(groups, directoryName, fileName, relativePath);
            }
        }

        return groups
            .Select(kvp => new QuickLoadGroup(
                kvp.Key,
                kvp.Value
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    public static IReadOnlyList<BotConfig> LoadBotConfigs(string? programDir, string? scriptDirectory)
    {
        string scriptsRoot = ResolveScriptsRoot(programDir, scriptDirectory);
        string configPath = ResolveConfigPath(programDir);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return Array.Empty<BotConfig>();

        var sections = ReadIniSections(configPath);
        var bots = new List<BotConfig>();

        foreach ((string sectionName, Dictionary<string, string> values) in sections)
        {
            if (!sectionName.StartsWith("bot:", StringComparison.OrdinalIgnoreCase))
                continue;

            string alias = sectionName["bot:".Length..].Trim();
            if (!values.TryGetValue("Name", out string? botName) || string.IsNullOrWhiteSpace(botName))
                continue;

            if (!values.TryGetValue("Script", out string? scriptList) || string.IsNullOrWhiteSpace(scriptList))
                continue;

            List<string> normalizedScripts = scriptList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(script => script.Replace('\\', '/'))
                .Where(script => !string.IsNullOrWhiteSpace(script))
                .ToList();

            string? firstScript = normalizedScripts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstScript))
                continue;

            string candidatePath = Path.Combine(scriptsRoot, firstScript.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(candidatePath))
                continue;

            var properties = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            bots.Add(new BotConfig
            {
                Alias = alias,
                Name = botName.Trim(),
                ScriptFile = firstScript,
                ScriptFiles = normalizedScripts,
                Description = values.TryGetValue("Description", out string? description) ? description : string.Empty,
                AutoStart = !values.TryGetValue("AutoStart", out string? autoStart) || ParseBool(autoStart, true),
                NameVar = values.TryGetValue("NameVar", out string? nameVar) ? nameVar : string.Empty,
                CommsVar = values.TryGetValue("CommsVar", out string? commsVar) ? commsVar : string.Empty,
                LoginScript = values.TryGetValue("LoginScript", out string? loginScript) ? loginScript : string.Empty,
                Theme = values.TryGetValue("Theme", out string? theme) ? theme : string.Empty,
                Properties = properties,
            });
        }

        return bots.ToArray();
    }

    private static void AddQuickEntry(
        IDictionary<string, List<QuickLoadEntry>> groups,
        string groupName,
        string displayName,
        string relativePath)
    {
        if (!groups.TryGetValue(groupName, out List<QuickLoadEntry>? entries))
        {
            entries = new List<QuickLoadEntry>();
            groups[groupName] = entries;
        }

        entries.Add(new QuickLoadEntry(groupName, displayName, relativePath.Replace('\\', '/')));
    }

    private static string ResolveVirtualQuickGroup(string fileName, IReadOnlyDictionary<string, string> quickLoadMap)
    {
        string virtualGroup = "Misc";
        if (fileName.StartsWith("__", StringComparison.Ordinal))
            virtualGroup = "_Favorite";
        else if (fileName.StartsWith("z-", StringComparison.OrdinalIgnoreCase))
            virtualGroup = "Zed / Archie";

        string virtualName = fileName.StartsWith("_", StringComparison.Ordinal)
            ? fileName[1..]
            : fileName;

        int underscoreIndex = virtualName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            string key = virtualName[..(underscoreIndex + 1)];
            if (quickLoadMap.TryGetValue(key, out string? configuredGroup) && !string.IsNullOrWhiteSpace(configuredGroup))
                virtualGroup = configuredGroup;
        }

        return virtualGroup;
    }

    private static Dictionary<string, string> ReadQuickLoadSection(string? programDir)
    {
        string configPath = ResolveConfigPath(programDir);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string sectionName, Dictionary<string, string> values) in ReadIniSections(configPath))
        {
            if (sectionName.Equals("QuickLoad", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<(string SectionName, Dictionary<string, string> Values)> ReadIniSections(string path)
    {
        var sections = new List<(string SectionName, Dictionary<string, string> Values)>();
        string? currentSection = null;
        Dictionary<string, string>? currentValues = null;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = line[1..^1].Trim();
                currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections.Add((currentSection, currentValues));
                continue;
            }

            if (currentValues == null)
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            string key = line[..equalsIndex].Trim();
            string value = line[(equalsIndex + 1)..].Trim();
            currentValues[key] = value;
        }

        return sections;
    }

    private static string ResolveScriptsRoot(string? programDir, string? scriptDirectory)
    {
        if (!string.IsNullOrWhiteSpace(scriptDirectory))
            return Path.GetFullPath(scriptDirectory);

        string root = string.IsNullOrWhiteSpace(programDir)
            ? GetDefaultProgramDir()
            : programDir;
        return Path.GetFullPath(Path.Combine(root, "scripts"));
    }

    private static string ResolveConfigPath(string? programDir)
    {
        if (File.Exists(SharedPaths.TwxpConfigPath))
            return SharedPaths.TwxpConfigPath;

        string root = string.IsNullOrWhiteSpace(programDir)
            ? GetDefaultProgramDir()
            : programDir;
        return Path.Combine(root, "twxp.cfg");
    }

    private static string GetDefaultProgramDir()
    {
        if (OperatingSystem.IsWindows())
            return WindowsInstallInfo.GetInstalledProgramDirOrDefault();

        return AppContext.BaseDirectory;
    }

    private static bool ParseBool(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase);
    }
}
