using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core = TWXProxy.Core;

namespace MTC.mombot;

internal enum mombotDispatchKind
{
    Invalid,
    Native,
    Script,
    Unsupported,
}

internal sealed record mombotDispatchResult(
    bool Success,
    mombotDispatchKind Kind,
    string CanonicalCommand,
    string Message,
    string? ScriptReference = null);

internal sealed record mombotStatusSnapshot(
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

internal sealed class mombotService
{
    private Core.GameInstance? _gameInstance;
    private Core.ModDatabase? _database;
    private Core.ModInterpreter? _interpreter;
    private mombotConfig _config = new();
    private readonly HashSet<string> _authorizedUsers = new(StringComparer.OrdinalIgnoreCase);

    public mombotConfig Config => _config;
    public mombotWatcher Watcher { get; } = new();
    public mombotCompatContext Compat { get; } = new();
    public bool IsAttached => _gameInstance != null;
    public bool Enabled => _config.Enabled;
    public IReadOnlyList<mombotInternalCommandGroup> InternalCommandGroups => mombotCatalog.InternalCommandGroups;
    public IReadOnlyList<mombotHotkeyBinding> DefaultHotkeys => mombotCatalog.DefaultHotkeys;
    public IReadOnlyList<mombotMenuSurface> MenuSurfaces => mombotCatalog.MenuSurfaces;
    public IReadOnlyList<mombotCommandSpec> InitialCommands => mombotCatalog.InitialCommands;
    public IReadOnlyList<mombotAliasSpec> InitialAliases => mombotCatalog.InitialAliases;
    public mombotSettings Settings => mombotSettings.Load();

    public void AttachSession(
        Core.GameInstance? gameInstance,
        Core.ModDatabase? database,
        Core.ModInterpreter? interpreter,
        mombotConfig? config)
    {
        _gameInstance = gameInstance;
        _database = database;
        _interpreter = interpreter;
        _config = config ?? new mombotConfig();
        _config.Enabled = false;
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

    public void ApplyConfig(mombotConfig? config)
    {
        bool wasEnabled = _config.Enabled;
        _config = config ?? new mombotConfig();
        NormalizeConfig();
        ResetAuthorizedUsers();
        SyncWatcherState();
        if (!wasEnabled && _config.Enabled)
            ArmRelogFlagsIfEnabled();
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

    public IReadOnlyList<string> GetStartupScriptReferences()
    {
        string? scriptRoot = GetAbsoluteScriptRoot();
        if (string.IsNullOrWhiteSpace(scriptRoot))
            return Array.Empty<string>();

        string startupRoot = Path.Combine(scriptRoot, "startups");
        if (!Directory.Exists(startupRoot))
            return Array.Empty<string>();

        return Directory
            .EnumerateFiles(startupRoot, "*.cts", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildLoadReference(Path.GetFullPath(path)))
            .ToArray();
    }

    public void StopAllNonSystemScripts()
    {
        Core.ProxyGameOperations.StopAllScripts(_interpreter, includeSystemScripts: false);
    }

    public bool TryResolveInitialCommand(string input, out mombotCommandSpec? command, out string canonical)
    {
        return mombotCatalog.TryResolveInitialCommand(input, out command, out canonical);
    }

    public mombotStatusSnapshot GetStatusSnapshot()
    {
        mombotSettings settings = Settings;
        string mode = Core.ScriptRef.GetCurrentGameVar("$BOT~MODE", "General");
        string lastLoadedModule = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);

        return new mombotStatusSnapshot(
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

    public bool TryExecuteLocalInput(string input, out IReadOnlyList<mombotDispatchResult> results)
    {
        results = Array.Empty<mombotDispatchResult>();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        if (TryParseLocalSelfCommand(trimmed, out mombotCommandContext? context) && context != null)
        {
            if (context.CommandLine.Length == 0)
            {
                mombotSettings settings = Settings;
                PublishMessage($"mombot: you are logged into this bot. Use {settings.BotName} help for commands.");
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

        if (!TryParseIncomingCommand(line, out mombotCommandContext? context))
            return false;

        if (context == null)
            return false;

        if (context.CommandLine.Length == 0)
        {
            mombotSettings settings = Settings;
            PublishMessage($"mombot: you are logged into this bot. Use {settings.BotName} help for commands.");
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

    public IReadOnlyList<mombotDispatchResult> ExecuteCommandLine(
        string input,
        bool selfCommand = true,
        string route = "self",
        string userName = "self")
    {
        var results = new List<mombotDispatchResult>();
        foreach (string segment in SplitCommandSegments(input))
            results.Add(ExecuteSingleCommand(segment, selfCommand, route, userName));

        return results;
    }

    private mombotDispatchResult ExecuteSingleCommand(
        string input,
        bool selfCommand,
        string route,
        string userName)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new mombotDispatchResult(false, mombotDispatchKind.Invalid, string.Empty, "Empty mombot command.");

        List<string> words = GetWords(input);
        if (words.Count == 0)
        {
            string message = "mombot: Empty mombot command.";
            PublishMessage(message);
            return new mombotDispatchResult(false, mombotDispatchKind.Invalid, string.Empty, message);
        }

        string originalCommand = NormalizeRawCommandToken(words[0]);
        List<string> originalParameters = words.Skip(1).Take(8).ToList();
        mombotCommandContext context = NormalizeDispatchContext(originalCommand, originalParameters, selfCommand, route, userName);
        string canonical = context.CommandName;
        List<string> parameters = context.Parameters.ToList();
        mombotCatalog.TryGetCommandSpec(canonical, out mombotCommandSpec? command);

        string originalLine = BuildNormalizedCommandLine(originalCommand, originalParameters);
        if (!string.Equals(originalLine, context.CommandLine, StringComparison.Ordinal))
        {
            Core.GlobalModules.DebugLog(
                $"[mombot] Normalized command '{originalLine}' -> '{context.CommandLine}' for route={route} user={userName}\n");
        }

        string? scriptReference = TryResolveScriptReference(canonical, command, out bool isModeScript);
        Compat.ApplyToSession(_interpreter, _database, _config, Settings, context, scriptReference);

        if (scriptReference != null)
        {
            if (isModeScript && !HasHelpParameter(parameters))
                Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", FormatModeName(canonical));

            Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", scriptReference);

            if (!TryLoadScript(scriptReference, out string? error))
            {
                string message = $"mombot: failed to load '{scriptReference}': {error}";
                PublishMessage(message);
                return new mombotDispatchResult(false, mombotDispatchKind.Script, canonical, message, scriptReference);
            }

            string loadedMessage = $"mombot: loaded {canonical} ({scriptReference}).";
            PublishMessage(loadedMessage);
            return new mombotDispatchResult(true, mombotDispatchKind.Script, canonical, loadedMessage, scriptReference);
        }

        if (command?.Kind == mombotCommandKind.Internal || mombotCatalog.IsInternalCommand(canonical))
        {
            mombotCommandSpec internalCommand = command ?? new mombotCommandSpec(
                canonical,
                mombotCommandKind.Internal,
                $":INTERNAL_COMMANDS~{canonical}",
                "Internal mombot-compatible command.");
            return ExecuteNative(internalCommand, context);
        }

        string messageInvalid = $"mombot: '{canonical}' is not a valid command.";
        PublishMessage(messageInvalid);
        return new mombotDispatchResult(false, mombotDispatchKind.Invalid, canonical, messageInvalid);
    }

    private mombotDispatchResult ExecuteNative(mombotCommandSpec command, mombotCommandContext context)
    {
        string canonical = context.CommandName;

        switch (canonical.ToLowerInvariant())
        {
            case "stopall":
                StopAllNonSystemScripts();
                Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
                Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
                return PublishNativeResult(canonical, "mombot stopped all non-system scripts.");

            case "stop":
                return ExecuteStop(context);

            case "stopmodules":
                return ExecuteStopModules(canonical);

            case "listall":
                return ExecuteListAll(canonical);

            case "refresh":
                Compat.ApplyToSession(_interpreter, _database, _config, Settings, context);
                return PublishNativeResult(canonical, "mombot refreshed its command context from current savevar/loadvar state.");

            case "bot":
                return ExecuteBotStatus(canonical);

            case "logoff":
                return PublishUnsupported(canonical, "mombot leaves logoff/logout script-backed for now because it is a server-interactive flow.");

            default:
                return PublishUnsupported(canonical, $"mombot does not have a native handler for '{canonical}' yet.");
        }
    }

    private mombotDispatchResult ExecuteStop(mombotCommandContext context)
    {
        string selector = context.Parameters.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            string lastLoaded = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
            if (!string.IsNullOrWhiteSpace(lastLoaded) && StopScriptByName(lastLoaded))
            {
                Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
                return PublishNativeResult(context.CommandName, $"mombot stopped {lastLoaded}.");
            }

            return PublishUnsupported(context.CommandName, "mombot stop needs a script name or an active last-loaded module.");
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
            return PublishUnsupported(context.CommandName, $"mombot could not find a non-system script starting with '{selector}'.");

        return PublishNativeResult(context.CommandName, $"mombot stopped {stopped} script(s) matching '{selector}'.");
    }

    private mombotDispatchResult ExecuteStopModules(string canonical)
    {
        string lastLoaded = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        bool stopped = !string.IsNullOrWhiteSpace(lastLoaded) && StopScriptByName(lastLoaded);

        Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
        Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);

        if (stopped)
            return PublishNativeResult(canonical, $"mombot reset to General mode and stopped {lastLoaded}.");

        return PublishNativeResult(canonical, "mombot reset to General mode.");
    }

    private mombotDispatchResult ExecuteListAll(string canonical)
    {
        IReadOnlyList<Core.RunningScriptInfo> running = GetRunningScripts();
        if (running.Count == 0)
            return PublishNativeResult(canonical, "mombot sees no running scripts.");

        PublishMessage("mombot active scripts:");
        foreach (Core.RunningScriptInfo script in running)
        {
            string kind = script.IsSystemScript ? "system" : "user";
            string paused = script.Paused ? " paused" : string.Empty;
            PublishMessage($"  [{kind}] {script.Name}{paused}");
        }

        return new mombotDispatchResult(true, mombotDispatchKind.Native, canonical, $"mombot listed {running.Count} active script(s).");
    }

    private mombotDispatchResult ExecuteBotStatus(string canonical)
    {
        mombotStatusSnapshot snapshot = GetStatusSnapshot();
        PublishMessage($"mombot: enabled={snapshot.Enabled} attached={snapshot.IsAttached} watcher={snapshot.WatcherEnabled}/{snapshot.WatcherAttached} sector={snapshot.CurrentSector}");
        PublishMessage($"mombot: botname={snapshot.BotName} team={snapshot.TeamName} subspace={snapshot.SubspaceChannel} mode={snapshot.Mode}");
        PublishMessage($"mombot: self={snapshot.AcceptSelfCommands} subspaceCmds={snapshot.AcceptSubspaceCommands} private={snapshot.AcceptPrivateCommands} authUsers={snapshot.AuthorizedUsers.Count}");
        PublishMessage($"mombot: scriptRoot={snapshot.ScriptRoot}");
        return new mombotDispatchResult(true, mombotDispatchKind.Native, canonical, "mombot displayed bot status.");
    }

    private mombotDispatchResult PublishNativeResult(string canonical, string message)
    {
        PublishMessage(message);
        return new mombotDispatchResult(true, mombotDispatchKind.Native, canonical, message);
    }

    private mombotDispatchResult PublishUnsupported(string canonical, string message)
    {
        PublishMessage(message);
        return new mombotDispatchResult(false, mombotDispatchKind.Unsupported, canonical, message);
    }

    private void PublishMessage(string message)
    {
        _gameInstance?.ClientMessage("\r\n" + message + "\r\n");
    }

    private void NormalizeConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.ScriptRoot))
            _config.ScriptRoot = "scripts/mombot";

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

    private void ArmRelogFlagsIfEnabled()
    {
        if (!_config.Enabled)
            return;

        Core.ScriptRef.SetCurrentGameVar("$doRelog", "1");
        Core.ScriptRef.SetCurrentGameVar("$BOT~DORELOG", "1");
        Core.GlobalModules.DebugLog("[mombot] Armed relog flags in current-game var cache ($doRelog=1, $BOT~DORELOG=1)\n");
    }

    private string? TryResolveScriptReference(string canonical, mombotCommandSpec? command, out bool isModeScript)
    {
        isModeScript = false;

        if (command?.Kind == mombotCommandKind.Module &&
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

        mombotSettings settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.BotPassword) || settings.SubspaceChannel <= 0)
            return false;

        string loginToken = $"{settings.BotName}:{settings.BotPassword}:{settings.SubspaceChannel}";
        if (payload.IndexOf(loginToken, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (!string.IsNullOrWhiteSpace(userName))
        {
            _authorizedUsers.Add(userName);
            PersistAuthorizedUsers();
        }

        PublishMessage($"mombot: user verified - {userName}");
        return true;
    }

    private bool TryParseIncomingCommand(string line, out mombotCommandContext? context)
    {
        context = null;
        mombotSettings settings = Settings;
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

    private bool TryParseLocalSelfCommand(string line, out mombotCommandContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        mombotSettings settings = Settings;
        if (TryParseOwnCommand(line, settings.BotName, out context))
            return true;

        if (!string.Equals(settings.TeamName, settings.BotName, StringComparison.OrdinalIgnoreCase) &&
            TryParseOwnCommand(line, settings.TeamName, out context))
        {
            return true;
        }

        return TryParseOwnCommand(line, "all", out context);
    }

    private static bool TryParseOwnCommand(string line, string targetName, out mombotCommandContext? context)
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
            context = new mombotCommandContext(string.Empty, string.Empty, Array.Empty<string>(), true, "self", "self");
            return true;
        }

        List<string> words = GetWords(remainder);
        if (words.Count == 0)
            return false;

        string commandName = words[0];
        List<string> parameters = words.Skip(1).Take(8).ToList();
        context = new mombotCommandContext(
            BuildNormalizedCommandLine(commandName, parameters),
            commandName,
            parameters,
            true,
            "self",
            "self");
        return true;
    }

    private static bool TryParseRemoteCommand(string line, char routePrefix, string targetName, string route, out mombotCommandContext? context)
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
        context = new mombotCommandContext(normalized, commandName, parameters, false, route, userName);
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

    private bool IsAuthorized(mombotCommandContext context)
    {
        if (context.SelfCommand)
            return true;

        if (_authorizedUsers.Count == 0)
            return true;

        return !string.IsNullOrWhiteSpace(context.UserName) && _authorizedUsers.Contains(context.UserName);
    }

    private static bool IsBlockedRemoteControlCommand(mombotCommandContext context)
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

        foreach (string user in LoadAuthorizedUsersFromStorage())
            _authorizedUsers.Add(user);
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
            .Select(ExpandNumericSuffix)
            .ToList();
    }

    private mombotCommandContext NormalizeDispatchContext(
        string commandName,
        IReadOnlyList<string> rawParameters,
        bool selfCommand,
        string route,
        string userName)
    {
        string normalizedCommand = commandName;
        var parameters = rawParameters.Take(8).ToList();

        ApplyStockCommandRewrites(ref normalizedCommand, parameters);
        ExpandStockParameterAliases(parameters);
        normalizedCommand = NormalizeStockCommandAlias(normalizedCommand);
        ApplyTravelCommandRewrites(ref normalizedCommand, parameters);
        normalizedCommand = mombotCatalog.NormalizeCommandName(normalizedCommand);

        string normalizedLine = BuildNormalizedCommandLine(normalizedCommand, parameters);
        return new mombotCommandContext(
            normalizedLine,
            normalizedCommand,
            parameters,
            selfCommand,
            route,
            userName);
    }

    private static string BuildNormalizedCommandLine(string canonical, IReadOnlyList<string> parameters)
    {
        return parameters.Count == 0
            ? canonical
            : canonical + " " + string.Join(" ", parameters);
    }

    private void ApplyStockCommandRewrites(ref string commandName, List<string> parameters)
    {
        if (MatchesCommand(commandName, "create"))
        {
            string firstParameter = GetParameter(parameters, 0);
            commandName = IsPortOrPlanetTarget(firstParameter) ? firstParameter : "port";
            InsertParameterAtFront(parameters, "create");
            return;
        }

        if (MatchesAnyCommand(commandName, "kill", "destroy", "blow"))
        {
            string firstParameter = GetParameter(parameters, 0);
            if (IsPortOrPlanetTarget(firstParameter))
            {
                commandName = firstParameter;
                if (parameters.Count == 0)
                    parameters.Add("kill");
                else
                    parameters[0] = "kill";
            }
            else
            {
                commandName = "kill";
            }

            return;
        }

        if (MatchesAnyCommand(commandName, "upgrade", "max"))
        {
            string firstParameter = GetParameter(parameters, 0);
            commandName = IsPortOrPlanetTarget(firstParameter) ? firstParameter : "port";
            InsertParameterAtFront(parameters, "upgrade");
            return;
        }

        if (MatchesAnyCommand(commandName, "f", "fde", "ufde", "nf", "uf", "de", "fp", "fup", "nfup"))
        {
            InsertParameterAtFront(parameters, commandName);
            commandName = "find";
        }
    }

    private void ExpandStockParameterAliases(List<string> parameters)
    {
        for (int index = 0; index < parameters.Count; index++)
        {
            string parameter = parameters[index];
            if (string.IsNullOrWhiteSpace(parameter))
                continue;

            if (string.Equals(parameter, "s", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny(FormatSector(_database?.DBHeader.StarDock), "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK", "$stardock");
            else if (string.Equals(parameter, "r", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny(FormatSector(_database?.DBHeader.Rylos), "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS", "$rylos");
            else if (string.Equals(parameter, "a", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny(FormatSector(_database?.DBHeader.AlphaCentauri), "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI", "$alpha_centauri");
            else if (string.Equals(parameter, "h", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny("0", "$MAP~HOME_SECTOR", "$MAP~home_sector", "$BOT~HOME_SECTOR", "$home_sector");
            else if (string.Equals(parameter, "b", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny("0", "$MAP~BACKDOOR", "$MAP~backdoor", "$backdoor");
            else if (string.Equals(parameter, "x", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ReadCurrentAny("0", "$BOT~SAFE_SHIP", "$bot~safe_ship", "$safe_ship");
            else if (string.Equals(parameter, "l", StringComparison.OrdinalIgnoreCase))
                parameters[index] = ResolveSafePlanetSectorAlias();
        }
    }

    private void ApplyTravelCommandRewrites(ref string commandName, List<string> parameters)
    {
        if (string.Equals(commandName, "m", StringComparison.OrdinalIgnoreCase))
            commandName = "mow";
        else if (string.Equals(commandName, "p", StringComparison.OrdinalIgnoreCase))
            commandName = "pwarp";
        else if (string.Equals(commandName, "t", StringComparison.OrdinalIgnoreCase))
            commandName = "twarp";
        else if (string.Equals(commandName, "b", StringComparison.OrdinalIgnoreCase))
            commandName = "bwarp";

        if (!MatchesAnyCommand(commandName, "mow", "twarp", "bwarp", "pwarp", "smow"))
            return;

        RewriteTravelPlanetTarget(parameters);
    }

    private static string NormalizeStockCommandAlias(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return string.Empty;

        return commandName.ToLowerInvariant() switch
        {
            "l" => "land",
            "x" => "xport",
            "qss" => "status",
            "d" => "dep",
            "w" => "with",
            "k" => "keep",
            "exit" => "xenter",
            "cn" => "cn9",
            "emx" => "reset",
            "pinfo" => "pscan",
            "shipstore" => "storeship",
            _ => commandName,
        };
    }

    private void RewriteTravelPlanetTarget(List<string> parameters)
    {
        if (parameters.Count < 2)
            return;

        if (!string.Equals(parameters[0], "planet", StringComparison.OrdinalIgnoreCase))
            return;

        string resolvedSector = ResolvePlanetSector(parameters[1]);
        if (!string.IsNullOrWhiteSpace(resolvedSector))
            parameters[0] = resolvedSector;
    }

    private string ResolveSafePlanetSectorAlias()
    {
        string safePlanet = ReadCurrentAny("0", "$BOT~SAFE_PLANET", "$bot~safe_planet", "$safe_planet");
        if (string.IsNullOrWhiteSpace(safePlanet) || safePlanet == "0")
            return "l";

        string resolvedSector = ResolvePlanetSector(safePlanet);
        return string.IsNullOrWhiteSpace(resolvedSector) ? string.Empty : resolvedSector;
    }

    private string ResolvePlanetSector(string planetIdToken)
    {
        if (_database == null || string.IsNullOrWhiteSpace(planetIdToken))
            return string.Empty;

        if (!int.TryParse(planetIdToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId) || planetId <= 0)
            return string.Empty;

        string sectorFromParameter = _database.GetSectorVar(planetId, "PSECTOR");
        if (!string.IsNullOrWhiteSpace(sectorFromParameter))
            return sectorFromParameter;

        int lastSector = _database.GetPlanet(planetId)?.LastSector ?? 0;
        return lastSector > 0
            ? lastSector.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static bool IsPortOrPlanetTarget(string value)
    {
        return string.Equals(value, "port", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "planet", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetParameter(IReadOnlyList<string> parameters, int index)
    {
        return index >= 0 && index < parameters.Count ? parameters[index] : string.Empty;
    }

    private static void InsertParameterAtFront(List<string> parameters, string value)
    {
        parameters.Insert(0, value);
        if (parameters.Count > 8)
            parameters.RemoveAt(8);
    }

    private static bool MatchesCommand(string commandName, string expected)
    {
        return string.Equals(commandName, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAnyCommand(string commandName, params string[] expected)
    {
        return expected.Any(candidate => string.Equals(commandName, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRawCommandToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        string normalized = token.Trim();
        int splitIndex = normalized.IndexOf(' ');
        if (splitIndex >= 0)
            normalized = normalized[..splitIndex];

        if (normalized.EndsWith(".cts", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        else if (normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];

        return normalized;
    }

    private string ReadCurrentAny(string fallback, params string[] names)
    {
        foreach (string name in names)
        {
            string value = Core.ScriptRef.GetCurrentGameVar(name, string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fallback;
    }

    private static string FormatSector(ushort? sector)
    {
        if (!sector.HasValue)
            return "0";

        ushort value = sector.Value;
        return value == 0 || value == ushort.MaxValue ? "0" : value.ToString(CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<string> LoadAuthorizedUsersFromStorage()
    {
        string? botUsersFile = GetBotUsersFilePath();
        if (string.IsNullOrWhiteSpace(botUsersFile) || !File.Exists(botUsersFile))
            return Array.Empty<string>();

        return File.ReadLines(botUsersFile)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void PersistAuthorizedUsers()
    {
        string? botUsersFile = GetBotUsersFilePath();
        if (string.IsNullOrWhiteSpace(botUsersFile))
            return;

        string? directory = Path.GetDirectoryName(botUsersFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(
            botUsersFile,
            _authorizedUsers
                .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private string? GetBotUsersFilePath()
    {
        string? storageDirectory = GetGameStorageDirectory();
        if (string.IsNullOrWhiteSpace(storageDirectory))
            return null;

        return Path.Combine(storageDirectory, "bot_users.lst");
    }

    private string? GetGameStorageDirectory()
    {
        string? scriptRoot = GetAbsoluteScriptRoot();
        if (string.IsNullOrWhiteSpace(scriptRoot))
            return null;

        string gameName = Path.GetFileNameWithoutExtension(_database?.DatabasePath ?? _database?.DatabaseName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        return Path.Combine(scriptRoot, "games", gameName);
    }

    private static string ExpandNumericSuffix(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            return token;

        char suffix = char.ToLowerInvariant(token[^1]);
        long multiplier = suffix switch
        {
            'k' => 1_000L,
            'm' => 1_000_000L,
            'b' => 1_000_000_000L,
            _ => 0L,
        };
        if (multiplier == 0L)
            return token;

        string numericPortion = token[..^1];
        if (!long.TryParse(numericPortion, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            return token;

        return (parsed * multiplier).ToString(CultureInfo.InvariantCulture);
    }
}
