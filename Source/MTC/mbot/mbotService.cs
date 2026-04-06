using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core = TWXProxy.Core;

namespace MTC.mbot;

internal enum mbotDispatchKind
{
    Invalid,
    Native,
    Script,
    Unsupported,
}

internal sealed record mbotDispatchResult(
    bool Success,
    mbotDispatchKind Kind,
    string CanonicalCommand,
    string Message,
    string? ScriptReference = null);

internal sealed class mbotService
{
    private Core.GameInstance? _gameInstance;
    private Core.ModDatabase? _database;
    private Core.ModInterpreter? _interpreter;
    private mbotConfig _config = new();
    private readonly HashSet<string> _authorizedUsers = new(StringComparer.OrdinalIgnoreCase);

    public mbotConfig Config => _config;
    public mbotWatcher Watcher { get; } = new();
    public mbotCompatContext Compat { get; } = new();
    public bool IsAttached => _gameInstance != null;
    public bool Enabled => _config.Enabled;
    public IReadOnlyList<mbotInternalCommandGroup> InternalCommandGroups => mbotCatalog.InternalCommandGroups;
    public IReadOnlyList<mbotHotkeyBinding> DefaultHotkeys => mbotCatalog.DefaultHotkeys;
    public IReadOnlyList<mbotMenuSurface> MenuSurfaces => mbotCatalog.MenuSurfaces;
    public IReadOnlyList<mbotCommandSpec> InitialCommands => mbotCatalog.InitialCommands;
    public IReadOnlyList<mbotAliasSpec> InitialAliases => mbotCatalog.InitialAliases;
    public mbotSettings Settings => mbotSettings.Load();

    public void AttachSession(
        Core.GameInstance? gameInstance,
        Core.ModDatabase? database,
        Core.ModInterpreter? interpreter,
        mbotConfig? config)
    {
        _gameInstance = gameInstance;
        _database = database;
        _interpreter = interpreter;
        _config = config ?? new mbotConfig();
        ResetAuthorizedUsers();

        if (_config.WatcherEnabled)
            Watcher.Attach(gameInstance, database);
        else
            Watcher.Detach();
    }

    public void DetachSession()
    {
        Watcher.Detach();
        _authorizedUsers.Clear();
        _gameInstance = null;
        _database = null;
        _interpreter = null;
    }

    public void ApplyConfig(mbotConfig? config)
    {
        _config = config ?? new mbotConfig();
        ResetAuthorizedUsers();

        if (_config.WatcherEnabled && _gameInstance != null)
            Watcher.Attach(_gameInstance, _database);
        else
            Watcher.Detach();
    }

    public IReadOnlyList<Core.RunningScriptInfo> GetRunningScripts()
    {
        return Core.ProxyGameOperations.GetRunningScripts(_interpreter);
    }

    public string BuildModulePath(string category, string type, string commandName, bool hidden = false)
    {
        string fileName = hidden ? $"_{commandName}.cts" : $"{commandName}.cts";
        return Path.Combine(_config.ScriptRoot, category, type, fileName);
    }

