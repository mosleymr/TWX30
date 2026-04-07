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

internal sealed record mbotStatusSnapshot(
    bool Enabled,
    bool AutoStart,
    bool IsAttached,
    bool WatcherEnabled,
    bool WatcherAttached,
    bool AcceptSelfCommands,
    bool AcceptSubspaceCommands,
    bool AcceptPrivateCommands,
    string BotName,
    string TeamName,
    int SubspaceChannel,
    int CurrentSector,
    string Mode,
    string ScriptRoot,
    string LastLoadedModule,
    IReadOnlyList<string> AuthorizedUsers);

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
        if (_config.AutoStart)
            _config.Enabled = true;
        NormalizeConfig();
        ResetAuthorizedUsers();
        SyncWatcherState();
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
        NormalizeConfig();
        ResetAuthorizedUsers();
        SyncWatcherState();
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

    public mbotStatusSnapshot GetStatusSnapshot()
    {
        mbotSettings settings = Settings;
        string mode = Core.ScriptRef.GetCurrentGameVar("$BOT~MODE", "General");
        string lastLoadedModule = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);

        return new mbotStatusSnapshot(
            Enabled: _config.Enabled,
            AutoStart: _config.AutoStart,
            IsAttached: IsAttached,
            WatcherEnabled: _config.WatcherEnabled,
            WatcherAttached: Watcher.IsAttached,
            AcceptSelfCommands: _config.AcceptSelfCommands,
            AcceptSubspaceCommands: _config.AcceptSubspaceCommands,
            AcceptPrivateCommands: _config.AcceptPrivateCommands,
            BotName: settings.BotName,
            TeamName: settings.TeamName,
            SubspaceChannel: settings.SubspaceChannel,
            CurrentSector: Core.ScriptRef.GetCurrentSector(),
            Mode: string.IsNullOrWhiteSpace(mode) ? "General" : mode,
            ScriptRoot: _config.ScriptRoot,
            LastLoadedModule: lastLoadedModule,
            AuthorizedUsers: _authorizedUsers
                .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public bool TryExecuteLocalInput(string input, out IReadOnlyList<mbotDispatchResult> results)
    {
        results = Array.Empty<mbotDispatchResult>();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        if (TryParseLocalSelfCommand(trimmed, out mbotCommandContext? context) && context != null)
        {
            if (context.CommandLine.Length == 0)
            {
                mbotSettings settings = Settings;
                PublishMessage($"mbot: you are logged into this bot. Use {settings.BotName} help for commands.");
                return true;
            }

            results = ExecuteCommandLine(context.CommandLine, selfCommand: true, route: "local", userName: "self");
            return true;
        }

        results = ExecuteCommandLine(trimmed, selfCommand: true, route: "local", userName: "self");
        return true;
    }

    public bool ObserveServerLine(string line)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(line))
            return false;

        if (Watcher.IsAttached)
            Watcher.ObserveServerLine(line);

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

        List<string> words = GetWords(input);
        if (words.Count == 0)
        {
            string message = "mbot: Empty mbot command.";
            PublishMessage(message);
            return new mbotDispatchResult(false, mbotDispatchKind.Invalid, string.Empty, message);
        }

        string canonical = mbotCatalog.NormalizeCommandName(words[0]);
        mbotCatalog.TryGetCommandSpec(canonical, out mbotCommandSpec? command);
        List<string> parameters = words.Skip(1).Take(8).ToList();
        string normalizedLine = BuildNormalizedCommandLine(canonical, parameters);
        var context = new mbotCommandContext(
            normalizedLine,
            canonical,
            parameters,
            selfCommand,
            route,
            userName);

        string? scriptReference = TryResolveScriptReference(canonical, command, out bool isModeScript);
        Compat.ApplyToSession(_interpreter, _database, _config, Settings, context, scriptReference);

        if (scriptReference != null)
        {
            if (isModeScript && !HasHelpParameter(parameters))
                Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", FormatModeName(canonical));

            Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", scriptReference);

            if (!TryLoadScript(scriptReference, out string? error))
            {
                string message = $"mbot: failed to load '{scriptReference}': {error}";
                PublishMessage(message);
                return new mbotDispatchResult(false, mbotDispatchKind.Script, canonical, message, scriptReference);
            }

            string loadedMessage = $"mbot: loaded {canonical} ({scriptReference}).";
            PublishMessage(loadedMessage);
            return new mbotDispatchResult(true, mbotDispatchKind.Script, canonical, loadedMessage, scriptReference);
        }

        if (command?.Kind == mbotCommandKind.Internal || mbotCatalog.IsInternalCommand(canonical))
        {
            mbotCommandSpec internalCommand = command ?? new mbotCommandSpec(
                canonical,
                mbotCommandKind.Internal,
                $":INTERNAL_COMMANDS~{canonical}",
                "Internal mombot-compatible command.");
            return ExecuteNative(internalCommand, context);
        }

        string messageInvalid = $"mbot: '{canonical}' is not a valid command.";
        PublishMessage(messageInvalid);
        return new mbotDispatchResult(false, mbotDispatchKind.Invalid, canonical, messageInvalid);
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
        mbotStatusSnapshot snapshot = GetStatusSnapshot();
        PublishMessage($"mbot: enabled={snapshot.Enabled} autostart={snapshot.AutoStart} attached={snapshot.IsAttached} watcher={snapshot.WatcherEnabled}/{snapshot.WatcherAttached} sector={snapshot.CurrentSector}");
        PublishMessage($"mbot: botname={snapshot.BotName} team={snapshot.TeamName} subspace={snapshot.SubspaceChannel} mode={snapshot.Mode}");
        PublishMessage($"mbot: self={snapshot.AcceptSelfCommands} subspaceCmds={snapshot.AcceptSubspaceCommands} private={snapshot.AcceptPrivateCommands} authUsers={snapshot.AuthorizedUsers.Count}");
        PublishMessage($"mbot: scriptRoot={snapshot.ScriptRoot}");
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

    private void NormalizeConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.ScriptRoot))
            _config.ScriptRoot = "scripts/mbot";

        _config.WatcherEnabled = _config.Enabled;
    }

    private void SyncWatcherState()
    {
        _config.WatcherEnabled = _config.Enabled;
        if (_config.Enabled && _gameInstance != null)
            Watcher.Attach(_gameInstance, _database);
        else
            Watcher.Detach();
    }

    private string? TryResolveScriptReference(string canonical, mbotCommandSpec? command, out bool isModeScript)
    {
        isModeScript = false;

        if (command?.Kind == mbotCommandKind.Module &&
            TryResolveExplicitScriptReference(command.Source, out string? explicitReference, out string? explicitFullPath))
        {
            isModeScript = IsModeScriptPath(explicitFullPath);
            return explicitReference;
        }

        if (TryResolveRecursiveScriptReference(canonical, out string? recursiveReference, out string? recursiveFullPath))
        {
            isModeScript = IsModeScriptPath(recursiveFullPath);
            return recursiveReference;
        }

        return null;
    }

    private bool TryResolveExplicitScriptReference(string source, out string? scriptReference, out string? fullPath)
    {
        scriptReference = null;
        fullPath = null;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        string relative = source
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        string configuredPath = Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(_config.ScriptRoot, relative);

        string resolvedFullPath = ResolveAgainstProgramDirectory(configuredPath);
        if (!File.Exists(resolvedFullPath))
            return false;

        fullPath = resolvedFullPath;
        scriptReference = BuildLoadReference(resolvedFullPath);
        return true;
    }

    private bool TryResolveRecursiveScriptReference(string canonical, out string? scriptReference, out string? fullPath)
    {
        scriptReference = null;
        fullPath = null;

        string? scriptRoot = GetAbsoluteScriptRoot();
        if (string.IsNullOrWhiteSpace(scriptRoot) || !Directory.Exists(scriptRoot))
            return false;

        string visibleName = canonical + ".cts";
        string hiddenName = "_" + canonical + ".cts";
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };

        string? match = Directory
            .EnumerateFiles(scriptRoot, "*.cts", options)
            .FirstOrDefault(path =>
            {
                string fileName = Path.GetFileName(path);
                return string.Equals(fileName, visibleName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, hiddenName, StringComparison.OrdinalIgnoreCase);
            });

        if (match == null)
            return false;

        fullPath = Path.GetFullPath(match);
        scriptReference = BuildLoadReference(fullPath);
        return true;
    }

    private string? GetAbsoluteScriptRoot()
    {
        if (string.IsNullOrWhiteSpace(_config.ScriptRoot))
            return null;

        string root = Path.IsPathRooted(_config.ScriptRoot)
            ? _config.ScriptRoot
            : Path.Combine(GetProgramDirectory(), _config.ScriptRoot);

        return Path.GetFullPath(root);
    }

    private string ResolveAgainstProgramDirectory(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(GetProgramDirectory(), path));
    }

    private string BuildLoadReference(string fullPath)
    {
        string programDirectory = Path.GetFullPath(GetProgramDirectory());
        string candidate = Path.GetFullPath(fullPath);
        string prefix = programDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(programDirectory, candidate).Replace('\\', '/');

        return candidate;
    }

    private string GetProgramDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_interpreter?.ProgramDir))
            return _interpreter.ProgramDir;

        return Directory.GetCurrentDirectory();
    }

    private bool IsModeScriptPath(string? fullPath)
    {
        string? scriptRoot = GetAbsoluteScriptRoot();
        if (string.IsNullOrWhiteSpace(fullPath) ||
            string.IsNullOrWhiteSpace(scriptRoot) ||
            !Path.IsPathRooted(fullPath))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(scriptRoot, fullPath);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Length > 0 && string.Equals(segments[0], "modes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHelpParameter(IReadOnlyList<string> parameters)
    {
        return parameters.Any(parameter =>
            string.Equals(parameter, "help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parameter, "?", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatModeName(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return "General";

        if (commandName.Length == 1)
            return commandName.ToUpperInvariant();

        return char.ToUpperInvariant(commandName[0]) + commandName[1..];
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

    private bool TryParseLocalSelfCommand(string line, out mbotCommandContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        mbotSettings settings = Settings;
        if (TryParseOwnCommand(line, settings.BotName, out context))
            return true;

        if (!string.Equals(settings.TeamName, settings.BotName, StringComparison.OrdinalIgnoreCase) &&
            TryParseOwnCommand(line, settings.TeamName, out context))
        {
            return true;
        }

        return TryParseOwnCommand(line, "all", out context);
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

        List<string> words = GetWords(remainder);
        if (words.Count == 0)
            return false;

        string commandName = words[0];
        List<string> parameters = words.Skip(1).Take(8).ToList();
        context = new mbotCommandContext(
            BuildNormalizedCommandLine(commandName, parameters),
            commandName,
            parameters,
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

        List<string> words = GetWords(payload);
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
