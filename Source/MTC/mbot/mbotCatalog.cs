using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MTC.mbot;

internal sealed record mbotInternalCommandGroup(
    string Type,
    IReadOnlyList<string> Commands);

internal sealed record mbotHotkeyBinding(
    string KeyDisplay,
    string ActionRef);

internal sealed record mbotMenuSurface(
    string Name,
    string SourceLabel,
    string Purpose);

internal enum mbotCommandKind
{
    Internal,
    Module
}

internal sealed record mbotCommandSpec(
    string Name,
    mbotCommandKind Kind,
    string Source,
    string Description,
    IReadOnlyList<string>? Aliases = null,
    bool ServerInteractive = false);

internal sealed record mbotAliasSpec(
    string Alias,
    string Canonical,
    string Reason);

internal static class mbotCatalog
{
    private static readonly ReadOnlyCollection<string> _categories = Array.AsReadOnly(new[]
    {
        "Modes",
        "Commands",
        "Daemons",
    });

    private static readonly ReadOnlyCollection<string> _types = Array.AsReadOnly(new[]
    {
        "General",
        "Defense",
        "Offense",
        "Resource",
        "Grid",
        "Cashing",
        "Data",
    });

    private static readonly ReadOnlyCollection<mbotInternalCommandGroup> _internalCommandGroups =
        Array.AsReadOnly(new[]
        {
            new mbotInternalCommandGroup("General", Array.AsReadOnly(new[]
            {
                "stopall", "stop", "listall", "reset", "emq", "bot", "relog", "tow",
                "refresh", "login", "logoff", "unlock", "lift", "with", "dep",
                "callin", "about", "cn", "extern", "twarp", "bwarp", "pwarp",
                "relog", "help", "switchbot",
            })),
            new mbotInternalCommandGroup("Defense", Array.AsReadOnly(Array.Empty<string>())),
            new mbotInternalCommandGroup("Offense", Array.AsReadOnly(new[]
            {
                "hkill", "kill", "htorp",
            })),
            new mbotInternalCommandGroup("Resource", Array.AsReadOnly(new[]
            {
                "refurb", "scrub",
            })),
            new mbotInternalCommandGroup("Grid", Array.AsReadOnly(new[]
            {
                "surround", "exit", "xenter", "mow",
            })),
            new mbotInternalCommandGroup("Cashing", Array.AsReadOnly(Array.Empty<string>())),
            new mbotInternalCommandGroup("Data", Array.AsReadOnly(new[]
            {
                "find", "pscan", "sector", "storeship", "setvar", "getvar",
            })),
        });

    private static readonly ReadOnlyCollection<string> _doubledCommands = Array.AsReadOnly(new[]
    {
        "parm",
        "params",
        "parms",
        "qss",
        "sec",
        "sect",
        "secto",
        "cn9",
        "logout",
        "emx",
        "smow",
        "port",
        "shipstore",
        "finder",
        "xenter",
        "status",
        "pinfo",
        "holotorp",
    });

    private static readonly ReadOnlyCollection<mbotHotkeyBinding> _defaultHotkeys =
        Array.AsReadOnly(new[]
        {
            new mbotHotkeyBinding("K", ":INTERNAL_COMMANDS~autokill"),
            new mbotHotkeyBinding("C", ":INTERNAL_COMMANDS~autocap"),
            new mbotHotkeyBinding("R", ":INTERNAL_COMMANDS~autorefurb"),
            new mbotHotkeyBinding("S", ":INTERNAL_COMMANDS~surround"),
            new mbotHotkeyBinding("H", ":INTERNAL_COMMANDS~htorp"),
            new mbotHotkeyBinding("T", ":INTERNAL_COMMANDS~twarpswitch"),
            new mbotHotkeyBinding("P", ":INTERNAL_COMMANDS~kit"),
            new mbotHotkeyBinding("Q", ":USER_INTERFACE~script_access"),
            new mbotHotkeyBinding("L", ":INTERNAL_COMMANDS~hkill"),
            new mbotHotkeyBinding("Tab", ":INTERNAL_COMMANDS~stopModules"),
            new mbotHotkeyBinding("D", ":INTERNAL_COMMANDS~kit"),
            new mbotHotkeyBinding("X", ":INTERNAL_COMMANDS~xenter"),
            new mbotHotkeyBinding("M", ":INTERNAL_COMMANDS~mowswitch"),
            new mbotHotkeyBinding("F", ":INTERNAL_COMMANDS~fotonswitch"),
            new mbotHotkeyBinding("Z", ":INTERNAL_COMMANDS~clear"),
            new mbotHotkeyBinding("~", ":MENUS~preferencesMenu"),
            new mbotHotkeyBinding("B", ":INTERNAL_COMMANDS~dock_shopper"),
        });

