using Microsoft.Maui.Controls;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using TWXProxy.Core;

namespace TWXP.Services
{
    /// <summary>
    /// A floating draggable panel that lives inside the main app window as an overlay.
    /// Replaces the failed OS-level Application.OpenWindow() approach on Mac Catalyst.
    /// </summary>
    public class ScriptWindowPanel : Grid
    {
        private readonly string _name;
        private readonly Label _contentLabel;
        private double _panelX;
        private double _panelY;
        // Captured at gesture start so Running deltas don't accumulate
        private double _dragStartX;
        private double _dragStartY;

        public string PanelName => _name;

        // Delegate called by MauiPanelOverlayService to let MauiScriptWindow store
        // a reference to the label-setter so UpdatePanel() works.
        public Action<string>? ContentSetter { get; set; }

        public ScriptWindowPanel(string name, string title, int width, int height)
        {
            _name = name;

            WidthRequest = width;
            HeightRequest = height;
            BackgroundColor = Color.FromArgb("#1a1a1a");
            // Panel itself must receive input even though the overlay it lives in is InputTransparent
            InputTransparent = false;

            // Thin border
            var outerBorder = new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#00aa00"),
                BackgroundColor = Color.FromArgb("#1a1a1a"),
                Padding = 0,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill,
            };
            outerBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.Rectangle();

            var innerGrid = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star }
                }
            };

            // Title bar — drag handle
            var titleBar = new Grid
            {
                BackgroundColor = Color.FromArgb("#003300"),
                Padding = new Thickness(6, 3),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var titleLabel = new Label
            {
                Text = title,
                TextColor = Color.FromArgb("#00dd00"),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            };

            var closeButton = new Button
            {
                Text = "✕",
                FontSize = 11,
                TextColor = Color.FromArgb("#00dd00"),
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(4, 0),
                WidthRequest = 22,
                HeightRequest = 22,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End,
                BorderWidth = 0
            };
            closeButton.Clicked += (s, e) => Hide();

            titleBar.Add(titleLabel, 0, 0);
            titleBar.Add(closeButton, 1, 0);

            // Drag gesture on the title bar
            var panGesture = new PanGestureRecognizer();
            panGesture.PanUpdated += OnTitleBarPanned;
            titleBar.GestureRecognizers.Add(panGesture);

            // Content area
            _contentLabel = new Label
            {
                FontFamily = "Courier New",
                FontSize = 11,
                TextColor = Color.FromArgb("#00cc00"),
                BackgroundColor = Color.FromArgb("#0a0a0a"),
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
                Padding = new Thickness(6),
                LineBreakMode = LineBreakMode.WordWrap
            };

            ContentSetter = text =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    _contentLabel.Text = text.Replace('*', '\n').Trim()
                );
            };

            var scrollView = new ScrollView
            {
                Content = _contentLabel,
                Orientation = ScrollOrientation.Vertical,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = Color.FromArgb("#0a0a0a")
            };

            innerGrid.Add(titleBar, 0, 0);
            innerGrid.Add(scrollView, 0, 1);

            outerBorder.Content = innerGrid;

            Add(outerBorder);

            IsVisible = false;
        }

        /// <summary>Set initial position within the overlay and make visible.</summary>
        public void PlaceAt(double x, double y)
        {
            _panelX = x;
            _panelY = y;
            AbsoluteLayout.SetLayoutBounds(this, new Rect(x, y, WidthRequest, HeightRequest));
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
            if (Parent is AbsoluteLayout overlay)
                overlay.Remove(this);
        }

        private void OnTitleBarPanned(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // Snapshot position at gesture start; Running gives cumulative TotalX/Y
                    _dragStartX = _panelX;
                    _dragStartY = _panelY;
                    break;

                case GestureStatus.Running:
                    // TotalX/Y is cumulative from gesture origin — do NOT += here
                    _panelX = _dragStartX + e.TotalX;
                    _panelY = _dragStartY + e.TotalY;
                    AbsoluteLayout.SetLayoutBounds(this, new Rect(_panelX, _panelY, WidthRequest, HeightRequest));
                    break;

                case GestureStatus.Completed:
                    // _panelX/_panelY already updated; nothing extra needed
                    break;
            }
        }
    }

    /// <summary>
    /// Manages the AbsoluteLayout overlay that hosts all script window panels.
    /// The main page registers this service; scripts create/update/remove panels via it.
    /// </summary>
    public class MauiPanelOverlayService : IPanelOverlayService
    {
        private readonly AbsoluteLayout _overlay;
        private readonly Dictionary<string, ScriptWindowPanel> _panels = new(StringComparer.OrdinalIgnoreCase);
        private static int _cascadeOffset = 0;

        public MauiPanelOverlayService(AbsoluteLayout overlay)
        {
            _overlay = overlay;
        }

        public void AddPanel(string name, string title, int width, int height, Action<string> contentSetter)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_panels.ContainsKey(name))
                {
                    GlobalModules.DebugLog($"[PanelOverlay] Panel '{name}' already exists, skipping\n");
                    return;
                }

                var panel = new ScriptWindowPanel(name, title, width, height);

                // Place panels in the right portion of the overlay so they don't cover
                // the main game content.  Use overlay.Width if already measured; otherwise
                // fall back to a fixed offset that works for typical Mac Catalyst sizes.
                int slot = _cascadeOffset % 5;
                double cascadeStep = slot * 30;
                double overlayW = _overlay.Width > 10 ? _overlay.Width : 1000.0;
                double startX = Math.Max(20, overlayW - width - 20 - cascadeStep);
                double startY = 20 + cascadeStep;
                _cascadeOffset++;

                _overlay.Add(panel);
                panel.PlaceAt(startX, startY);

                _panels[name] = panel;
                GlobalModules.DebugLog($"[PanelOverlay] Added panel '{name}' at ({startX},{startY}) size={width}x{height}\n");
            });
        }

        public void UpdatePanel(string name, string content)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_panels.TryGetValue(name, out var panel))
                {
                    panel.ContentSetter?.Invoke(content);
                    GlobalModules.DebugLog($"[PanelOverlay] Updated panel '{name}' ({content.Length} chars)\n");
                }
                else
                {
                    GlobalModules.DebugLog($"[PanelOverlay] UpdatePanel: panel '{name}' not found\n");
                }
            });
        }

        public void RemovePanel(string name)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_panels.TryGetValue(name, out var panel))
                {
                    _panels.Remove(name);
                    panel.Hide();
                    GlobalModules.DebugLog($"[PanelOverlay] Removed panel '{name}'\n");
                }
                else
                {
                    GlobalModules.DebugLog($"[PanelOverlay] RemovePanel: panel '{name}' not found\n");
                }
            });
        }
    }
}
