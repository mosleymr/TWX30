using TWXProxy.Core;
using Microsoft.Maui.Controls;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;

namespace TWXP.Services
{
    /// <summary>
    /// MAUI implementation of IScriptWindow using in-app draggable panels rather
    /// than OS-level Application.OpenWindow(), which is unreliable on Mac Catalyst.
    /// </summary>
    public class MauiScriptWindow : IScriptWindow
    {
        private readonly string _name;
        private readonly string _title;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _onTop;
        private string _textContent;
        private bool _isVisible;

        public string Name => _name;
        public string Title => _title;
        public int Width => _width;
        public int Height => _height;
        public bool OnTop => _onTop;
        public bool IsVisible => _isVisible;

        public string TextContent
        {
            get => _textContent;
            set
            {
                _textContent = value;
                if (_isVisible)
                    GlobalModules.PanelOverlay?.UpdatePanel(_name, _textContent);
            }
        }

        public MauiScriptWindow(string name, string title, int width, int height, bool onTop = false)
        {
            _name = name;
            _title = title;
            _width = width;
            _height = height;
            _onTop = onTop;
            _textContent = string.Empty;
            _isVisible = false;
            GlobalModules.DebugLog($"[MauiScriptWindow] Created '{_name}' {_width}x{_height}\n");
        }

        public void Show()
        {
            _isVisible = true;
            GlobalModules.DebugLog($"[MauiScriptWindow] Show() '{_name}'\n");

            if (GlobalModules.PanelOverlay != null)
            {
                GlobalModules.PanelOverlay.AddPanel(_name, _title, _width, _height,
                    _ => { /* content set via UpdatePanel */ });
                // Push any content that was set before Show()
                if (!string.IsNullOrEmpty(_textContent))
                    GlobalModules.PanelOverlay.UpdatePanel(_name, _textContent);
            }
            else
            {
                GlobalModules.DebugLog($"[MauiScriptWindow] Show() '{_name}': PanelOverlay not registered yet\n");
            }
        }

        public void Hide()
        {
            if (_isVisible)
            {
                _isVisible = false;
                GlobalModules.DebugLog($"[MauiScriptWindow] Hide() '{_name}'\n");
                GlobalModules.PanelOverlay?.RemovePanel(_name);
            }
        }

        public void Dispose() => Hide();
    }

    /// <summary>
    /// Factory for creating MAUI script windows (panel-based).
    /// </summary>
    public class MauiScriptWindowFactory : IScriptWindowFactory
    {
        public IScriptWindow CreateWindow(string name, string title, int width, int height, bool onTop = false)
        {
            return new MauiScriptWindow(name, title, width, height, onTop);
        }
    }
}