    public bool TryLoadScript(string scriptPath, out string? error)
    {
        error = null;
        if (_interpreter == null)
        {
            error = "No active interpreter.";
            return false;
        }

        try
        {
            Core.ProxyGameOperations.LoadScript(_interpreter, scriptPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool StopScriptByName(string scriptName)
    {
        return Core.ProxyGameOperations.StopScriptByName(_interpreter, scriptName);
    }

    public void StopAllNonSystemScripts()
    {
        Core.ProxyGameOperations.StopAllScripts(_interpreter, includeSystemScripts: false);
    }

    public bool TryResolveInitialCommand(string input, out mbotCommandSpec? command, out string canonical)
    {
        return mbotCatalog.TryResolveInitialCommand(input, out command, out canonical);
    }

    public bool ObserveServerLine(string line)
    {
        Watcher.ObserveServerLine(line);

        if (!Enabled || string.IsNullOrWhiteSpace(line))
            return false;

        if (TryHandlePageLogin(line))
            return true;

        if (!TryParseIncomingCommand(line, out mbotCommandContext? context))
            return false;

        if (context == null)
            return false;

        if (context.CommandLine.Length == 0)
        {
            mbotSettings settings = Settings;
            PublishMessage($"mbot: you are logged into this bot. Use {settings.BotName} help for commands.");
            return true;
        }

        if (!IsAuthorized(context))
            return false;

        if (IsBlockedRemoteControlCommand(context))
            return false;

        ExecuteCommandLine(
            context.CommandLine,
            selfCommand: context.SelfCommand,
            route: context.Route,
            userName: context.UserName);

        return true;
    }

    public IReadOnlyList<mbotDispatchResult> ExecuteCommandLine(
        string input,
        bool selfCommand = true,
        string route = "self",
        string userName = "self")
    {
        var results = new List<mbotDispatchResult>();
        foreach (string segment in SplitCommandSegments(input))
            results.Add(ExecuteSingleCommand(segment, selfCommand, route, userName));

        return results;
    }

    private mbotDispatchResult ExecuteSingleCommand(
        string input,
        bool selfCommand,
        string route,
        string userName)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new mbotDispatchResult(false, mbotDispatchKind.Invalid, string.Empty, "Empty mbot command.");

        if (!TryResolveInitialCommand(input, out mbotCommandSpec? command, out string canonical) || command == null)
        {
            string invalidCommand = GetWords(input).FirstOrDefault() ?? input.Trim();
            string message = $"mbot: '{invalidCommand}' is not a valid command.";
            PublishMessage(message);
            return new mbotDispatchResult(false, mbotDispatchKind.Invalid, invalidCommand, message);
        }

        List<string> words = GetWords(input);
        if (words.Count == 0)
        {
            string message = "mbot: Empty mbot command.";
            PublishMessage(message);
            return new mbotDispatchResult(false, mbotDispatchKind.Invalid, string.Empty, message);
        }

        List<string> parameters = words.Skip(1).Take(8).ToList();
        string normalizedLine = BuildNormalizedCommandLine(canonical, parameters);
        string? scriptReference = command.Kind == mbotCommandKind.Module ? TryResolveScriptReference(command) : null;
        var context = new mbotCommandContext(
            normalizedLine,
            canonical,
            parameters,
            selfCommand,
            route,
            userName);

        Compat.ApplyToSession(_interpreter, _database, _config, Settings, context, scriptReference);

        if (command.Kind == mbotCommandKind.Internal)
            return ExecuteNative(command, context);

        if (scriptReference == null)
        {
            string message = $"mbot: no module path is available for '{canonical}'.";
            PublishMessage(message);
            return new mbotDispatchResult(false, mbotDispatchKind.Unsupported, canonical, message);
        }

        if (!TryLoadScript(scriptReference, out string? error))
        {
            string message = $"mbot: failed to load '{scriptReference}': {error}";
            PublishMessage(message);
            return new mbotDispatchResult(false, mbotDispatchKind.Script, canonical, message, scriptReference);
        }

        Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", scriptReference);
        string loadedMessage = $"mbot: loaded {canonical} ({scriptReference}).";
        PublishMessage(loadedMessage);
        return new mbotDispatchResult(true, mbotDispatchKind.Script, canonical, loadedMessage, scriptReference);
    }

    private mbotDispatchResult ExecuteNative(mbotCommandSpec command, mbotCommandContext context)
    {
        string canonical = context.CommandName;

        switch (canonical.ToLowerInvariant())
        {
            case "stopall":
                StopAllNonSystemScripts();
                Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
                Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
                return PublishNativeResult(canonical, "mbot stopped all non-system scripts.");

            case "stop":
                return ExecuteStop(context);

            case "listall":
                return ExecuteListAll(canonical);

            case "refresh":
                Compat.ApplyToSession(_interpreter, _database, _config, Settings, context);
                return PublishNativeResult(canonical, "mbot refreshed its command context from current savevar/loadvar state.");

            case "bot":
                return ExecuteBotStatus(canonical);

            case "logoff":
                return PublishUnsupported(canonical, "mbot leaves logoff/logout script-backed for now because it is a server-interactive flow.");

            default:
                return PublishUnsupported(canonical, $"mbot does not have a native handler for '{canonical}' yet.");
        }
    }

    private mbotDispatchResult ExecuteStop(mbotCommandContext context)
    {
        string selector = context.Parameters.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            string lastLoaded = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
            if (!string.IsNullOrWhiteSpace(lastLoaded) && StopScriptByName(lastLoaded))
            {
                Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
                return PublishNativeResult(context.CommandName, $"mbot stopped {lastLoaded}.");
            }

            return PublishUnsupported(context.CommandName, "mbot stop needs a script name or an active last-loaded module.");
        }

        IReadOnlyList<Core.RunningScriptInfo> running = GetRunningScripts();
        int stopped = 0;
        foreach (Core.RunningScriptInfo script in running)
        {
            if (script.IsSystemScript)
                continue;

            string loadedName = script.Name ?? string.Empty;
            string leaf = Path.GetFileNameWithoutExtension(loadedName);
            if (!loadedName.StartsWith(selector, StringComparison.OrdinalIgnoreCase) &&
                !leaf.StartsWith(selector, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (StopScriptByName(loadedName))
                stopped++;
        }

        if (stopped == 0)
            return PublishUnsupported(context.CommandName, $"mbot could not find a non-system script starting with '{selector}'.");

        return PublishNativeResult(context.CommandName, $"mbot stopped {stopped} script(s) matching '{selector}'.");
    }

    private mbotDispatchResult ExecuteListAll(string canonical)
    {
        IReadOnlyList<Core.RunningScriptInfo> running = GetRunningScripts();
        if (running.Count == 0)
            return PublishNativeResult(canonical, "mbot sees no running scripts.");

        PublishMessage("mbot active scripts:");
        foreach (Core.RunningScriptInfo script in running)
        {
            string kind = script.IsSystemScript ? "system" : "user";
            string paused = script.Paused ? " paused" : string.Empty;
            PublishMessage($"  [{kind}] {script.Name}{paused}");
        }

        return new mbotDispatchResult(true, mbotDispatchKind.Native, canonical, $"mbot listed {running.Count} active script(s).");
    }

    private mbotDispatchResult ExecuteBotStatus(string canonical)
    {
        mbotSettings settings = Settings;
        int currentSector = Core.ScriptRef.GetCurrentSector();
        PublishMessage($"mbot: enabled={Enabled} attached={IsAttached} sector={currentSector}");
        PublishMessage($"mbot: botname={settings.BotName} team={settings.TeamName} subspace={settings.SubspaceChannel}");
        PublishMessage($"mbot: self={_config.AcceptSelfCommands} subspaceCmds={_config.AcceptSubspaceCommands} private={_config.AcceptPrivateCommands}");
        PublishMessage($"mbot: scriptRoot={_config.ScriptRoot}");
        return new mbotDispatchResult(true, mbotDispatchKind.Native, canonical, "mbot displayed bot status.");
    }

    private mbotDispatchResult PublishNativeResult(string canonical, string message)
    {
        PublishMessage(message);
        return new mbotDispatchResult(true, mbotDispatchKind.Native, canonical, message);
    }

    private mbotDispatchResult PublishUnsupported(string canonical, string message)
    {
        PublishMessage(message);
        return new mbotDispatchResult(false, mbotDispatchKind.Unsupported, canonical, message);
    }

    private void PublishMessage(string message)
    {
        _gameInstance?.ClientMessage("\r\n" + message + "\r\n");
    }

    private string? TryResolveScriptReference(mbotCommandSpec command)
    {
        if (command.Kind != mbotCommandKind.Module || string.IsNullOrWhiteSpace(command.Source))
            return null;

        string relative = command.Source
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        string reference = Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(_config.ScriptRoot, relative);

        string fullPath = reference;
        if (!Path.IsPathRooted(fullPath))
        {
            string? baseDir = _interpreter?.ProgramDir;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Directory.GetCurrentDirectory();

            fullPath = Path.Combine(baseDir, reference);
        }

        return File.Exists(fullPath) ? reference : null;
    }

    private bool TryHandlePageLogin(string line)
    {
        if (!_config.AcceptPrivateCommands || !TryParseFixedWidthCommand(line, 'P', out string userName, out string payload))
            return false;

        mbotSettings settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.BotPassword) || settings.SubspaceChannel <= 0)
            return false;

        string loginToken = $"{settings.BotName}:{settings.BotPassword}:{settings.SubspaceChannel}";
        if (payload.IndexOf(loginToken, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (!string.IsNullOrWhiteSpace(userName))
            _authorizedUsers.Add(userName);

        PublishMessage($"mbot: user verified - {userName}");
        return true;
    }

    private bool TryParseIncomingCommand(string line, out mbotCommandContext? context)
    {
        context = null;
        mbotSettings settings = Settings;
        string botName = settings.BotName;
        string teamName = settings.TeamName;

        if (_config.AcceptSelfCommands &&
            TryParseOwnCommand(line, botName, out context))
        {
            return true;
        }

        if (_config.AcceptSelfCommands &&
            !string.Equals(teamName, botName, StringComparison.OrdinalIgnoreCase) &&
            TryParseOwnCommand(line, teamName, out context))
        {
            return true;
        }

        if (_config.AcceptSelfCommands &&
            TryParseOwnCommand(line, "all", out context))
        {
            return true;
        }

        if (_config.AcceptSubspaceCommands &&
            TryParseRemoteCommand(line, 'R', botName, "subspace", out context))
        {
            return true;
        }

        if (_config.AcceptSubspaceCommands &&
            !string.Equals(teamName, botName, StringComparison.OrdinalIgnoreCase) &&
            TryParseRemoteCommand(line, 'R', teamName, "subspace", out context))
        {
            return true;
        }

        if (_config.AcceptSubspaceCommands &&
            TryParseRemoteCommand(line, 'R', "all", "subspace", out context))
        {
            return true;
        }

        if (_config.AcceptPrivateCommands &&
            TryParseRemoteCommand(line, 'P', botName, "private", out context))
        {
            return true;
        }

        if (_config.AcceptPrivateCommands &&
            !string.Equals(teamName, botName, StringComparison.OrdinalIgnoreCase) &&
            TryParseRemoteCommand(line, 'P', teamName, "private", out context))
        {
            return true;
        }

        if (_config.AcceptPrivateCommands &&
            TryParseRemoteCommand(line, 'P', "all", "private", out context))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseOwnCommand(string line, string targetName, out mbotCommandContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        string prefix = "'" + targetName;
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string remainder = line[prefix.Length..].TrimStart();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            context = new mbotCommandContext(string.Empty, string.Empty, Array.Empty<string>(), true, "self", "self");
            return true;
        }

        List<string> words = GetWords(remainder.ToLowerInvariant());
        if (words.Count == 0)
            return false;

        context = new mbotCommandContext(
            remainder.ToLowerInvariant(),
            words[0],
            words.Skip(1).Take(8).ToList(),
            true,
            "self",
            "self");
        return true;
    }

    private static bool TryParseRemoteCommand(string line, char routePrefix, string targetName, string route, out mbotCommandContext? context)
    {
        context = null;
        if (!TryParseFixedWidthCommand(line, routePrefix, out string userName, out string payload))
            return false;

        List<string> words = GetWords(payload.ToLowerInvariant());
        if (words.Count == 0 || !string.Equals(words[0], targetName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (words.Count == 1)
            return false;

        string commandName = words[1];
        List<string> parameters = words.Skip(2).Take(8).ToList();
        string normalized = BuildNormalizedCommandLine(commandName, parameters);
        context = new mbotCommandContext(normalized, commandName, parameters, false, route, userName);
        return true;
    }

    private static bool TryParseFixedWidthCommand(string line, char routePrefix, out string userName, out string payload)
    {
        userName = string.Empty;
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(line) || line.Length < 2 || char.ToUpperInvariant(line[0]) != routePrefix)
            return false;

        userName = line.Length > 2
            ? line.Substring(2, Math.Min(6, Math.Max(0, line.Length - 2))).Trim()
            : string.Empty;

        payload = line.Length > 9 ? line[9..].Trim() : string.Empty;
        return !string.IsNullOrWhiteSpace(payload);
    }

    private bool IsAuthorized(mbotCommandContext context)
    {
        if (context.SelfCommand)
            return true;

        if (_authorizedUsers.Count == 0)
            return true;

        return !string.IsNullOrWhiteSpace(context.UserName) && _authorizedUsers.Contains(context.UserName);
    }

    private static bool IsBlockedRemoteControlCommand(mbotCommandContext context)
    {
        if (context.SelfCommand)
            return false;

        return string.Equals(context.CommandName, "bot", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.CommandName, "relog", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetAuthorizedUsers()
    {
        _authorizedUsers.Clear();
        foreach (string user in _config.AuthorizedUsers)
        {
            string trimmed = user?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(trimmed))
                _authorizedUsers.Add(trimmed);
        }
    }

    private static IEnumerable<string> SplitCommandSegments(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            yield break;

        foreach (string segment in input.Split('|'))
        {
            string trimmed = segment.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static List<string> GetWords(string input)
    {
        return input
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static string BuildNormalizedCommandLine(string canonical, IReadOnlyList<string> parameters)
    {
        return parameters.Count == 0
            ? canonical
            : canonical + " " + string.Join(" ", parameters);
    }
}
