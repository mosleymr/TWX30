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
    private static readonly IBrush BgPanel    = new SolidColorBrush(Color.FromRgb(23,  25,  28));
    private static readonly IBrush BgSection  = new SolidColorBrush(Color.FromRgb(31,  34,  38));
    private static readonly IBrush BgInput    = new SolidColorBrush(Color.FromRgb(18,  20,  23));
    private static readonly IBrush BdInput    = new SolidColorBrush(Color.FromRgb(74,  81,  92));
    private static readonly IBrush BdSection  = new SolidColorBrush(Color.FromRgb(49,  55,  64));
    private static readonly IBrush Accent     = new SolidColorBrush(Color.FromRgb(104, 176, 196));
    private static readonly IBrush FgNormal   = new SolidColorBrush(Color.FromRgb(206, 211, 218));
    private static readonly IBrush FgLabel    = new SolidColorBrush(Color.FromRgb(236, 239, 243));
    private static readonly IBrush FgMuted    = new SolidColorBrush(Color.FromRgb(145, 153, 164));
    private static readonly IBrush BgButton   = new SolidColorBrush(Color.FromRgb(52,  58,  66));
    private static readonly IBrush BgPrimary  = new SolidColorBrush(Color.FromRgb(57,  117, 135));

    private static readonly MemoryLimitOption[] MemoryLimitOptions =
    {
        new("128 KB", 128),
        new("256 KB", 256),
        new("384 KB", 384),
        new("512 KB", 512),
        new("768 KB", 768),
        new("1 MB", 1024),
    };

    public PreferencesDialog(AppPreferences prefs)
    {
        Title                 = "Preferences";
        Width                 = 640;
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

        var txtProgramDir = BuildPathTextBox(defaultProgramDir, "path to TWX program directory");
        var txtScripts = BuildPathTextBox(defaultScriptsDir, "path to scripts folder");

        var btnBrowseProgramDir = BuildBrowseButton();
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

        var btnBrowse = BuildBrowseButton();
        btnBrowse.Click += async (_, _) =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            // Start in the current scripts dir (or home if it doesn't exist).
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

        var programDirRow = BuildPathInputRow(txtProgramDir, btnBrowseProgramDir);
        var scriptsRow = BuildPathInputRow(txtScripts, btnBrowse);

        var chkDebug = BuildCheckBox("Enable debug logging", prefs.DebugLoggingEnabled);
        var chkVerbose = BuildCheckBox("Enable verbose parameter debug logging", prefs.VerboseDebugLogging);
        var chkScriptTrace = BuildCheckBox("Enable script VM trace logging (huge)", prefs.ScriptTraceDebugLogging);
        var chkAutoRecorderDebug = BuildCheckBox("Enable AutoRecorder debug logging", prefs.AutoRecorderDebugLogging);
        var chkTriggerDebug = BuildCheckBox("Enable trigger debug logging (very noisy)", prefs.TriggerDebugLogging);
        var chkDebugPortHaggle = BuildCheckBox("Debug port haggle to mtc_haggle_debug.log", prefs.DebugPortHaggleEnabled);
        var chkDebugPlanetHaggle = BuildCheckBox("Debug planet haggle to mtc_neg_debug.log", prefs.DebugPlanetHaggleEnabled);
        var chkEnableRedAlertMode = BuildCheckBox("Enable Red Alert Mode", prefs.EnableRedAlertMode);
        var chkPreparedVm = BuildCheckBox("Use prepared VM", prefs.PreparedVmEnabled);
        var chkVmMetrics = BuildCheckBox("Log VM metrics", prefs.VmMetricsEnabled);

        var cboPreparedCacheLimit = BuildMemoryLimitComboBox(
            prefs.PreparedScriptCacheLimitKb,
            AppPreferences.DefaultPreparedScriptCacheLimitKb);
        var cboHotkeyPrewarmLimit = BuildMemoryLimitComboBox(
            prefs.MombotHotkeyPrewarmLimitKb,
            AppPreferences.DefaultMombotHotkeyPrewarmLimitKb);

        chkDebug.IsCheckedChanged += (_, _) =>
        {
            bool debugEnabled = chkDebug.IsChecked == true;
            chkVerbose.IsEnabled = debugEnabled;
            chkScriptTrace.IsEnabled = debugEnabled;
            chkAutoRecorderDebug.IsEnabled = debugEnabled;
            chkTriggerDebug.IsEnabled = debugEnabled;
            if (!debugEnabled)
            {
                chkVerbose.IsChecked = false;
                chkScriptTrace.IsChecked = false;
                chkAutoRecorderDebug.IsChecked = false;
                chkTriggerDebug.IsChecked = false;
            }
        };
        chkVerbose.IsEnabled = chkDebug.IsChecked == true;
        chkScriptTrace.IsEnabled = chkDebug.IsChecked == true;
        chkAutoRecorderDebug.IsEnabled = chkDebug.IsChecked == true;
        chkTriggerDebug.IsEnabled = chkDebug.IsChecked == true;

        var storageSection = BuildSection(
            "Storage",
            "Shared folders used by the desktop client and script runtime.",
            BuildField("Program directory", programDirRow, "Base TWX program data location."),
            BuildField("Scripts directory", scriptsRow, "Live script tree used for Mombot and custom scripts."));

        var diagnosticsSection = BuildSection(
            "Diagnostics",
            "Logging controls for runtime troubleshooting.",
            BuildCheckGroup(chkDebug, chkVerbose, chkScriptTrace, chkAutoRecorderDebug, chkTriggerDebug, chkDebugPortHaggle, chkDebugPlanetHaggle));

        var alertsSection = BuildSection(
            "Alerts",
            "Safety switches that change how aggressively MTC reacts.",
            BuildCheckGroup(chkEnableRedAlertMode));

        var runtimeSection = BuildSection(
            "Runtime",
            "Prepared script retention and Mombot hotkey prewarm limits.",
            BuildCheckGroup(chkPreparedVm, chkVmMetrics),
            BuildMemoryLimitRow("Prepared cache retention", cboPreparedCacheLimit),
            BuildMemoryLimitRow("Mombot hotkey prewarm cap", cboHotkeyPrewarmLimit));

        var btnSave = new Button
        {
            Content             = "Save",
            MinWidth            = 88,
            Background          = BgPrimary,
            Foreground          = FgLabel,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 0, 8, 0),
        };

        var btnCancel = new Button
        {
            Content    = "Cancel",
            MinWidth   = 88,
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
            prefs.ScriptTraceDebugLogging = prefs.DebugLoggingEnabled && chkScriptTrace.IsChecked == true;
            prefs.AutoRecorderDebugLogging = prefs.DebugLoggingEnabled && chkAutoRecorderDebug.IsChecked == true;
            prefs.TriggerDebugLogging = prefs.DebugLoggingEnabled && chkTriggerDebug.IsChecked == true;
            prefs.DebugPortHaggleEnabled = chkDebugPortHaggle.IsChecked == true;
            prefs.DebugPlanetHaggleEnabled = chkDebugPlanetHaggle.IsChecked == true;
            prefs.EnableRedAlertMode = chkEnableRedAlertMode.IsChecked == true;
            prefs.PreparedVmEnabled = chkPreparedVm.IsChecked == true;
            prefs.VmMetricsEnabled = chkVmMetrics.IsChecked == true;
            prefs.PreparedScriptCacheLimitKb = GetMemoryLimitKb(
                cboPreparedCacheLimit,
                AppPreferences.DefaultPreparedScriptCacheLimitKb);
            prefs.MombotHotkeyPrewarmLimitKb = GetMemoryLimitKb(
                cboHotkeyPrewarmLimit,
                AppPreferences.DefaultMombotHotkeyPrewarmLimitKb);
            prefs.Save();
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 4, 0, 0),
            Children            = { btnSave, btnCancel },
        };

        Content = new StackPanel
        {
            Margin   = new Thickness(18),
            Spacing  = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "MTC Preferences",
                    Foreground = FgLabel,
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Tune paths, diagnostics, and runtime cache behavior.",
                    Foreground = FgMuted,
                    Margin = new Thickness(0, -8, 0, 2),
                },
                storageSection,
                diagnosticsSection,
                alertsSection,
                runtimeSection,
                buttons,
            },
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static TextBox BuildPathTextBox(string value, string watermark)
    {
        return new TextBox
        {
            Text                = value,
            Watermark           = watermark,
            Background          = BgInput,
            Foreground          = FgNormal,
            BorderBrush         = BdInput,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static Button BuildBrowseButton()
    {
        return new Button
        {
            Content    = "Browse…",
            Background = BgButton,
            Foreground = FgNormal,
            Margin     = new Thickness(8, 0, 0, 0),
        };
    }

    private static Grid BuildPathInputRow(TextBox input, Button browseButton)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(input, 0);
        Grid.SetColumn(browseButton, 1);
        row.Children.Add(input);
        row.Children.Add(browseButton);
        return row;
    }

    private static CheckBox BuildCheckBox(string label, bool isChecked)
    {
        return new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
            Foreground = FgNormal,
        };
    }

    private static StackPanel BuildCheckGroup(params CheckBox[] checkBoxes)
    {
        var group = new StackPanel
        {
            Spacing = 4,
        };

        foreach (var checkBox in checkBoxes)
            group.Children.Add(checkBox);

        return group;
    }

    private static Border BuildSection(string title, string description, params Control[] children)
    {
        var body = new StackPanel
        {
            Spacing = 10,
        };

        body.Children.Add(new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(3) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children =
            {
                new Border
                {
                    Background = Accent,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 2, 10, 0),
                },
                BuildSectionHeader(title, description),
            },
        });

        foreach (var child in children)
            body.Children.Add(child);

        return new Border
        {
            Background = BgSection,
            BorderBrush = BdSection,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = body,
        };
    }

    private static StackPanel BuildSectionHeader(string title, string description)
    {
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = FgLabel,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = description,
                    Foreground = FgMuted,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        Grid.SetColumn(header, 1);
        return header;
    }

    private static StackPanel BuildField(string label, Control input, string help)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = FgNormal,
                    FontWeight = FontWeight.SemiBold,
                },
                input,
                new TextBlock
                {
                    Text = help,
                    Foreground = FgMuted,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }

    private static ComboBox BuildMemoryLimitComboBox(int selectedKb, int defaultKb)
    {
        var combo = new ComboBox
        {
            ItemsSource          = MemoryLimitOptions,
            Background           = BgInput,
            Foreground           = FgNormal,
            BorderBrush          = BdInput,
            Width                = 120,
            HorizontalAlignment  = HorizontalAlignment.Right,
        };

        combo.SelectedItem = FindMemoryLimitOption(selectedKb)
            ?? FindMemoryLimitOption(defaultKb)
            ?? MemoryLimitOptions[0];

        return combo;
    }

    private static Grid BuildMemoryLimitRow(string label, ComboBox input)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = FgNormal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(input, 1);
        row.Children.Add(lbl);
        row.Children.Add(input);
        return row;
    }

    private static int GetMemoryLimitKb(ComboBox combo, int defaultValue)
    {
        return combo.SelectedItem is MemoryLimitOption option
            ? option.Kilobytes
            : defaultValue;
    }

    private static MemoryLimitOption? FindMemoryLimitOption(int kilobytes)
    {
        foreach (var option in MemoryLimitOptions)
        {
            if (option.Kilobytes == kilobytes)
                return option;
        }

        return null;
    }

    private sealed class MemoryLimitOption
    {
        public MemoryLimitOption(string label, int kilobytes)
        {
            Label = label;
            Kilobytes = kilobytes;
        }

        public string Label { get; }
        public int Kilobytes { get; }

        public override string ToString() => Label;
    }
}
