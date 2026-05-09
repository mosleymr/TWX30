using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    // ── Constructor ────────────────────────────────────────────────────────
    public MainWindow()
    {
        Title          = BaseWindowTitle;
        Icon           = new WindowIcon(AssetLoader.Open(new Uri("avares://MTC/mtc2.png")));
        Width          = 1100;
        Height         = 650;
        MinWidth       = 800;
        MinHeight      = 500;
        Background     = BgWindow;
        FontFamily     = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        _state    = new GameState();
        _buffer   = new TerminalBuffer(80, 24);
        _parser   = new AnsiParser(_buffer);
        RecreateClassicShellControls();
        RecreateDeckShellControls();
        _telnet   = new TelnetClient(_buffer, _parser);

        _telnet.Connected    += OnTelnetConnected;
        _telnet.Disconnected += OnTelnetDisconnected;
        _telnet.Error        += OnTelnetError;

        // Ship status: feed every server line through the parser
        _telnet.TextLineReceived += _shipParser.FeedLine;
        _shipParser.Updated      += OnShipStatusUpdated;

        UpdateWindowTitle();

        // Database recording: feed server lines through the AutoRecorder
        _telnet.TextLineAnsiReceived += (ansiLine, strippedLine) =>
        {
            Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine, ansiLine);
            HandlePotentialCommLine(ansiLine);
            ProcessStandaloneNativeHaggleLine(strippedLine);
        };

        // Session logging for direct telnet mode is handled through the shared Core logger.
        RefreshSessionLogTarget();
        _telnet.AppDataDecoded += text =>
        {
            _sessionLog.RecordServerText(text);
        };

        // Update current sector from the command prompt — fires on every "Command [TL=...]:[N]"
        Core.GlobalModules.GlobalAutoRecorder.CurrentSectorChanged += sn =>
            Dispatcher.UIThread.Post(() =>
            {
                Core.ScriptRef.SetCurrentSector(sn);
                SetMombotCurrentVars(sn.ToString(), "$PLAYER~CURRENT_SECTOR", "$player~current_sector");
                var sectorDelta = new Core.ShipStatusDelta
                {
                    CurrentSector = sn
                };
                if (_gameInstance != null)
                    _gameInstance.ApplyShipStatusDelta(sectorDelta);
                else
                    _shipParser.ApplyDelta(sectorDelta);
                if (_state.Sector != sn)
                {
                    _state.Sector = sn;
                    _state.NotifyChanged();
                }
            });

        Core.GlobalModules.GlobalAutoRecorder.LandmarkSectorsChanged += () =>
            Dispatcher.UIThread.Post(() =>
            {
                SyncMombotSpecialSectorVarsFromDatabase(persist: true);
                RefreshStatusBar();
                _buffer.Dirty = true;
            });

        Core.GlobalModules.GlobalAutoRecorder.GenesisTorpsChanged += delta =>
            Dispatcher.UIThread.Post(() => OnGenesisTorpsChanged(delta));

        Core.GlobalModules.GlobalAutoRecorder.AtomicDetChanged += delta =>
            Dispatcher.UIThread.Post(() => OnAtomicDetChanged(delta));

        Core.GlobalModules.GlobalAutoRecorder.ShipStatusDeltaDetected += delta =>
        {
            if (_gameInstance != null)
            {
                _gameInstance.ApplyShipStatusDelta(delta);
                return;
            }

            _shipParser.ApplyDelta(delta);
        };

        _state.Changed += () => Dispatcher.UIThread.Post(RefreshInfoPanels);

        // Wire keyboard → telnet
        SetTerminalInputHandler(bytes => RouteTerminalInput(bytes, SendToTelnet));

        // Load persisted preferences (recent file list etc.) before the first shell build
        // so we don't compose the visual tree twice on startup.
        _appPrefs = AppPreferences.Load();
        _standaloneNativeHaggle.SetEnabled(true);
        _standaloneNativeHaggle.SetPortHaggleMode(ResolveGlobalPortHaggleMode());
        _standaloneNativeHaggle.SetPlanetHaggleMode(ResolveGlobalPlanetHaggleMode());
        _standaloneNativeHaggle.EnabledChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            UpdateHaggleToggleState();
            RequestStatusBarRefresh();
        });
        _standaloneNativeHaggle.StatsChanged += () => Dispatcher.UIThread.Post(RequestStatusBarRefresh);
        bool resetCommandDeckLayout =
            _appPrefs.CommandDeckLayoutVersion < AppPreferences.CurrentCommandDeckLayoutVersion ||
            _appPrefs.CommandDeckPanels.Values.Any(layout => layout.Width <= 0 || layout.BodyHeight <= 0);
        if (resetCommandDeckLayout)
        {
            _appPrefs.CommandDeckPanels.Clear();
            _appPrefs.CommandDeckLayoutVersion = AppPreferences.CurrentCommandDeckLayoutVersion;
            _appPrefs.Save();
        }
        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        _useCommandDeckSkin = _appPrefs.CommandDeckSkinEnabled;

        _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _statusRefreshTimer.Tick += (_, _) =>
        {
            _statusRefreshTimer.Stop();
            RefreshStatusBar();
        };

        _redAlertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _redAlertTimer.Tick += (_, _) =>
        {
            _redAlertTimer.Stop();
            ClearRedAlert();
        };

        Content = BuildLayout();
        PositionChanged += (_, _) => NotifyTerminalWindowMove();

        ApplyDebugLoggingPreferences();
        ApplyRedAlertPreference();
        RebuildRecentMenu();
        RebuildProxyMenu();
        RebuildScriptsMenu();
        RebuildAiMenu();
        _parser.Feed("\x1b[2J\x1b[H");
        _parser.Feed("\x1b[1;33mMayhem Tradewars Client v1.0\x1b[0m\r\n");
        _parser.Feed("\x1b[37mUse \x1b[1;32mFile \u25b6 New Connection\x1b[0;37m or \x1b[1;32mOpen\x1b[0;37m to select a game, then \x1b[1;32mFile \u25b6 Connect\x1b[0;37m to connect.\x1b[0m\r\n");
        _buffer.Dirty = true;

        _mombotKeepaliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _mombotKeepaliveTimer.Tick += (_, _) =>
        {
            if (_mombotKeepaliveTickRunning)
                return;

            _mombotKeepaliveTickRunning = true;
            _ = RunNativeMombotKeepaliveTickAsync();
        };
        _mombotKeepaliveTimer.Start();

        Opened += (_, _) =>
        {
            _nativeAppMenuReady = true;
            _nativeAppMenuAttached = false;
            _nativeDockMenuAttached = false;
            RefreshNativeAppMenu();
            RefreshNativeDockMenu();
            _ = EnsureSharedPathsConfiguredAsync();
        };
        Activated += (_, _) => FocusActiveTerminal();
        Closed    += (_, _) =>
        {
            _appPrefs.Save();
            _nativeAppMenuReady = false;
            _nativeAppMenuAttached = false;
            _nativeDockMenuAttached = false;
            _mombotKeepaliveTimer.Stop();
            _telnet.Disconnect();
            _proxyCts?.Cancel();
            if (_moduleHost != null)
                _ = _moduleHost.DisposeAsync().AsTask();
            foreach (AiAssistantWindow window in _assistantWindows.Values.ToList())
                window.Close();
            _assistantWindows.Clear();
            if (_gameInstance != null) _ = _gameInstance.StopAsync();
            _gameFileLock?.Dispose();
            _gameFileLock = null;
            _sessionLog.Dispose();
            _redAlertTimer.Stop();
            _statusRefreshTimer.Stop();
        };
    }

}
