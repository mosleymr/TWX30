using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Core = TWXProxy.Core;

namespace MTC;

/// <summary>
/// Application-wide preferences dialog.
/// Usage: <c>var saved = await new PreferencesDialog(prefs).ShowDialog&lt;bool&gt;(owner);</c>
/// The caller's <see cref="AppPreferences"/> instance is updated in-place when the user
/// clicks Save, and the dialog returns <c>true</c>.
/// </summary>
public class PreferencesDialog : Window
{
    // ── Colors (match MainWindow dark chrome) ─────────────────────────────
    private static readonly IBrush BgPanel  = new SolidColorBrush(Color.FromRgb(30,  30,  30));
    private static readonly IBrush BgInput  = new SolidColorBrush(Color.FromRgb(20,  20,  20));
    private static readonly IBrush BdInput  = new SolidColorBrush(Color.FromRgb(70,  70,  70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel  = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55,  55,  55));

    public PreferencesDialog(AppPreferences prefs)
    {
        Title                 = "Preferences";
        Width                 = 520;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = BgPanel;

        string defaultProgramDir = string.IsNullOrWhiteSpace(prefs.ProgramDirectory)
            ? Core.SharedPaths.GetDefaultProgramDir()
            : prefs.ProgramDirectory;
        string defaultScriptsDir = string.IsNullOrWhiteSpace(prefs.ScriptsDirectory)
            ? Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(defaultProgramDir)
            : prefs.ScriptsDirectory;

        // ── Program Directory ───────────────────────────────────────────
        var txtProgramDir = new TextBox
        {
            Text                = defaultProgramDir,
            Watermark           = "path to TWX program directory",
            Background          = BgInput,
            Foreground          = FgNormal,
            BorderBrush         = BdInput,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // ── Scripts Directory ────────────────────────────────────────────
        var txtScripts = new TextBox
        {
            Text                = defaultScriptsDir,
            Watermark           = "path to scripts folder",
            Background          = BgInput,
            Foreground          = FgNormal,
            BorderBrush         = BdInput,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var btnBrowseProgramDir = new Button
        {
            Content    = "Browse…",
            Background = BgButton,
            Foreground = FgNormal,
            Margin     = new Thickness(6, 0, 0, 0),
        };

        btnBrowseProgramDir.Click += async (_, _) =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            string currentProgramDir = Directory.Exists(txtProgramDir.Text)
                ? txtProgramDir.Text!
                : Core.SharedPaths.GetDefaultProgramDir();
            string previousDefaultScripts = Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(currentProgramDir);

            var startFolder = await storage.TryGetFolderFromPathAsync(currentProgramDir);
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title                  = "Select TWX Program Directory",
                SuggestedStartLocation = startFolder,
                AllowMultiple          = false,
            });

            if (folders.Count == 0)
                return;

            string selectedProgramDir = folders[0].Path.LocalPath;
            txtProgramDir.Text = selectedProgramDir;

            if (string.IsNullOrWhiteSpace(txtScripts.Text) ||
                string.Equals(txtScripts.Text, previousDefaultScripts, StringComparison.OrdinalIgnoreCase))
            {
                txtScripts.Text = Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(selectedProgramDir);
            }
        };

        var btnBrowse = new Button
        {
            Content    = "Browse…",
            Background = BgButton,
            Foreground = FgNormal,
            Margin     = new Thickness(6, 0, 0, 0),
        };

