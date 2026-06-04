using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MTC;

public partial class MainWindow
{
    private const double NotesPanelWidth = 218;
    private readonly MenuItem _viewNotes = new() { Header = "_Notes", IsEnabled = false };
    private TextBox? _notesTextBox;
    private TextBlock? _notesHeaderText;
    private TextBlock? _notesStatusText;
    private DispatcherTimer? _notesSaveTimer;
    private bool _notesPanelVisible;
    private bool _notesLoading;
    private bool _notesDirty;
    private string? _notesGameName;
    private string? _notesFilePath;

    private bool ShouldShowNotesPanel()
        => _notesPanelVisible && TryResolveNotesGameName(out _);

    private bool TryResolveNotesGameName(out string gameName)
    {
        string candidate =
            !string.IsNullOrWhiteSpace(_embeddedGameName)
                ? _embeddedGameName!
                : !string.IsNullOrWhiteSpace(_embeddedGameConfig?.Name)
                    ? _embeddedGameConfig!.Name
                    : _state.GameName;

        gameName = NormalizeGameName(candidate);
        return !string.IsNullOrWhiteSpace(gameName);
    }

    private void ToggleNotesPanel()
    {
        if (!TryResolveNotesGameName(out _))
        {
            RefreshNotesMenuState();
            return;
        }

        if (_notesPanelVisible)
            SaveCurrentNotesNow();

        _notesPanelVisible = !_notesPanelVisible;
        _appPrefs.ShowNotesPanel = _notesPanelVisible;
        _appPrefs.Save();
        ApplySelectedSkinSafe();
        RefreshNotesMenuState();

        if (_notesPanelVisible)
            Dispatcher.UIThread.Post(() => _notesTextBox?.Focus(), DispatcherPriority.Input);
    }

    private void RefreshNotesMenuState()
    {
        bool hasGame = TryResolveNotesGameName(out _);
        _viewNotes.IsEnabled = hasGame;
        _viewNotes.Icon = hasGame && _notesPanelVisible
            ? new TextBlock { Text = "●", Foreground = HudAccentOk }
            : null;
    }

    private void UpdateNotesForActiveGame()
    {
        bool hasGame = TryResolveNotesGameName(out _);
        if (!hasGame)
        {
            SaveCurrentNotesNow();
            _notesGameName = null;
            _notesFilePath = null;
            _notesTextBox = null;
            RefreshNotesMenuState();
            return;
        }

        if (_notesPanelVisible && _notesTextBox == null)
        {
            ApplySelectedSkinSafe();
            RefreshNotesMenuState();
            return;
        }

        if (_notesPanelVisible && _notesTextBox != null)
            LoadNotesForActiveGame();

        RefreshNotesMenuState();
    }

    private Control BuildNotesPanel()
    {
        TextBox? previousTextBox = _notesTextBox;
        string? previousGameName = _notesGameName;
        string? previousFilePath = _notesFilePath;
        string previousText = previousTextBox?.Text ?? string.Empty;
        string previousStatus = _notesStatusText?.Text ?? string.Empty;
        bool previousDirty = _notesDirty;

        _notesHeaderText = new TextBlock
        {
            Foreground = HudAccent,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Text = "Notes",
        };

        _notesStatusText = new TextBlock
        {
            Foreground = HudMuted,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        };

        _notesTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            FontSize = 12,
            Background = Brushes.Black,
            Foreground = HudText,
            CaretBrush = HudAccent,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 0,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_notesTextBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_notesTextBox, ScrollBarVisibility.Disabled);
        _notesTextBox.TextChanged += (_, _) => QueueNotesSave();
        _notesTextBox.LostFocus += (_, _) => SaveCurrentNotesNow();

        var body = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(8) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        body.Children.Add(_notesHeaderText);
        Grid.SetRow(_notesTextBox, 2);
        body.Children.Add(_notesTextBox);
        Grid.SetRow(_notesStatusText, 3);
        body.Children.Add(_notesStatusText);

        var panel = new Border
        {
            Width = NotesPanelWidth,
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10),
            Child = body,
        };

        if (!TryRestoreRebuiltNotesPanel(previousGameName, previousFilePath, previousText, previousStatus, previousDirty))
            LoadNotesForActiveGame(forceReloadCurrentPath: true);

        return panel;
    }

    private bool TryRestoreRebuiltNotesPanel(
        string? previousGameName,
        string? previousFilePath,
        string previousText,
        string previousStatus,
        bool previousDirty)
    {
        if (_notesTextBox == null ||
            string.IsNullOrWhiteSpace(previousFilePath) ||
            !TryResolveNotesGameName(out string gameName))
        {
            return false;
        }

        string path = AppPaths.NotesPathForGame(gameName);
        if (!string.Equals(previousFilePath, path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previousGameName, gameName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _notesLoading = true;
        try
        {
            _notesGameName = gameName;
            _notesFilePath = path;
            _notesTextBox.Text = previousText;
            _notesDirty = previousDirty;
            if (_notesStatusText != null && !string.IsNullOrWhiteSpace(previousStatus))
                _notesStatusText.Text = previousStatus;
        }
        finally
        {
            _notesLoading = false;
            UpdateNotesHeader();
        }

        return true;
    }

    private void LoadNotesForActiveGame(bool forceReloadCurrentPath = false)
    {
        if (_notesTextBox == null || !TryResolveNotesGameName(out string gameName))
            return;

        string path = AppPaths.NotesPathForGame(gameName);
        if (!forceReloadCurrentPath &&
            string.Equals(_notesFilePath, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_notesGameName, gameName, StringComparison.OrdinalIgnoreCase))
        {
            UpdateNotesHeader();
            return;
        }

        SaveCurrentNotesNow();

        _notesLoading = true;
        try
        {
            _notesGameName = gameName;
            _notesFilePath = path;
            _notesTextBox.Text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            _notesDirty = false;
        }
        catch (Exception ex)
        {
            _notesTextBox.Text = string.Empty;
            _notesDirty = false;
            if (_notesStatusText != null)
                _notesStatusText.Text = $"Could not load notes: {ex.Message}";
        }
        finally
        {
            _notesLoading = false;
            UpdateNotesHeader();
        }
    }

    private void QueueNotesSave()
    {
        if (_notesLoading || _notesTextBox == null || string.IsNullOrWhiteSpace(_notesFilePath))
            return;

        _notesDirty = true;
        _notesSaveTimer ??= CreateNotesSaveTimer();
        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
        if (_notesStatusText != null)
            _notesStatusText.Text = "Unsaved changes...";
    }

    private DispatcherTimer CreateNotesSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveCurrentNotesNow();
        };
        return timer;
    }

    private void SaveCurrentNotesNow()
    {
        if (!_notesDirty || _notesTextBox == null || string.IsNullOrWhiteSpace(_notesFilePath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_notesFilePath)!);
            File.WriteAllText(_notesFilePath, _notesTextBox.Text ?? string.Empty);
            _notesDirty = false;
            if (_notesStatusText != null)
                _notesStatusText.Text = $"Saved to {Path.GetFileName(_notesFilePath)}";
        }
        catch (Exception ex)
        {
            if (_notesStatusText != null)
                _notesStatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void UpdateNotesHeader()
    {
        if (_notesHeaderText != null)
            _notesHeaderText.Text = string.IsNullOrWhiteSpace(_notesGameName)
                ? "Notes"
                : $"Notes: {_notesGameName}";

        if (_notesStatusText != null && !_notesDirty)
            _notesStatusText.Text = string.IsNullOrWhiteSpace(_notesFilePath)
                ? "Open a game to edit notes."
                : _notesFilePath;
    }
}
