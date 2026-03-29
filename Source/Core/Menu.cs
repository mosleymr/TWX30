/*
Copyright (C) 2026  Matt Mosley

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

using System;
using System.Text;
using System.Threading.Tasks;

namespace TWXProxy.Core
{
    /// <summary>
    /// Menu state for the client menu system
    /// </summary>
    public enum MenuState
    {
        None,
        Main,
        Data,
        Port,
        Script,
        Setup
    }

    /// <summary>
    /// Input collection mode for multi-character input
    /// </summary>
    public enum InputMode
    {
        None,
        ScriptLoad,
        ScriptUnload,
        ScriptPause,
        ScriptResume,
        ScriptKill,
        BurstInput,
        BurstEdit,
        CommandHelp,
        DataResetConfirm
    }

    /// <summary>
    /// Handles the client menu system (matches Pascal TWX Proxy behavior)
    /// </summary>
    public class MenuHandler
    {
        private readonly GameInstance _gameInstance;
        private readonly ModInterpreter? _interpreter;
        private readonly string _scriptDirectory;
        private MenuState _currentMenu = MenuState.None;
        private InputMode _inputMode = InputMode.None;
        private readonly StringBuilder _inputBuffer = new StringBuilder();
        private string _lastBurst = string.Empty;
        private string _lastScript = string.Empty;
        private bool _streamingMode = false;
        private bool _deafMode = false;
        private bool _skipNextLineFeed = false;

        public MenuHandler(GameInstance gameInstance, ModInterpreter? interpreter = null, string? scriptDirectory = null)
        {
            _gameInstance = gameInstance;
            _interpreter = interpreter;
            string baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            _scriptDirectory = scriptDirectory ?? Path.Combine(baseDir, "scripts");
        }

        public MenuState CurrentMenu => _currentMenu;
        public InputMode CurrentInputMode => _inputMode;
        public bool IsActive => _currentMenu != MenuState.None || _inputMode != InputMode.None;

        public async Task ExitMenuAsync()
        {
            _currentMenu = MenuState.None;
            _inputMode = InputMode.None;
            _inputBuffer.Clear();
            _skipNextLineFeed = false;

            string currentAnsiLine = _gameInstance.IsConnected ? ScriptRef.GetCurrentAnsiLine() : string.Empty;
            string exitText = "\r" + AnsiCodes.ANSI_CLEARLINE;

            if (!string.IsNullOrEmpty(currentAnsiLine))
                exitText += currentAnsiLine;

            await _gameInstance.SendMessageAsync(exitText);
        }

        /// <summary>
        /// Process a menu command (single character)
        /// </summary>
        public async Task<bool> HandleMenuCommandAsync(char command)
        {
            // Ignore non-printable characters (except carriage return)
            if (command < 32 && command != '\r')
            {
                return true; // Handled (ignored)
            }

            // If no menu is active, check if this is the command char to enter main menu
            if (_currentMenu == MenuState.None)
            {
                _currentMenu = MenuState.Main;
                await _gameInstance.SendMessageAsync("\r\n");
                await ShowMenuPromptAsync();
                return true;
            }

            // Process command based on current menu
            switch (_currentMenu)
            {
                case MenuState.Main:
                    return await HandleMainMenuAsync(command);
                case MenuState.Data:
                    return await HandleDataMenuAsync(command);
                case MenuState.Port:
                    return await HandlePortMenuAsync(command);
                case MenuState.Script:
                    return await HandleScriptMenuAsync(command);
                case MenuState.Setup:
                    return await HandleSetupMenuAsync(command);
                default:
                    return false;
            }
        }

        private async Task<bool> HandleMainMenuAsync(char command)
        {
            switch (char.ToUpper(command))
            {
                case '?':
                    await ShowMainMenuHelpAsync();
                    return true;

                case '+':
                    await _gameInstance.SendMessageAsync("\r\nEnter command for help: ");
                    _inputMode = InputMode.CommandHelp;
                    _inputBuffer.Clear();
                    return true;

                case 'Q':
                    await ExitMenuAsync();
                    return true;

                case '-':
                    await ShowAllClientsAsync();
                    return true;

                case '/':
                    _streamingMode = !_streamingMode;
                    await _gameInstance.SendMessageAsync($"\r\nStreaming mode {(_streamingMode ? "enabled" : "disabled")}.\r\n");
                    await ShowMenuPromptAsync();
                    return true;

                case '=':
                    _deafMode = !_deafMode;
                    await _gameInstance.SendMessageAsync($"\r\nDeaf client {(_deafMode ? "enabled" : "disabled")}.\r\n");
                    await ShowMenuPromptAsync();
                    return true;

                case 'B':
                    await _gameInstance.SendMessageAsync("\r\nEnter text to burst: ");
                    _inputMode = InputMode.BurstInput;
                    _inputBuffer.Clear();
                    return true;

                case 'C':
                    await ConnectDisconnectAsync();
                    return true;

                case 'D':
                    _currentMenu = MenuState.Data;
                    await ShowDataMenuAsync();
                    return true;

                case 'E':
                    await EditSendLastBurstAsync();
                    return true;

                case 'H':
                    await ToggleNativeHaggleAsync();
                    return true;

                case 'P':
                    _currentMenu = MenuState.Port;
                    await ShowPortMenuAsync();
                    return true;

                case 'R':
                    await RepeatLastBurstAsync();
                    return true;

                case 'S':
                    _currentMenu = MenuState.Script;
                    await ShowScriptMenuAsync();
                    return true;

                case 'T':
                    _currentMenu = MenuState.Setup;
                    await ShowSetupMenuAsync();
                    return true;

                case 'X':
                    await ExitProgramAsync();
                    return true;

                case 'Z':
                    await StopAllScriptsAsync();
                    return true;

                default:
                    await _gameInstance.SendMessageAsync($"\r\nUnknown command '{command}'. Press ? for help.\r\n");
                    await ShowMenuPromptAsync();
                    return true;
            }
        }

        private async Task<bool> HandleDataMenuAsync(char command)
        {
            switch (char.ToUpper(command))
            {
                case '?':
                    await ShowDataMenuHelpAsync();
                    return true;

                case 'Q':
                    _currentMenu = MenuState.Main;
                    await ShowMenuPromptAsync();
                    return true;

                case 'C':
                    await _gameInstance.SendMessageAsync("\r\nClearing database...\r\n");
                    // TODO: Implement database clear
                    await ShowDataMenuPromptAsync();
                    return true;

                case 'L':
                    await _gameInstance.SendMessageAsync("\r\nLoading database...\r\n");
                    // TODO: Implement database load
                    await ShowDataMenuPromptAsync();
                    return true;

                case 'S':
                    await _gameInstance.SendMessageAsync("\r\nSaving database...\r\n");
                    // TODO: Implement database save
                    await ShowDataMenuPromptAsync();
                    return true;

                case 'R':
                    await _gameInstance.SendMessageAsync("\r\nAre you sure you want to reset all sector data? (y/N): ");
                    _inputMode = InputMode.DataResetConfirm;
                    _inputBuffer.Clear();
                    return true;

                default:
                    await _gameInstance.SendMessageAsync($"\r\nUnknown command '{command}'. Press ? for help.\r\n");
                    await ShowDataMenuPromptAsync();
                    return true;
            }
        }

        private async Task<bool> HandlePortMenuAsync(char command)
        {
            switch (char.ToUpper(command))
            {
                case '?':
                    await ShowPortMenuHelpAsync();
                    return true;

                case 'Q':
                    _currentMenu = MenuState.Main;
                    await ShowMenuPromptAsync();
                    return true;

                // TODO: Add port-specific commands

                default:
                    await _gameInstance.SendMessageAsync($"\r\nUnknown command '{command}'. Press ? for help.\r\n");
                    await ShowPortMenuPromptAsync();
                    return true;
            }
        }

        private async Task<bool> HandleScriptMenuAsync(char command)
        {
            switch (char.ToUpper(command))
            {
                case '?':
                    await ShowScriptMenuHelpAsync();
                    return true;

                case 'Q':
                    _currentMenu = MenuState.Main;
                    await ShowMenuPromptAsync();
                    return true;

                case 'S':
                    await _gameInstance.SendMessageAsync("\r\nEnter script name to load: ");
                    _inputMode = InputMode.ScriptLoad;
                    _inputBuffer.Clear();
                    return true;

                case 'L':
                    await ReloadLastScriptAsync();
                    return true;

                case 'U':
                    await _gameInstance.SendMessageAsync("\r\nEnter script name to unload: ");
                    _inputMode = InputMode.ScriptUnload;
                    _inputBuffer.Clear();
                    return true;

                case 'P':
                    await _gameInstance.SendMessageAsync("\r\nEnter script name to pause: ");
                    _inputMode = InputMode.ScriptPause;
                    _inputBuffer.Clear();
                    return true;

                case 'R':
                    await _gameInstance.SendMessageAsync("\r\nEnter script name to resume: ");
                    _inputMode = InputMode.ScriptResume;
                    _inputBuffer.Clear();
                    return true;

                case 'Z':
                    await StopAllScriptsAsync();
                    return true;

                case 'X':
                    await ExitCurrentScriptAsync();
                    return true;

                case 'I':
                    await ListScriptsAsync();
                    return true;

                case 'K':
                    await _gameInstance.SendMessageAsync("\r\nKill> ");
                    _inputMode = InputMode.ScriptKill;
                    _inputBuffer.Clear();
                    return true;

                default:
                    await _gameInstance.SendMessageAsync($"\r\nUnknown command '{command}'. Press ? for help.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return true;
            }
        }

        private async Task<bool> HandleSetupMenuAsync(char command)
        {
            switch (char.ToUpper(command))
            {
                case '?':
                    await ShowSetupMenuHelpAsync();
                    return true;

                case 'Q':
                    _currentMenu = MenuState.Main;
                    await ShowMenuPromptAsync();
                    return true;

                // TODO: Add setup-specific commands

                default:
                    await _gameInstance.SendMessageAsync($"\r\nUnknown command '{command}'. Press ? for help.\r\n");
                    await ShowSetupMenuPromptAsync();
                    return true;
            }
        }

        private async Task ShowMenuPromptAsync()
        {
            await _gameInstance.SendMessageAsync("Main> ");
        }

        private async Task ShowDataMenuPromptAsync()
        {
            await _gameInstance.SendMessageAsync("Data> ");
        }

        private async Task ShowPortMenuPromptAsync()
        {
            await _gameInstance.SendMessageAsync("Port> ");
        }

        private async Task ShowScriptMenuPromptAsync()
        {
            await _gameInstance.SendMessageAsync("Script> ");
        }

        private async Task ShowSetupMenuPromptAsync()
        {
            await _gameInstance.SendMessageAsync("Setup> ");
        }

        private async Task ShowMainMenuHelpAsync()
        {
            var help = new StringBuilder();
            help.Append("\r\nMain menu:\r\n");
            help.Append("  ? - Command list\r\n");
            help.Append("  + - Help on command\r\n");
            help.Append("  Q - Exit menu\r\n");
            help.Append("  - - Show all clients\r\n");
            help.Append("  / - Enable Streaming Mode\r\n");
            help.Append("  = - Toggle deaf client\r\n");
            help.Append("  B - Send burst\r\n");
            help.Append("  C - Connect/Disconnect from server\r\n");
            help.Append("  D - Data menu\r\n");
            help.Append("  E - Edit/Send last burst\r\n");
            help.Append("  H - Toggle native haggle\r\n");
            help.Append("  P - Port menu\r\n");
            help.Append("  R - Repeat last burst\r\n");
            help.Append("  S - Script menu\r\n");
            help.Append("  T - Setup menu\r\n");
            help.Append("  X - Exit Program\r\n");
            help.Append("  Z - Stop all scripts\r\n");
            help.Append("\r\n");
            await _gameInstance.SendMessageAsync(help.ToString());
            await ShowMenuPromptAsync();
        }

        private async Task ShowDataMenuHelpAsync()
        {
            var help = new StringBuilder();
            help.Append("\r\nData menu:\r\n");
            help.Append("  ? - Command list\r\n");
            help.Append("  Q - Return to main menu\r\n");
            help.Append("  C - Clear database\r\n");
            help.Append("  L - Load database\r\n");
            help.Append("  R - Reset all sector data\r\n");
            help.Append("  S - Save database\r\n");
            help.Append("\r\n");
            await _gameInstance.SendMessageAsync(help.ToString());
            await ShowDataMenuPromptAsync();
        }

        private async Task ShowPortMenuHelpAsync()
        {
            var help = new StringBuilder();
            help.Append("\r\nPort menu:\r\n");
            help.Append("  ? - Command list\r\n");
            help.Append("  Q - Return to main menu\r\n");
            help.Append("\r\n");
            await _gameInstance.SendMessageAsync(help.ToString());
            await ShowPortMenuPromptAsync();
        }

        private async Task ShowScriptMenuHelpAsync()
        {
            var help = new StringBuilder();
            help.Append("\r\nScript menu:\r\n");
            help.Append("  ? - Command list\r\n");
            help.Append("  Q - Return to main menu\r\n");
            help.Append("  S - Load script\r\n");
            help.Append("  L - Reload last script\r\n");
            help.Append("  U - Unload script\r\n");
            help.Append("  P - Pause script\r\n");
            help.Append("  R - Resume script\r\n");
            help.Append("  Z - Stop all scripts\r\n");
            help.Append("  X - Terminate script and exit menu\r\n");
            help.Append("  I - List scripts\r\n");
            help.Append("  K - Kill script\r\n");
            help.Append("\r\n");
            await _gameInstance.SendMessageAsync(help.ToString());
            await ShowScriptMenuPromptAsync();
        }

        private async Task ShowSetupMenuHelpAsync()
        {
            var help = new StringBuilder();
            help.Append("\r\nSetup menu:\r\n");
            help.Append("  ? - Command list\r\n");
            help.Append("  Q - Return to main menu\r\n");
            help.Append("\r\n");
            await _gameInstance.SendMessageAsync(help.ToString());
            await ShowSetupMenuPromptAsync();
        }

        private async Task ShowDataMenuAsync()
        {
            await _gameInstance.SendMessageAsync("\r\nData menu - Press ? for help\r\n");
            await ShowDataMenuPromptAsync();
        }

        private async Task ShowPortMenuAsync()
        {
            await _gameInstance.SendMessageAsync("\r\nPort menu - Press ? for help\r\n");
            await ShowPortMenuPromptAsync();
        }

        private async Task ShowScriptMenuAsync()
        {
            await _gameInstance.SendMessageAsync("\r\nScript menu - Press ? for help\r\n");
            await ShowScriptMenuPromptAsync();
        }

        private async Task ShowSetupMenuAsync()
        {
            await _gameInstance.SendMessageAsync("\r\nSetup menu - Press ? for help\r\n");
            await ShowSetupMenuPromptAsync();
        }

        private async Task ShowAllClientsAsync()
        {
            await _gameInstance.SendMessageAsync($"\r\nConnected clients: 1 ({_gameInstance.GameName})\r\n");
            await ShowMenuPromptAsync();
        }

        private async Task ConnectDisconnectAsync()
        {
            // Exit menu first
            _currentMenu = MenuState.None;
            
            if (_gameInstance.IsConnected)
            {
                await _gameInstance.SendMessageAsync("\r\nDisconnecting from server...\r\n");
                await _gameInstance.DisconnectFromServerAsync();
            }
            else
            {
                await _gameInstance.SendMessageAsync("\r\nConnecting to server...\r\n");
                try
                {
                    await _gameInstance.ConnectToServerAsync();
                    await _gameInstance.SendMessageAsync("Connected!\r\n");
                }
                catch (Exception ex)
                {
                    await _gameInstance.SendMessageAsync($"Connection failed: {ex.Message}\r\n");
                }
            }
        }

        private async Task EditSendLastBurstAsync()
        {
            if (string.IsNullOrEmpty(_lastBurst))
            {
                await _gameInstance.SendMessageAsync("\r\nNo previous burst to edit.\r\n");
                await ShowMenuPromptAsync();
            }
            else
            {
                await _gameInstance.SendMessageAsync($"\r\nLast burst: {_lastBurst}\r\n");
                await _gameInstance.SendMessageAsync("Enter new burst (or press Enter to send as-is): ");
                _inputMode = InputMode.BurstEdit;
                _inputBuffer.Clear();
                _inputBuffer.Append(_lastBurst);
            }
        }

        private async Task ToggleNativeHaggleAsync()
        {
            bool enabled = _gameInstance.ToggleNativeHaggle();
            await _gameInstance.SendMessageAsync($"\r\nNative haggle {(enabled ? "enabled" : "disabled")}.\r\n");
            await ExitMenuAsync();
        }

        private async Task RepeatLastBurstAsync()
        {
            if (string.IsNullOrEmpty(_lastBurst))
            {
                await _gameInstance.SendMessageAsync("\r\nNo previous burst to repeat.\r\n");
            }
            else
            {
                await _gameInstance.SendMessageAsync($"\r\nRepeating burst: {_lastBurst}\r\n");
                var data = Encoding.ASCII.GetBytes(_lastBurst + "\r\n");
                await _gameInstance.SendToServerAsync(data);
            }
            await ShowMenuPromptAsync();
        }

        private async Task StopAllScriptsAsync()
        {
            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                }
                else
                {
                    int count = _interpreter.Count;
                    await _gameInstance.SendMessageAsync($"\r\nStopping {count} script(s)...\r\n");
                    _interpreter.StopAll(false); // Don't stop system scripts
                    await _gameInstance.SendMessageAsync("All scripts stopped.\r\n");
                }
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError stopping scripts: {ex.Message}\r\n");
            }
            
            await ShowMenuPromptAsync();
        }

        private async Task ExitProgramAsync()
        {
            TWXProxy.Core.GlobalModules.DebugLog("[Menu] ExitProgramAsync called — stopping proxy\n");
            TWXProxy.Core.GlobalModules.FlushDebugLog();
            await _gameInstance.SendMessageAsync("\r\nExiting TWX Proxy...\r\n");
            await _gameInstance.StopAsync();
        }

        public void SetLastBurst(string burst)
        {
            _lastBurst = burst;
        }

        /// <summary>
        /// Handle input collection (for filenames, burst text, etc.)
        /// </summary>
        public async Task<bool> HandleInputCharAsync(char c)
        {
            int charCode = (int)c;
            
            // Ignore NULL characters
            if (charCode == 0)
            {
                _skipNextLineFeed = false; // Clear the skip flag if we get a NULL
                return true; // Handled (ignored)
            }
            
            // Check for pending \n after \r (must check first, even if InputMode is None)
            if (c == '\n' && _skipNextLineFeed)
            {
                _skipNextLineFeed = false;
                return true;
            }

            if (_inputMode == InputMode.None)
            {
                return false;
            }

            // Check for line ending
            if (c == '\r' || c == '\n')
            {
                // Process the collected input
                var input = _inputBuffer.ToString().Trim();
                _inputBuffer.Clear();

                var mode = _inputMode;
                _inputMode = InputMode.None;

                // Set flag to skip \n after \r
                _skipNextLineFeed = (c == '\r');

                await ProcessCollectedInputAsync(mode, input);
                return true;
            }

            // Backspace handling
            if (c == '\b' || c == 127) // backspace or DEL
            {
                _skipNextLineFeed = false;
                if (_inputBuffer.Length > 0)
                {
                    _inputBuffer.Length--;
                    // Echo backspace
                    await _gameInstance.SendMessageAsync("\b \b");
                }
                return true;
            }

            // Regular character - add to buffer and echo
            _skipNextLineFeed = false;
            _inputBuffer.Append(c);
            // Echo the character back to client
            await _gameInstance.SendMessageAsync(c.ToString());
            return true;
        }

        private async Task ProcessCollectedInputAsync(InputMode mode, string input)
        {
            switch (mode)
            {
                case InputMode.ScriptLoad:
                    await LoadScriptAsync(input);
                    break;

                case InputMode.ScriptUnload:
                    await UnloadScriptAsync(input);
                    break;

                case InputMode.ScriptPause:
                    await PauseScriptAsync(input);
                    break;

                case InputMode.ScriptResume:
                    await ResumeScriptAsync(input);
                    break;

                case InputMode.ScriptKill:
                    await KillScriptAsync(input);
                    break;

                case InputMode.BurstInput:
                    await SendBurstAsync(input);
                    break;

                case InputMode.BurstEdit:
                    await SendBurstAsync(string.IsNullOrEmpty(input) ? _lastBurst : input);
                    break;

                case InputMode.CommandHelp:
                    await ShowCommandHelpAsync(input);
                    break;

                case InputMode.DataResetConfirm:
                    if (input == "y" || input == "Y")
                    {
                        TWXProxy.Core.GlobalModules.Database?.ResetSectors();
                        await _gameInstance.SendMessageAsync("\r\nAll sector data has been cleared.\r\n");
                    }
                    else
                    {
                        await _gameInstance.SendMessageAsync("\r\nReset cancelled.\r\n");
                    }
                    _currentMenu = MenuState.Data;
                    await ShowDataMenuPromptAsync();
                    break;
            }
        }

        private async Task LoadScriptAsync(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                await _gameInstance.SendMessageAsync("\r\nScript name cannot be empty.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            try
            {
                // Check if interpreter is available
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                // Build full path - check for extension
                string fullPath;
                if (Path.IsPathRooted(scriptName))
                {
                    // Absolute path provided
                    fullPath = scriptName;
                }
                else
                {
                    // Relative path - use script directory
                    fullPath = Path.Combine(_scriptDirectory, scriptName);
                }

                // Add default extension if none provided
                if (string.IsNullOrEmpty(Path.GetExtension(fullPath)))
                {
                    // Try .cts first (compiled bytecode)
                    if (File.Exists(fullPath + ".cts"))
                    {
                        fullPath += ".cts";
                    }
                    else if (File.Exists(fullPath + ".ts"))
                    {
                        // Try .ts (source)
                        fullPath += ".ts";
                    }
                    else
                    {
                        // Default to .cts
                        fullPath += ".cts";
                    }
                }

                // Check if file exists
                if (!File.Exists(fullPath))
                {
                    await _gameInstance.SendMessageAsync($"\r\nScript file not found: {fullPath}\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                await _gameInstance.SendMessageAsync($"\r\nLoading script: {Path.GetFileName(fullPath)}\r\n");
                
                // Load and execute the script
                _interpreter.Load(fullPath, false);
                
                _lastScript = fullPath;
                
                // Exit menu mode after loading script
                _currentMenu = MenuState.None;
                await _gameInstance.SendMessageAsync("\r\n");
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError loading script: {ex.Message}\r\n");
                await ShowScriptMenuPromptAsync();
            }
        }

        private async Task ReloadLastScriptAsync()
        {
            if (string.IsNullOrEmpty(_lastScript))
            {
                await _gameInstance.SendMessageAsync("\r\nNo script has been loaded yet.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            // Reuse LoadScriptAsync with the saved full path
            await LoadScriptAsync(_lastScript);
        }

        private async Task UnloadScriptAsync(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                await _gameInstance.SendMessageAsync("\r\nScript name cannot be empty.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                await _gameInstance.SendMessageAsync($"\r\nUnloading script: {scriptName}\r\n");
                
                // Find and stop the script
                bool found = false;
                for (int i = _interpreter.Count - 1; i >= 0; i--)
                {
                    var script = _interpreter.GetScript(i);
                    if (script != null)
                    {
                        string scriptFile = Path.GetFileNameWithoutExtension(script.ScriptName ?? "");
                        string searchName = Path.GetFileNameWithoutExtension(scriptName);
                        
                        if (scriptFile.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            _interpreter.Stop(i);
                            found = true;
                            await _gameInstance.SendMessageAsync($"Script '{scriptName}' unloaded.\r\n");
                            break;
                        }
                    }
                }
                
                if (!found)
                {
                    await _gameInstance.SendMessageAsync($"Script '{scriptName}' not found or not running.\r\n");
                }
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError unloading script: {ex.Message}\r\n");
            }
            
            await ShowScriptMenuPromptAsync();
        }

        private async Task PauseScriptAsync(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                await _gameInstance.SendMessageAsync("\r\nScript name cannot be empty.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                await _gameInstance.SendMessageAsync($"\r\nPausing script: {scriptName}\r\n");
                
                bool found = false;
                for (int i = 0; i < _interpreter.Count; i++)
                {
                    var script = _interpreter.GetScript(i);
                    if (script != null)
                    {
                        string scriptFile = Path.GetFileNameWithoutExtension(script.ScriptName ?? "");
                        string searchName = Path.GetFileNameWithoutExtension(scriptName);
                        
                        if (scriptFile.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            script.Pause();
                            found = true;
                            await _gameInstance.SendMessageAsync($"Script '{scriptName}' paused.\r\n");
                            break;
                        }
                    }
                }
                
                if (!found)
                {
                    await _gameInstance.SendMessageAsync($"Script '{scriptName}' not found or not running.\r\n");
                }
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError pausing script: {ex.Message}\r\n");
            }
            
            await ShowScriptMenuPromptAsync();
        }

        private async Task ResumeScriptAsync(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
            {
                await _gameInstance.SendMessageAsync("\r\nScript name cannot be empty.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                await _gameInstance.SendMessageAsync($"\r\nResuming script: {scriptName}\r\n");
                
                bool found = false;
                for (int i = 0; i < _interpreter.Count; i++)
                {
                    var script = _interpreter.GetScript(i);
                    if (script != null)
                    {
                        string scriptFile = Path.GetFileNameWithoutExtension(script.ScriptName ?? "");
                        string searchName = Path.GetFileNameWithoutExtension(scriptName);
                        
                        if (scriptFile.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            script.Resume();
                            found = true;
                            await _gameInstance.SendMessageAsync($"Script '{scriptName}' resumed.\r\n");
                            break;
                        }
                    }
                }
                
                if (!found)
                {
                    await _gameInstance.SendMessageAsync($"Script '{scriptName}' not found or not paused.\r\n");
                }
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError resuming script: {ex.Message}\r\n");
            }
            
            await ShowScriptMenuPromptAsync();
        }

        private async Task ExitCurrentScriptAsync()
        {
            TWXProxy.Core.GlobalModules.DebugLog("[Menu] ExitCurrentScriptAsync called\n");
            TWXProxy.Core.GlobalModules.FlushDebugLog();
            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                // Find the most recently loaded non-system script
                Script? targetScript = null;
                int targetIndex = -1;
                
                for (int i = _interpreter.Count - 1; i >= 0; i--)
                {
                    var script = _interpreter.GetScript(i);
                    if (script != null && !script.System)
                    {
                        targetScript = script;
                        targetIndex = i;
                        break;
                    }
                }

                if (targetScript != null)
                {
                    string scriptFile = targetScript.Compiler?.ScriptFile ?? targetScript.ScriptName;
                    await _gameInstance.SendMessageAsync($"\r\nScript terminated: {scriptFile}\r\n");
                    _interpreter.Stop(targetIndex);
                }
                else
                {
                    await _gameInstance.SendMessageAsync("\r\nNo non-system scripts running.\r\n");
                }
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError exiting script: {ex.Message}\r\n");
            }

            // Exit the menu after terminating the script
            _currentMenu = MenuState.None;
            await _gameInstance.SendMessageAsync("\r\n");
        }

        private async Task ListScriptsAsync()
        {
            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                var output = new StringBuilder();
                output.Append("\r\n\r\nID\tFile\r\n");
                
                for (int i = 0; i < _interpreter.Count; i++)
                {
                    var script = _interpreter.GetScript(i);
                    if (script != null)
                    {
                        string scriptFile = script.Compiler?.ScriptFile ?? script.ScriptName;
                        output.Append($"{i}\t{scriptFile}\r\n");
                    }
                }
                
                output.Append("\r\n");
                await _gameInstance.SendMessageAsync(output.ToString());
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError listing scripts: {ex.Message}\r\n");
            }
            
            await ShowScriptMenuPromptAsync();
        }

        private async Task KillScriptAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                await _gameInstance.SendMessageAsync("\r\nScript ID cannot be empty.\r\n");
                await ShowScriptMenuPromptAsync();
                return;
            }

            try
            {
                if (_interpreter == null)
                {
                    await _gameInstance.SendMessageAsync("\r\nScript interpreter not available.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                if (!int.TryParse(input.Trim(), out int scriptId))
                {
                    await _gameInstance.SendMessageAsync($"\r\nInvalid script ID: '{input}'. Please enter a number.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                if (scriptId < 0 || scriptId >= _interpreter.Count)
                {
                    await _gameInstance.SendMessageAsync($"\r\nScript ID {scriptId} out of range (0-{_interpreter.Count - 1}).\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                var script = _interpreter.GetScript(scriptId);
                if (script == null)
                {
                    await _gameInstance.SendMessageAsync($"\r\nScript ID {scriptId} not found.\r\n");
                    await ShowScriptMenuPromptAsync();
                    return;
                }

                string scriptFile = script.Compiler?.ScriptFile ?? script.ScriptName;
                await _gameInstance.SendMessageAsync($"\r\nScript terminated: {scriptFile}\r\n");
                _interpreter.Stop(scriptId);
            }
            catch (Exception ex)
            {
                await _gameInstance.SendMessageAsync($"\r\nError killing script: {ex.Message}\r\n");
            }
            
            await ShowScriptMenuPromptAsync();
        }

        private async Task SendBurstAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await _gameInstance.SendMessageAsync("\r\nBurst text cannot be empty.\r\n");
            }
            else
            {
                _lastBurst = text;
                await _gameInstance.SendMessageAsync($"\r\nSending burst: {text}\r\n");
                var data = Encoding.ASCII.GetBytes(text + "\r\n");
                await _gameInstance.SendToServerAsync(data);
            }
            await ShowMenuPromptAsync();
        }

        private async Task ShowCommandHelpAsync(string command)
        {
            await _gameInstance.SendMessageAsync($"\r\nHelp for command '{command}' not yet implemented.\r\n");
            await ShowMenuPromptAsync();
        }
    }
}
