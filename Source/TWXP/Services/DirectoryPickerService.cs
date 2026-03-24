using Foundation;
using Microsoft.Maui.ApplicationModel;
using UniformTypeIdentifiers;
using UIKit;

namespace TWXP.Services;

public interface IDirectoryPickerService
{
    Task<string?> PickDirectoryAsync();
}

public class DirectoryPickerService : IDirectoryPickerService
{
    private DirectoryPickerDelegate? _activeDelegate;
    private UIDocumentPickerViewController? _activePicker;

    public Task<string?> PickDirectoryAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var host = GetTopViewController();
            if (host == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder })
            {
                AllowsMultipleSelection = false,
                ModalPresentationStyle = UIModalPresentationStyle.FormSheet
            };

            _activeDelegate = new DirectoryPickerDelegate(tcs, ClearActivePicker);
            _activePicker = picker;
            picker.Delegate = _activeDelegate;
            host.PresentViewController(picker, true, null);
        });

        return tcs.Task;
    }

    private void ClearActivePicker()
    {
        _activeDelegate = null;
        _activePicker = null;
    }

    private static UIViewController? GetTopViewController()
    {
        var keyWindow = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow);

        var controller = keyWindow?.RootViewController;
        while (controller?.PresentedViewController != null)
        {
            controller = controller.PresentedViewController;
        }

        return controller;
    }

    private sealed class DirectoryPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<string?> _tcs;
        private readonly Action _onDone;
        private bool _completed;

        public DirectoryPickerDelegate(TaskCompletionSource<string?> tcs, Action onDone)
        {
            _tcs = tcs;
            _onDone = onDone;
        }

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl url)
        {
            Complete(ExtractPath(url), controller);
        }

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            Complete(ExtractPath(urls.FirstOrDefault()), controller);
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
        {
            Complete(null, controller);
        }

        private void Complete(string? path, UIDocumentPickerViewController controller)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            System.Diagnostics.Debug.WriteLine($"[DirectoryPicker] Selected path: {path ?? "<null>"}");
            _tcs.TrySetResult(path);
            controller.DismissViewController(true, null);
            _onDone();
        }

        private static string? ExtractPath(NSUrl? url)
        {
            if (url == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(url.Path))
            {
                return url.Path;
            }

            var absolute = url.AbsoluteString;
            if (string.IsNullOrWhiteSpace(absolute))
            {
                return null;
            }

            if (Uri.TryCreate(absolute, UriKind.Absolute, out var uri))
            {
                return uri.IsFile ? uri.LocalPath : Uri.UnescapeDataString(uri.AbsolutePath);
            }

            return absolute;
        }
    }
}