    private static readonly ReadOnlyCollection<mbotMenuSurface> _menuSurfaces =
        Array.AsReadOnly(new[]
        {
            new mbotMenuSurface("Wait For Command", ":BOT~WAIT_FOR_COMMAND", "Idle loop, routing triggers, and bot supervision hub."),
            new mbotMenuSurface("Preferences", ":MENUS~PREFERENCESMENU", "Primary multi-page configuration menu with hotkeys and location variables."),
            new mbotMenuSurface("Grid Prompt", ":USER_INTERFACE~GRIDPROMPT", "Single-key adjacent grid menu used for holo, density, and surround actions."),
            new mbotMenuSurface("Script Access", ":USER_INTERFACE~script_access", "Hotkey-driven script launcher sourced from hotkey script config."),
            new mbotMenuSurface("Fast Stop", "TWX_STOPALLFAST", "Confirmation menu used by internal stop-all helpers."),
        });

    private static readonly ReadOnlyCollection<mbotCommandSpec> _initialCommands =
        Array.AsReadOnly(new[]
        {
            new mbotCommandSpec("stopall", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~stopall", "Stop all bot-controlled scripts."),
            new mbotCommandSpec("stop", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~stop", "Stop the current bot module."),
            new mbotCommandSpec("listall", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~listall", "List active bot and module scripts."),
            new mbotCommandSpec("relog", mbotCommandKind.Module, "commands/general/relog.cts", "Perform relog/recovery flow via script.", ServerInteractive: true),
            new mbotCommandSpec("refresh", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~refresh", "Refresh cached bot state."),
            new mbotCommandSpec("reset", mbotCommandKind.Module, "commands/general/reset.cts", "Reset bot context via script."),
            new mbotCommandSpec("bot", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~bot", "Bot shell/control command."),
            new mbotCommandSpec("login", mbotCommandKind.Module, "commands/general/login.cts", "Login helper command should stay script-backed.", ServerInteractive: true),
            new mbotCommandSpec("logoff", mbotCommandKind.Internal, ":INTERNAL_COMMANDS~logoff", "Log off the current session. Kept out of native v1 because it is server-interactive.", ServerInteractive: true),
            new mbotCommandSpec("cn9", mbotCommandKind.Module, "commands/general/cn9.cts", "CN helper command should stay script-backed.", new[] { "cn" }, ServerInteractive: true),
            new mbotCommandSpec("help", mbotCommandKind.Module, "commands/general/help.cts", "Display command help."),

            new mbotCommandSpec("find", mbotCommandKind.Module, "commands/data/find.cts", "Find sectors/targets using bot search helpers.", new[] { "finder" }),
            new mbotCommandSpec("pscan", mbotCommandKind.Module, "commands/data/pscan.cts", "Planet or player scan helper.", new[] { "pinfo" }),
            new mbotCommandSpec("sector", mbotCommandKind.Module, "commands/data/sector.cts", "Sector information helper.", new[] { "sec", "sect", "secto" }),
            new mbotCommandSpec("param", mbotCommandKind.Module, "commands/data/param.cts", "Parameter helper command.", new[] { "parm", "params", "parms" }),
            new mbotCommandSpec("setvar", mbotCommandKind.Module, "commands/data/setvar.cts", "Set a shared bot variable."),
            new mbotCommandSpec("getvar", mbotCommandKind.Module, "commands/data/getvar.cts", "Read a shared bot variable."),

            new mbotCommandSpec("status", mbotCommandKind.Module, "commands/data/status.cts", "Show bot or game status.", new[] { "qss" }),
            new mbotCommandSpec("port", mbotCommandKind.Module, "commands/general/port.cts", "Port helper/build/upgrade command.", ServerInteractive: true),
            new mbotCommandSpec("storeship", mbotCommandKind.Module, "commands/data/storeship.cts", "Ship store helper.", new[] { "shipstore" }, ServerInteractive: true),
            new mbotCommandSpec("xenter", mbotCommandKind.Module, "commands/grid/xenter.cts", "Cross-enter helper command should stay script-backed.", ServerInteractive: true),
            new mbotCommandSpec("htorp", mbotCommandKind.Module, "commands/offense/htorp.cts", "Holotorp command surface.", new[] { "holotorp" }, ServerInteractive: true),
        });

    private static readonly ReadOnlyCollection<mbotAliasSpec> _initialAliases =
        Array.AsReadOnly(new[]
        {
            new mbotAliasSpec("qss", "status", "USER_INTERFACE normalizes qss to status."),
            new mbotAliasSpec("sec", "sector", "Short-form sector alias."),
            new mbotAliasSpec("sect", "sector", "Short-form sector alias."),
            new mbotAliasSpec("secto", "sector", "Short-form sector alias."),
            new mbotAliasSpec("cn", "cn9", "USER_INTERFACE normalizes cn to cn9."),
            new mbotAliasSpec("emx", "reset", "USER_INTERFACE normalizes emx to reset."),
            new mbotAliasSpec("finder", "find", "Compatibility alias for find."),
            new mbotAliasSpec("pinfo", "pscan", "USER_INTERFACE normalizes pinfo to pscan."),
            new mbotAliasSpec("parm", "param", "Short-form alias for param."),
            new mbotAliasSpec("params", "param", "Plural alias for param."),
            new mbotAliasSpec("parms", "param", "Plural alias for param."),
            new mbotAliasSpec("shipstore", "storeship", "USER_INTERFACE normalizes shipstore to storeship."),
            new mbotAliasSpec("holotorp", "htorp", "Compatibility alias for htorp."),
            new mbotAliasSpec("logout", "logoff", "Compatibility alias for logoff/logout surface."),
            new mbotAliasSpec("loguo", "logoff", "Tolerate the shorthand typo from planning notes."),
        });

    public static IReadOnlyList<string> Categories => _categories;
    public static IReadOnlyList<string> Types => _types;
    public static IReadOnlyList<mbotInternalCommandGroup> InternalCommandGroups => _internalCommandGroups;
    public static IReadOnlyList<string> DoubledCommands => _doubledCommands;
    public static IReadOnlyList<mbotHotkeyBinding> DefaultHotkeys => _defaultHotkeys;
    public static IReadOnlyList<mbotMenuSurface> MenuSurfaces => _menuSurfaces;
    public static IReadOnlyList<mbotCommandSpec> InitialCommands => _initialCommands;
    public static IReadOnlyList<mbotAliasSpec> InitialAliases => _initialAliases;

    public static IReadOnlyList<string> AllInternalCommands =>
        _internalCommandGroups
            .SelectMany(group => group.Commands)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string NormalizeCommandName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string normalized = input.Trim();
        int splitIndex = normalized.IndexOf(' ');
        if (splitIndex >= 0)
            normalized = normalized[..splitIndex];

        mbotAliasSpec? alias = _initialAliases.FirstOrDefault(item =>
            string.Equals(item.Alias, normalized, StringComparison.OrdinalIgnoreCase));

        return alias?.Canonical ?? normalized;
    }

    public static bool TryGetCommandSpec(string canonical, out mbotCommandSpec? command)
    {
        command = _initialCommands.FirstOrDefault(item =>
            string.Equals(item.Name, canonical, StringComparison.OrdinalIgnoreCase));
        return command != null;
    }

    public static bool IsInternalCommand(string input)
    {
        string canonical = NormalizeCommandName(input);
        return !string.IsNullOrWhiteSpace(canonical) &&
               AllInternalCommands.Contains(canonical, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryResolveInitialCommand(string input, out mbotCommandSpec? command, out string canonical)
    {
        canonical = NormalizeCommandName(input);
        return TryGetCommandSpec(canonical, out command);
    }
}