        btnBrowse.Click += async (_, _) =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            // Start in the current scripts dir (or home if it doesn't exist)
            var startPath = Directory.Exists(txtScripts.Text)
                ? txtScripts.Text!
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var startFolder = await storage.TryGetFolderFromPathAsync(startPath);

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title                  = "Select Scripts Directory",
                SuggestedStartLocation = startFolder,
                AllowMultiple          = false,
            });

            if (folders.Count > 0)
                txtScripts.Text = folders[0].Path.LocalPath;
        };

        // Row: text box + Browse button in a Grid
        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(txtScripts, 0);
        Grid.SetColumn(btnBrowse,  1);
        inputRow.Children.Add(txtScripts);
        inputRow.Children.Add(btnBrowse);

        var programDirRow = new Grid();
        programDirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        programDirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(txtProgramDir, 0);
        Grid.SetColumn(btnBrowseProgramDir, 1);
        programDirRow.Children.Add(txtProgramDir);
        programDirRow.Children.Add(btnBrowseProgramDir);

        var programDirectoryRow = BuildRow("Program Directory:", programDirRow);
        var scriptsRow = BuildRow("Scripts Directory:", inputRow);

        var chkDebug = new CheckBox
        {
            Content = "Enable debug logging",
            IsChecked = prefs.DebugLoggingEnabled,
            Foreground = FgNormal,
        };

        var chkVerbose = new CheckBox
        {
            Content = "Enable verbose debug logging",
            IsChecked = prefs.VerboseDebugLogging,
            Foreground = FgNormal,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var chkDebugPortHaggle = new CheckBox
        {
            Content = "Debug port haggle to mtc_haggle_debug.log",
            IsChecked = prefs.DebugPortHaggleEnabled,
            Foreground = FgNormal,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var chkDebugPlanetHaggle = new CheckBox
        {
            Content = "Debug planet haggle to mtc_neg_debug.log",
            IsChecked = prefs.DebugPlanetHaggleEnabled,
            Foreground = FgNormal,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var chkPreparedVm = new CheckBox
        {
            Content = "Use prepared VM",
            IsChecked = prefs.PreparedVmEnabled,
            Foreground = FgNormal,
        };

        var chkVmMetrics = new CheckBox
        {
            Content = "Log VM metrics",
            IsChecked = prefs.VmMetricsEnabled,
            Foreground = FgNormal,
            Margin = new Thickness(0, 4, 0, 0),
        };

        chkDebug.IsCheckedChanged += (_, _) =>
        {
            bool debugEnabled = chkDebug.IsChecked == true;
            chkVerbose.IsEnabled = debugEnabled;
            if (!debugEnabled)
                chkVerbose.IsChecked = false;
        };
        chkVerbose.IsEnabled = chkDebug.IsChecked == true;

        var debugRow = BuildRow("Debug Logging:", new StackPanel
        {
            Spacing = 2,
            Children = { chkDebug, chkVerbose, chkDebugPortHaggle, chkDebugPlanetHaggle },
        });

        var vmRow = BuildRow("Virtual Machine:", new StackPanel
        {
            Spacing = 2,
            Children = { chkPreparedVm, chkVmMetrics },
        });

        // ── OK / Cancel ──────────────────────────────────────────────────
        var btnSave = new Button
        {
            Content             = "Save",
            MinWidth            = 80,
            Background          = BgButton,
            Foreground          = FgNormal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 0, 8, 0),
        };

        var btnCancel = new Button
        {
            Content    = "Cancel",
            MinWidth   = 80,
            Background = BgButton,
            Foreground = FgNormal,
        };

        btnSave.Click += (_, _) =>
        {
            prefs.ProgramDirectory = txtProgramDir.Text?.Trim()
                ?? Core.SharedPaths.GetDefaultProgramDir();
            prefs.ScriptsDirectory = string.IsNullOrWhiteSpace(txtScripts.Text)
                ? Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(prefs.ProgramDirectory)
                : txtScripts.Text.Trim();
            prefs.DebugLoggingEnabled = chkDebug.IsChecked == true;
            prefs.VerboseDebugLogging = prefs.DebugLoggingEnabled && chkVerbose.IsChecked == true;
            prefs.DebugPortHaggleEnabled = chkDebugPortHaggle.IsChecked == true;
            prefs.DebugPlanetHaggleEnabled = chkDebugPlanetHaggle.IsChecked == true;
            prefs.PreparedVmEnabled = chkPreparedVm.IsChecked == true;
            prefs.VmMetricsEnabled = chkVmMetrics.IsChecked == true;
            prefs.Save();
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 12, 0, 0),
            Children            = { btnSave, btnCancel },
        };

        // ── Layout ───────────────────────────────────────────────────────
        Content = new StackPanel
        {
            Margin   = new Thickness(16),
            Spacing  = 4,
            Children = { programDirectoryRow, scriptsRow, debugRow, vmRow, buttons },
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static StackPanel BuildRow(string label, Control input)
    {
        var lbl = new TextBlock
        {
            Text       = label,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin     = new Thickness(0, 0, 0, 3),
        };
        return new StackPanel
        {
            Spacing  = 2,
            Margin   = new Thickness(0, 4, 0, 4),
            Children = { lbl, input },
        };
    }
}
