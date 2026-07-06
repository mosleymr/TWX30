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
    /// <summary>Adds path to recent list, persists prefs, rebuilds the Recent submenu.</summary>
    private void AddToRecentAndSave(string path)
    {
        if (IsGeneratedPlaceholderRecentPath(path))
            return;

        if (!_appPrefs.AddRecent(path))
            return;

        _appPrefs.Save();
        RebuildRecentMenu();
    }

    /// <summary>Rebuilds the items inside the Recent submenu from <see cref="_appPrefs"/>.</summary>
    private void RebuildRecentMenu(bool force = false)
    {
        if ((_recentMenuOpen || AreSharedMenusOpen) && !force)
        {
            _recentMenuRebuildPending = true;
            _viewClearRecents.IsEnabled = _appPrefs.RecentFiles.Count > 0;
            return;
        }

        _recentMenuRebuildPending = false;
        int removed = _appPrefs.RecentFiles.RemoveAll(path => IsGeneratedPlaceholderRecentPath(path));
        if (removed > 0)
            _appPrefs.Save();

        var items = new List<object>();
        foreach (var path in _appPrefs.RecentFiles)
        {
            var p    = path;  // capture
            var name = Path.GetFileNameWithoutExtension(p);
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileName(p);
            var item = new MenuItem { Header = EscapeMenuHeaderText(name) };
            ToolTip.SetTip(item, p);
            item.Click += (_, _) =>
            {
                var owner = ActiveMtcTab;
                _ = ExecuteInOptionalMtcTabSessionAsync(owner, () => OpenRecentAsync(p));
            };
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(none)", IsEnabled = false });

        _recentMenu.ItemsSource = items;
        _viewClearRecents.IsEnabled = _appPrefs.RecentFiles.Count > 0;
        RefreshNativeAppMenu();
    }

    private void OnRecentMenuOpened()
    {
        _recentMenuOpen = true;
        RebuildRecentMenu(force: true);
    }

    private void OnRecentMenuClosed()
    {
        _recentMenuOpen = false;
        QueueDeferredSharedMenuFlush();
    }

    private bool AreSharedMenusOpen => _openSharedMenus.Count > 0;

    private void TrackSharedMenuOpenState(MenuItem menuItem, Action? opened = null, Action? closed = null)
    {
        menuItem.PropertyChanged += (_, e) =>
        {
            if (e.Property != MenuItem.IsSubMenuOpenProperty)
                return;

            if (menuItem.IsSubMenuOpen)
            {
                _openSharedMenus.Add(menuItem);
                opened?.Invoke();
            }
            else
            {
                _openSharedMenus.Remove(menuItem);
                closed?.Invoke();
                QueueDeferredSharedMenuFlush();
            }
        };
    }

    private void QueueDeferredSharedMenuFlush()
    {
        Dispatcher.UIThread.Post(FlushDeferredSharedMenuRefreshes, DispatcherPriority.Background);
    }

    private void FlushDeferredSharedMenuRefreshes()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(FlushDeferredSharedMenuRefreshes, DispatcherPriority.Background);
            return;
        }

        if (AreSharedMenusOpen)
            return;

        if (_recentMenuRebuildPending)
            RebuildRecentMenu(force: true);

        if (_proxyMenuRebuildPending)
        {
            _proxyMenuRebuildPending = false;
            RebuildProxyMenu(force: true);
        }

        if (_scriptsMenuRebuildPending)
        {
            _scriptsMenuRebuildPending = false;
            RebuildScriptsMenu(force: true);
        }

        if (_aiMenuRebuildPending)
        {
            _aiMenuRebuildPending = false;
            RebuildAiMenu(force: true);
        }

        if (_nativeMenuRefreshPending)
        {
            _nativeMenuRefreshPending = false;
            RefreshNativeAppMenu(force: true);
            RefreshNativeDockMenu(force: true);
        }

        if (_tabStripRefreshPending)
        {
            _tabStripRefreshPending = false;
            RefreshMtcTabStrip(force: true);
        }

        if (_focusTerminalAfterSharedMenuClose)
        {
            _focusTerminalAfterSharedMenuClose = false;
            FocusActiveTerminal();
        }
    }

    private void RefreshNativeAppMenu(bool force = false)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostToCurrentMtcTabSession(() => RefreshNativeAppMenu(force), DispatcherPriority.Background);
            return;
        }

        if (!PrepareMtcTabVisualRefresh())
            return;

        if (AreSharedMenusOpen && !force)
        {
            _nativeMenuRefreshPending = true;
            return;
        }

        if (!_nativeAppMenuReady)
            return;

        _nativeAppMenu.Items.Clear();
        foreach (object? item in _menuBar.Items)
        {
            if (item is MenuItem menuItem && !menuItem.IsVisible)
                continue;

            NativeMenuItemBase? nativeItem = ConvertToNativeMenuItem(item);
            if (nativeItem != null)
                _nativeAppMenu.Add(nativeItem);
        }

        if (!_nativeAppMenuAttached)
        {
            NativeMenu.SetMenu(this, _nativeAppMenu);
            _nativeAppMenuAttached = true;
        }
    }

    private void RefreshNativeDockMenu(bool force = false)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostToCurrentMtcTabSession(() => RefreshNativeDockMenu(force), DispatcherPriority.Background);
            return;
        }

        if (!PrepareMtcTabVisualRefresh())
            return;

        if (AreSharedMenusOpen && !force)
        {
            _nativeMenuRefreshPending = true;
            return;
        }

        if (!_nativeAppMenuReady)
            return;

        _nativeDockMenu.Items.Clear();
        AddDockRoot(_scriptsMenu, "_Scripts");
        AddDockRoot(_proxyMenu, "_Proxy");
        AddDockRoot(_botMenu, "_Bot");
        AddDockRoot(_quickMenu, "_Quick");
        AddDockRoot(_toolsMenu, "_Tools");
        AddDockRoot(_aiMenu, "_Chat");

        if (!_nativeDockMenuAttached)
        {
            NativeDock.SetMenu(this, _nativeDockMenu);
            _nativeDockMenuAttached = true;
        }
    }

    private void AddDockRoot(MenuItem sourceMenu, string header)
    {
        if (!sourceMenu.IsVisible)
            return;

        var dockRoot = new MenuItem
        {
            Header = header,
            ItemsSource = sourceMenu.ItemsSource,
            IsEnabled = sourceMenu.IsEnabled,
            IsVisible = sourceMenu.IsVisible,
        };

        NativeMenuItemBase? nativeItem = ConvertToNativeMenuItem(dockRoot);
        if (nativeItem != null)
            _nativeDockMenu.Add(nativeItem);
    }

    private static NativeMenuItemBase? ConvertToNativeMenuItem(object? item)
    {
        if (item is Separator)
            return new NativeMenuItemSeparator();

        if (item is not MenuItem menuItem)
            return null;

        var nativeItem = new NativeMenuItem
        {
            Header = NormalizeNativeMenuHeader(menuItem.Header?.ToString()),
            IsEnabled = menuItem.IsEnabled,
            IsVisible = menuItem.IsVisible,
        };

        var children = GetMenuChildren(menuItem)
            .Select(ConvertToNativeMenuItem)
            .Where(child => child != null)
            .Cast<NativeMenuItemBase>()
            .ToList();

        if (children.Count > 0)
        {
            var submenu = new NativeMenu();
            foreach (NativeMenuItemBase child in children)
                submenu.Add(child);
            nativeItem.Menu = submenu;
        }
        else
        {
            nativeItem.Click += (_, _) =>
                menuItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        }

        return nativeItem;
    }

    private static IEnumerable<object?> GetMenuChildren(MenuItem menuItem)
    {
        if (menuItem.ItemsSource is IEnumerable source)
        {
            foreach (object? item in source)
                yield return item;
            yield break;
        }

        foreach (object? item in menuItem.Items)
            yield return item;
    }

    private static string NormalizeNativeMenuHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        var sb = new System.Text.StringBuilder(header.Length);
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != '_')
            {
                sb.Append(header[i]);
                continue;
            }

            if (i + 1 < header.Length && header[i + 1] == '_')
            {
                sb.Append('_');
                i++;
            }
        }

        return sb.ToString();
    }

    private static string EscapeMenuHeaderText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("_", "__");
    }

    /// <summary>Opens a recently used game config or database directly (no file picker, no connect).</summary>
    private async Task OpenRecentAsync(string path)
    {
        try
        {
            _menuBar.Close();
            if (!File.Exists(path))
            {
                await ShowMessageAsync("File Not Found",
                    $"The file\n{path}\nno longer exists.\n\nIt will be removed from the recent list.");
                _appPrefs.RecentFiles.Remove(path);
                _appPrefs.Save();
                RebuildRecentMenu();
                return;
            }

            await OpenPathAsync(path, addToRecent: true);
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog($"[MTC.OpenRecent] failed path='{path}': {ex}\n");
            Core.GlobalModules.FlushDebugLog();
            await ShowMessageAsync("Open Recent Failed", ex.Message);
        }
    }

}
