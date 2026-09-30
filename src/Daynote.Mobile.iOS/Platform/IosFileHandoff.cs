using Foundation;
using PhotosUI;
using UIKit;
using UniformTypeIdentifiers;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// Attachments across the app's edge on iOS: Quick Look to open one, and the photo library to pick
/// from, which the Files picker Avalonia uses does not show.
/// </summary>
internal static class IosFileHandoff
{
    /// <summary>Held while Quick Look is up; the controller is released by UIKit otherwise.</summary>
    private static UIDocumentInteractionController? _current;

    /// <summary>
    /// Shows an attachment in Quick Look, which previews images, PDFs, Office files and text, with
    /// the system's share button for everything beyond that. Falls back to the "Open in" menu for a
    /// type Quick Look cannot draw; false when neither is possible.
    /// </summary>
    public static async Task<bool> OpenFileAsync(string name, byte[] bytes)
    {
        string folder = Path.Combine(Path.GetTempPath(), "daynote-open");
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }

        string slot = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        string path = Path.Combine(slot, Path.GetFileName(name));
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);

        if (TopController() is not { } root)
        {
            return false;
        }

        var controller = UIDocumentInteractionController.FromUrl(NSUrl.FromFilename(path));
        controller.Name = name;
        controller.ViewControllerForPreview = _ => root;
        controller.DidEndPreview += (_, _) => _current = null;
        _current = controller;
        if (controller.PresentPreview(true))
        {
            return true;
        }

        if (root.View is { } view && controller.PresentOpenInMenu(view.Bounds, view, true))
        {
            return true;
        }

        _current = null;
        return false;
    }

    /// <summary>
    /// The system photo picker. It runs out of process and hands back only what was chosen, so it
    /// needs no photo-library permission and no usage string. Photos come back as JPEG (the
    /// compatible representation), which every device the file syncs to can show.
    /// </summary>
    public static Task<IReadOnlyList<string>> PickPhotosAsync(CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>();
        if (TopController() is not { } root)
        {
            result.SetResult([]);
            return result.Task;
        }

        var configuration = new PHPickerConfiguration
        {
            SelectionLimit = 0,
            Filter = PHPickerFilter.ImagesFilter,
            PreferredAssetRepresentationMode = PHPickerConfigurationAssetRepresentationMode.Compatible,
        };
        var picker = new PHPickerViewController(configuration) { Delegate = new PhotoDelegate(result) };
        root.PresentViewController(picker, true, null);
        cancellationToken.Register(() => result.TrySetResult([]));
        return result.Task;
    }

    private sealed class PhotoDelegate(TaskCompletionSource<IReadOnlyList<string>> result) : PHPickerViewControllerDelegate
    {
        public override async void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            picker.DismissViewController(true, null);
            var paths = new List<string>(results.Length);
            foreach (PHPickerResult item in results)
            {
                if (await CopyAsync(item.ItemProvider).ConfigureAwait(true) is { } path)
                {
                    paths.Add(path);
                }
            }

            result.TrySetResult(paths);
        }

        /// <summary>
        /// Copies one photo into the picker's staging folder. The file the provider lends is deleted
        /// as soon as its callback returns, so the copy happens inside it.
        /// </summary>
        private static Task<string?> CopyAsync(NSItemProvider provider)
        {
            var copied = new TaskCompletionSource<string?>();
            string type = UTTypes.Image.Identifier;
            if (!provider.HasItemConformingTo(type))
            {
                copied.SetResult(null);
                return copied.Task;
            }

            provider.LoadFileRepresentation(type, (url, error) =>
            {
                if (error is not null || url?.Path is not { } source)
                {
                    copied.TrySetResult(null);
                    return;
                }

                try
                {
                    string stem = string.IsNullOrWhiteSpace(provider.SuggestedName) ? "photo" : provider.SuggestedName;
                    string name = stem + Path.GetExtension(source).ToLowerInvariant();
                    string slot = Path.Combine(Path.GetTempPath(), "daynote-picker", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(slot);
                    string destination = Path.Combine(slot, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
                    File.Copy(source, destination);
                    copied.TrySetResult(destination);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    copied.TrySetResult(null);
                }
            });
            return copied.Task;
        }
    }

    /// <summary>The controller on top, which is what may present another.</summary>
    private static UIViewController? TopController()
    {
        UIViewController? controller = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
        {
            controller = presented;
        }

        return controller;
    }
}
