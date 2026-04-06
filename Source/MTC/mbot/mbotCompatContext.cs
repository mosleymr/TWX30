using System;
using System.Collections.Generic;
using System.IO;
using Core = TWXProxy.Core;

namespace MTC.mbot;

internal sealed record mbotCommandContext(
    string CommandLine,
    string CommandName,
    IReadOnlyList<string> Parameters,
    bool SelfCommand = false,
    string Route = "",
    string UserName = "");

internal sealed class mbotCompatContext
{
    public IReadOnlyDictionary<string, string> BuildVariableSnapshot(
        Core.ModDatabase? database,
        mbotConfig config,
        mbotSettings settings,
        mbotCommandContext context,
        string? lastLoadedModule = null)
    {
        string scriptRootLeaf = GetScriptRootLeaf(config.ScriptRoot);
        string mode = ReadCurrent("$BOT~MODE", "General");
        string homeSector = ReadCurrent("$MAP~HOME_SECTOR", "0");
        string safeShip = ReadCurrent("$BOT~SAFE_SHIP", "0");
        string safePlanet = ReadCurrent("$BOT~SAFE_PLANET", "0");
        string botIsDeaf = ReadCurrent("$BOT~BOTISDEAF", "0");
        string silentRunning = ReadCurrent("$BOT~SILENT_RUNNING", "0");

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$BOT~COMMAND"] = context.CommandName,
            ["$BOT~USER_COMMAND_LINE"] = context.CommandLine,
            ["$USER_COMMAND_LINE"] = context.CommandLine,
            ["$BOT~BOT_NAME"] = settings.BotName,
            ["$SWITCHBOARD~BOT_NAME"] = settings.BotName,
            ["$BOT~SELF_COMMAND"] = context.SelfCommand ? "1" : "0",
            ["$SWITCHBOARD~SELF_COMMAND"] = context.SelfCommand ? "1" : "0",
            ["$BOT~BOT_TEAM_NAME"] = settings.TeamName,
            ["$BOT~SUBSPACE"] = settings.SubspaceChannel.ToString(),
            ["$BOT~BOT_PASSWORD"] = settings.BotPassword,
            ["$BOT~PASSWORD"] = settings.LoginPassword,
            ["$BOT~MODE"] = string.IsNullOrWhiteSpace(mode) ? "General" : mode,
            ["$USER_INTERFACE~ROUTING"] = context.Route,
            ["$BOT~USERNAME"] = context.UserName,
            ["$BOT~COMMAND_CALLER"] = string.IsNullOrWhiteSpace(context.UserName) ? "self" : context.UserName,
            ["$BOT~PARMS"] = context.Parameters.Count.ToString(),
            ["$BOT~MOMBOT_DIRECTORY"] = scriptRootLeaf,
            ["$BOT~LAST_LOADED_MODULE"] = lastLoadedModule ?? ReadCurrent("$BOT~LAST_LOADED_MODULE", string.Empty),
            ["$MAP~STARDOCK"] = FormatSector(database?.DBHeader.StarDock),
            ["$MAP~RYLOS"] = FormatSector(database?.DBHeader.Rylos),
            ["$MAP~ALPHA_CENTAURI"] = FormatSector(database?.DBHeader.AlphaCentauri),
            ["$BOT~STARDOCK"] = FormatSector(database?.DBHeader.StarDock),
            ["$BOT~RYLOS"] = FormatSector(database?.DBHeader.Rylos),
            ["$BOT~ALPHA_CENTAURI"] = FormatSector(database?.DBHeader.AlphaCentauri),
            ["$MAP~HOME_SECTOR"] = homeSector,
            ["$BOT~HOME_SECTOR"] = homeSector,
            ["$BOT~SAFE_SHIP"] = safeShip,
            ["$BOT~SAFE_PLANET"] = safePlanet,
            ["$BOT~BOTISDEAF"] = botIsDeaf,
            ["$BOT~SILENT_RUNNING"] = silentRunning,
        };

        for (int i = 0; i < 8; i++)
        {
            string value = i < context.Parameters.Count ? context.Parameters[i] : string.Empty;
            vars[$"$BOT~PARM{i + 1}"] = value;
        }

        return vars;
    }

    public void ApplyToSession(
        Core.ModInterpreter? interpreter,
        Core.ModDatabase? database,
        mbotConfig config,
        mbotSettings settings,
        mbotCommandContext context,
        string? lastLoadedModule = null)
    {
        IReadOnlyDictionary<string, string> vars = BuildVariableSnapshot(database, config, settings, context, lastLoadedModule);
        foreach ((string name, string value) in vars)
            Core.ScriptRef.SetCurrentGameVar(name, value);

        if (interpreter == null)
            return;

        for (int i = 0; i < interpreter.Count; i++)
        {
            Core.Script? script = interpreter.GetScript(i);
            if (script == null)
                continue;

            foreach ((string name, string value) in vars)
                script.SetScriptVarIgnoreCase(name, value);
        }
    }

    private static string GetScriptRootLeaf(string scriptRoot)
    {
        if (string.IsNullOrWhiteSpace(scriptRoot))
            return "mombot";

        string trimmed = scriptRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? "mombot" : leaf;
    }

    private static string ReadCurrent(string name, string fallback)
    {
        string value = Core.ScriptRef.GetCurrentGameVar(name, fallback);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatSector(ushort? sector)
    {
        if (!sector.HasValue)
            return "0";

        ushort value = sector.Value;
        return value == 0 || value == ushort.MaxValue ? "0" : value.ToString();
    }
}
