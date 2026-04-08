using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TWXProxy.Core
{
    /// <summary>
    /// Interface for script windows that can display text content
    /// </summary>
    public interface IScriptWindow : IDisposable
    {
        string Name { get; }
        string Title { get; }
        int Width { get; }
        int Height { get; }
        bool OnTop { get; }
        string TextContent { get; set; }
        bool IsVisible { get; }
        void Show();
        void Hide();
    }

    /// <summary>
    /// Factory for creating script windows
    /// </summary>
    public interface IScriptWindowFactory
    {
        IScriptWindow CreateWindow(string name, string title, int width, int height, bool onTop = false);
    }

    /// <summary>
    /// Service that the UI layer provides so script windows can add/remove
    /// draggable panels inside the main application window.
    /// </summary>
    public interface IPanelOverlayService
    {
        void AddPanel(string name, string title, int width, int height, Action<string> contentSetter);
        void UpdatePanel(string name, string content);
        void RemovePanel(string name);
    }

    /// <summary>
    /// Default stub implementation (logs to console, no actual window)
    /// </summary>
    public class ConsoleScriptWindow : IScriptWindow
    {
        private readonly string _name;
        private readonly string _title;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _onTop;
        private string _textContent;
        private bool _isVisible;

        public ConsoleScriptWindow(string name, string title, int width, int height, bool onTop = false)
        {
            _name = name;
            _title = title;
            _width = width;
            _height = height;
            _onTop = onTop;
            _textContent = string.Empty;
            _isVisible = false;
        }

        public string Name => _name;
        public string Title => _title;
        public int Width => _width;
        public int Height => _height;
        public bool OnTop => _onTop;
        
        public string TextContent
        {
            get => _textContent;
            set
            {
                _textContent = value;
                UpdateContent();
            }
        }

        public bool IsVisible => _isVisible;

        /// <summary>
        /// Show the window
        /// </summary>
        public void Show()
        {
            if (!_isVisible)
            {
                _isVisible = true;
                CreateWindow();
            }
        }

        /// <summary>
        /// Hide the window
        /// </summary>
        public void Hide()
        {
            if (_isVisible)
            {
                _isVisible = false;
                DestroyWindow();
            }
        }

        /// <summary>
        /// Create the window (console stub version)
        /// </summary>
        private void CreateWindow()
        {
            Console.WriteLine($"[ScriptWindow] Created window '{_name}' - {_width}x{_height} - '{_title}' (console stub)");
        }

        /// <summary>
        /// Update window content (console stub version)
        /// </summary>
        private void UpdateContent()
        {
            if (_isVisible)
            {
                Console.WriteLine($"[ScriptWindow] Updated '{_name}' content: {_textContent.Substring(0, Math.Min(50, _textContent.Length))}...");
            }
        }

        /// <summary>
        /// Destroy the window (console stub version)
        /// </summary>
        private void DestroyWindow()
        {
            Console.WriteLine($"[ScriptWindow] Destroyed window '{_name}'");
        }

        public void Dispose()
        {
            Hide();
        }
    }

    /// <summary>
    /// Default factory that creates console stub windows
    /// </summary>
    public class ConsoleScriptWindowFactory : IScriptWindowFactory
    {
        public IScriptWindow CreateWindow(string name, string title, int width, int height, bool onTop = false)
        {
            return new ConsoleScriptWindow(name, title, width, height, onTop);
        }
    }

    /// <summary>
    /// Menu item for custom script menus
    /// </summary>
    public class MenuItem
    {
        public string Parent { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public char Hotkey { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool CloseMenu { get; set; }
        public string Value { get; set; } = string.Empty;
        public string Help { get; set; } = string.Empty;
        public object? Script { get; set; }

        // Menu options (Q, ?, +)
        public bool OptionQ { get; set; }
        public bool OptionHelp { get; set; }
        public bool OptionPlus { get; set; }

        public void SetOptions(bool q, bool help, bool plus)
        {
            OptionQ = q;
            OptionHelp = help;
            OptionPlus = plus;
        }
    }

    /// <summary>
    /// Menu system for managing custom script menus
    /// </summary>
    public interface ITWXMenu
    {
        MenuItem? AddCustomMenu(string parent, string name, string description, 
            string reference, string prompt, char hotkey, bool closeMenu, object script);
        void OpenMenu(string menuName, int flags);
        void CloseMenu(bool force);
        MenuItem? GetMenuByName(string menuName);
        void BeginScriptInput(Script script, CmdParam varParam, bool singleKey);
        void SuspendMenuForInput();
        void RestoreSuspendedMenuIfNeeded();
        bool HasSuspendedMenu { get; }
    }

    /// <summary>
    /// Simple menu manager implementation
    /// </summary>
    public class MenuManager : ITWXMenu
    {
        private readonly Dictionary<string, MenuItem> _menus = new Dictionary<string, MenuItem>(StringComparer.OrdinalIgnoreCase);
        private Stack<MenuItem> _menuStack = new Stack<MenuItem>();
        private List<MenuItem>? _suspendedMenuStack;
        private Script? _inputScript;
        private CmdParam? _inputVarParam;
        private bool _inputSingleKey;

        public MenuItem? AddCustomMenu(string parent, string name, string description,
            string reference, string prompt, char hotkey, bool closeMenu, object script)
        {
            var menuItem = new MenuItem
            {
                Parent = parent.ToUpper(),
                Name = name.ToUpper(),
                Description = description,
                Hotkey = hotkey,
                Reference = reference,
                Prompt = prompt,
                CloseMenu = closeMenu,
                Script = script
            };

            _menus[name.ToUpper()] = menuItem;
            return menuItem;
        }

        public void OpenMenu(string menuName, int flags)
        {
            menuName = menuName.ToUpper();

            if (!_menus.TryGetValue(menuName, out var menu))
                throw new Exception($"Menu '{menuName}' not found");

            if (_suspendedMenuStack is { Count: > 0 })
            {
                int suspendedIndex = _suspendedMenuStack.FindIndex(m =>
                    m.Name.Equals(menuName, StringComparison.OrdinalIgnoreCase));
                if (suspendedIndex >= 0)
                {
                    _menuStack.Clear();
                    for (int i = 0; i <= suspendedIndex; i++)
                        _menuStack.Push(_suspendedMenuStack[i]);

                    GlobalModules.DebugLog(
                        $"[Menu] Restored suspended stack directly to '{menuName}' (depth={_menuStack.Count})\n");
                    _suspendedMenuStack = null;
                    DisplayScriptMenu(_menuStack.Peek());
                    return;
                }
            }

            if (_menuStack.Count > 0 &&
                _menuStack.Peek().Name.Equals(menuName, StringComparison.OrdinalIgnoreCase))
            {
                GlobalModules.DebugLog($"[Menu] OpenMenu('{menuName}') ignored because it is already current\n");
                DisplayScriptMenu(_menuStack.Peek());
                return;
            }

            _suspendedMenuStack = null;
            _menuStack.Push(menu);

            // Display the menu to the user
            DisplayScriptMenu(menu);
        }

        public void CloseMenu(bool force)
        {
            if (_menuStack.Count > 0)
            {
                var menu = _menuStack.Pop();
                Console.WriteLine($"[Menu] Closed menu '{menu.Name}'");

                if (_menuStack.Count == 0)
                {
                    ClearMenuDisplay();
                }
            }
        }

        public MenuItem? GetMenuByName(string menuName)
        {
            menuName = menuName.ToUpper();
            if (_menus.TryGetValue(menuName, out var menu))
            {
                return menu;
            }
            throw new Exception($"Menu '{menuName}' not found");
        }

        public void BeginScriptInput(Script script, CmdParam varParam, bool singleKey)
        {
            _inputScript = script;
            _inputVarParam = varParam;
            _inputSingleKey = singleKey;

            // Start async input collection
            if (singleKey)
            {
                // Single key input mode
                Task.Run(() =>
                {
                    var key = Console.ReadKey(true);
                    CompleteInput(key.KeyChar.ToString());
                });
            }
            else
            {
                // Line input mode
                Task.Run(() =>
                {
                    var line = Console.ReadLine() ?? string.Empty;
                    CompleteInput(line);
                });
            }
        }
        
        public bool IsMenuOpen()
        {
            return _menuStack.Count > 0;
        }

        public bool IsMenuOnStack(string menuName)
        {
            return _menuStack.Any(m => m.Name.Equals(menuName, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasSuspendedMenu => _suspendedMenuStack is { Count: > 0 };

        public void SuspendMenuForInput()
        {
            if (_menuStack.Count == 0)
                return;

            _suspendedMenuStack = _menuStack.Reverse().ToList();
            _menuStack.Clear();
            GlobalModules.DebugLog($"[Menu] Suspended menu stack for GETINPUT (depth={_suspendedMenuStack.Count})\n");
            ClearMenuDisplay(restoreCurrentLine: false);
        }

        public void RestoreSuspendedMenuIfNeeded()
        {
            if (_menuStack.Count > 0 || _suspendedMenuStack is not { Count: > 0 })
                return;

            foreach (MenuItem item in _suspendedMenuStack)
                _menuStack.Push(item);

            GlobalModules.DebugLog($"[Menu] Restored suspended menu stack (depth={_menuStack.Count})\n");
            _suspendedMenuStack = null;
            DisplayScriptMenu(_menuStack.Peek());
        }

        public void ClearSuspendedMenu()
        {
            if (_suspendedMenuStack is not { Count: > 0 })
                return;

            GlobalModules.DebugLog($"[Menu] Cleared suspended menu stack (depth={_suspendedMenuStack.Count})\n");
            _suspendedMenuStack = null;
        }

        public void RedisplayCurrentMenu()
        {
            if (_menuStack.Count > 0)
            {
                var currentMenu = _menuStack.Peek();
                DisplayScriptMenu(currentMenu);
            }
        }

        private static void ClearMenuDisplay(bool restoreCurrentLine = true)
        {
            string exitText = "\r" + AnsiCodes.ANSI_CLEARLINE;
            if (restoreCurrentLine)
            {
                string currentAnsiLine = ScriptRef.GetCurrentAnsiLine();
                if (!string.IsNullOrEmpty(currentAnsiLine))
                    exitText += currentAnsiLine;
            }

            GlobalModules.TWXServer?.ClientMessage(exitText);
        }

        private void CompleteInput(string inputText)
        {
            if (_inputScript != null && _inputVarParam != null)
            {
                _inputScript.InputCompleted(inputText, _inputVarParam);
                _inputScript = null;
                _inputVarParam = null;
            }
        }
        
        private void DisplayScriptMenu(MenuItem menu)
        {
            var server = GlobalModules.TWXServer;
            
            if (GlobalModules.DebugMode)
                GlobalModules.DebugLog($"[DEBUG] Displaying menu: {menu.Name}, Parent: {menu.Parent}, Options: Q={menu.OptionQ} ?={menu.OptionHelp} +={menu.OptionPlus}\n");
            
            // Match Pascal menu behavior more closely: redraw the current line
            // before showing the menu header instead of forcing a leading blank line.
            string title = !string.IsNullOrEmpty(menu.Prompt) ? menu.Prompt : menu.Name;
            server?.ClientMessage(AnsiCodes.ANSI_CLEARLINE + "\r" + title + "\r\n");
            
            // Get all menu items that have this menu as their parent
            // Note: Parent '0' means display in all menus (like root-level items)
            if (GlobalModules.DebugMode)
            {
                GlobalModules.DebugLog($"[DEBUG] Checking children for menu '{menu.Name}' (parent='{menu.Parent}'):\n");
                foreach (var m in _menus.Values)
                {
                    bool parentMatch = m.Parent == menu.Name;
                    bool zeroMatch = m.Parent == "0" && (string.IsNullOrEmpty(menu.Parent) || menu.Name == "MAIN");
                    bool included = parentMatch || zeroMatch;
                    GlobalModules.DebugLog($"[DEBUG]   Item '{m.Name}' parent='{m.Parent}' hotkey='{m.Hotkey}': parentMatch={parentMatch}, zeroMatch={zeroMatch}, included={included}\n");
                }
            }
            var childItems = _menus.Values.Where(m => 
                m.Parent == menu.Name || 
                (m.Parent == "0" && (string.IsNullOrEmpty(menu.Parent) || menu.Name == "MAIN"))
            ).ToList();
            
            if (GlobalModules.DebugMode)
                GlobalModules.DebugLog($"[DEBUG] Found {childItems.Count} child items\n");
            
            // Display menu items
            foreach (var item in childItems)
            {
                // Display with value if available
                if (!string.IsNullOrEmpty(item.Value))
                {
                    // Replace carriage returns with * for display (TWX script convention)
                    string displayValue = item.Value.Replace("\r", "*");
                    server?.ClientMessage($"{item.Hotkey} - {item.Description,-25} {displayValue}\r\n");
                }
                else
                {
                    server?.ClientMessage($"{item.Hotkey} - {item.Description}\r\n");
                }
            }
            
            // Display standard menu options
            if (menu.OptionQ)
                server?.ClientMessage("Q - Terminate script\r\n");
            if (menu.OptionHelp)
                server?.ClientMessage("? - Command list\r\n");
            if (menu.OptionPlus)
                server?.ClientMessage("+ - Help on command\r\n");
            
            // Use prompt if available, otherwise use name
            string prompt = !string.IsNullOrEmpty(menu.Prompt) ? menu.Prompt : menu.Name;
            server?.ClientMessage($"\r\n{prompt}> ");
        }
        
        public bool HandleMenuInput(char keyChar)
        {
            if (_menuStack.Count == 0)
                return false;

            // Custom script menus are single-key hotkey driven. When a menu is first
            // opened from a typed command, the trailing LF from the user's Enter can
            // arrive after the script has already opened the menu. Pascal behavior is
            // to ignore that newline rather than moving the prompt to the next line.
            if (keyChar == '\r' || keyChar == '\n')
                return true;
                
            var currentMenu = _menuStack.Peek();

            static void EchoMenuKey(char key)
            {
                GlobalModules.TWXServer?.ClientMessage(char.ToUpperInvariant(key).ToString());
            }
            
            // Handle standard options
            char upperKey = char.ToUpper(keyChar);
            if (upperKey == 'Q')
            {
                EchoMenuKey(upperKey);

                // Q always goes back/closes menu (never passes to server)
                if (_menuStack.Count > 1)
                {
                    // In submenu - pop back to parent
                    _menuStack.Pop();
                    var parentMenu = _menuStack.Peek();
                    DisplayScriptMenu(parentMenu);
                }
                else
                {
                    // In the root menu, Pascal/TWX semantics are "Terminate script".
                    // The menu text already says "Q - Terminate script", so close the
                    // menu UI and stop the owning script rather than merely dismissing it.
                    var rootMenu = _menuStack.Peek();
                    CloseMenu(false);

                    if (rootMenu.Script is Script rootScript)
                    {
                        ClearSuspendedMenu();
                        GlobalModules.DebugLog($"[Menu] Root Q terminating script '{rootScript.ScriptName}'\n");
                        rootScript.Controller.StopByHandle(rootScript);
                    }
                }
                return true; // Always consume Q key
            }
            
            // Find matching menu item
            // Include items with parent matching current menu, or parent '0' (global items)
            var matchingItem = _menus.Values.FirstOrDefault(m => 
                (m.Parent == currentMenu.Name || (m.Parent == "0" && (string.IsNullOrEmpty(currentMenu.Parent) || currentMenu.Name == "MAIN"))) && 
                char.ToUpper(m.Hotkey) == upperKey);
            
            if (matchingItem != null)
            {
                // Execute menu item reference (GOSUB to label)
                if (matchingItem.Script is Script script)
                {
                    try
                    {
                        bool hasChildMenu = _menus.Values.Any(m =>
                            string.Equals(m.Parent, matchingItem.Name, StringComparison.OrdinalIgnoreCase));
                        EchoMenuKey(upperKey);

                        // Get the label reference and strip leading ':' if present
                        string labelName = matchingItem.Reference;
                        if (labelName.StartsWith(':'))
                            labelName = labelName.Substring(1);
                        
                        // If reference is empty, this is a submenu - open it
                        if (string.IsNullOrEmpty(labelName))
                        {
                            if (_menus.ContainsKey(matchingItem.Name))
                            {
                                OpenMenu(matchingItem.Name, 0);
                                return true;
                            }
                            GlobalModules.TWXServer?.ClientMessage($"\r\nMenu item '{matchingItem.Description}' has no submenu defined.\r\n");
                            return true;
                        }
                        
                        // Start each hotkey handler with a clean pause reason so
                        // Pascal-style submenu stubs like ":Menu_Nav -> pause" don't
                        // inherit a stale OpenMenu/Input reason from the previous menu action.
                        script.PausedReason = PauseReason.None;

                        // Unpause the script and GOSUB to the reference label
                        script.Paused = false;
                        int initialSubStackDepth = script.SubStackDepth; // Remember depth before GOSUB
                        // Close-menu items like "Start" should resume the script after OPENMENU
                        // instead of returning to EOF like config-only menu handlers.
                        script.GosubFromMenu(labelName, matchingItem.CloseMenu);
                        
                        // Close menu if configured
                        if (matchingItem.CloseMenu)
                        {
                            CloseMenu(false);
                        }
                        
                        // Call Execute() to actually run the GOSUBed code
                        // Execute until handler completes (RETURN) or pauses (GETINPUT)
                        GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Calling Execute() for handler '{labelName}', subStack depth before={initialSubStackDepth}...\n");
                        GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Hotkey '{upperKey}' matched menu item '{matchingItem.Name}'\n");
                        
                        bool handlerCompleted = false;
                        bool handlerMenuAlreadyDisplayed = false;
                        int maxIterations = 100; // Safety limit
                        int iterations = 0;
                        
                        while (!handlerCompleted && iterations < maxIterations)
                        {
                            GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Loop iteration {iterations + 1}, Paused={script.Paused}, WaitingForInput={script.WaitingForInput}, SubStackDepth={script.SubStackDepth}\n");
                            
                            bool scriptCompleted = script.Execute();
                            iterations++;
                            
                            GlobalModules.DebugLog($"[DEBUG HandleMenuInput] After Execute(): completed={scriptCompleted}, Paused={script.Paused}, WaitingForInput={script.WaitingForInput}, SubStackDepth={script.SubStackDepth}\n");
                            
                            // Check if handler completed (script reached end or HALT)
                            // Note: Don't check SubStackDepth == initialSubStackDepth because handlers can call GOSUB
                            // and still have more code to execute after the GOSUB returns (e.g., HALT)
                            if (scriptCompleted)
                            {
                                GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler COMPLETED (script ended or HALT)\n");
                                handlerCompleted = true;
                                break;
                            }
                            
                            // Check if paused for input (GETINPUT)
                            if (script.Paused && script.WaitingForInput)
                            {
                                GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler paused for GETINPUT\n");
                                if (!matchingItem.CloseMenu)
                                    SuspendMenuForInput();
                                break; // Exit - will resume later when input received
                            }
                            
                            // If paused but NOT waiting for input, it might be from OPENMENU - continue
                            if (script.Paused && !script.WaitingForInput)
                            {
                                if (script.PausedReason == PauseReason.OpenMenu)
                                {
                                    GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler ended via OPENMENU\n");
                                    handlerCompleted = true;
                                    handlerMenuAlreadyDisplayed = true;
                                    break;
                                }
                                else if (script.PausedReason == PauseReason.Command && hasChildMenu)
                                {
                                    script.CompletePendingNonResumingMenuHandler();
                                    GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Paused for Command on submenu '{matchingItem.Name}' - opening child menu\n");
                                    OpenMenu(matchingItem.Name, 0);
                                    return true;
                                }
                                else
                                {
                                    GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Paused for {script.PausedReason} - NOT unpausing, exiting handler loop\n");
                                    break; // Exit handler loop - script is paused for a reason
                                }
                            }
                        }
                        
                        GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Execute() finished after {iterations} iterations, handlerCompleted={handlerCompleted}, script.Paused={script.Paused}, SubStackDepth={script.SubStackDepth}\n");
                        
                        // After execution, redisplay menu if handler completed
                        if (!matchingItem.CloseMenu && _menuStack.Count > 0)
                        {
                            if (handlerCompleted)
                            {
                                // Handler completed - check if it was GETINPUT or just normal completion
                                if (script.WaitingForInput)
                                {
                                    GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler waiting for GETINPUT, will resume later\n");
                                }
                                else
                                {
                                    GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler completed normally, redisplaying menu\n");
                                    // Handler completed with RETURN - ensure script is paused for menu and redisplay
                                    script.Paused = true;
                                    if (!handlerMenuAlreadyDisplayed)
                                    {
                                        var currentMenuAfter = _menuStack.Peek();
                                        DisplayScriptMenu(currentMenuAfter);
                                    }
                                }
                            }
                            else
                            {
                                GlobalModules.DebugLog($"[DEBUG HandleMenuInput] Handler not completed yet (iterations limit or other reason)\n");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GlobalModules.TWXServer?.ClientMessage($"\r\nMenu error: {ex.Message}\r\n");
                        
                        // Re-display the menu after error
                        DisplayScriptMenu(currentMenu);
                    }
                }
                return true;
            }
            
            return false;
        }
    }
}
