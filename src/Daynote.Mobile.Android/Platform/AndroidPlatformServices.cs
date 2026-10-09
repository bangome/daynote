using Android.App;
using Android.Content;
using Avalonia.Android;
using Avalonia.Controls;
using Daynote.Mobile.Platform;

namespace Daynote.Mobile.Android.Platform;

/// <summary>Fills in <see cref="MobilePlatformServices"/> with the Android implementations.</summary>
public static class AndroidPlatformServices
{
    /// <summary>
    /// The Google OAuth client of type Android for this build.
    /// </summary>
    /// <remarks>
    /// Empty until that client exists in the Google console, and sign-in is simply absent while it
    /// is: <c>MobileSyncRegistration</c> registers no account at all without an identity provider,
    /// so the app runs local-only rather than showing a button that cannot work. The console steps,
    /// and the Worker change that has to accompany them, are in docs/MOBILE_PORT.md.
    /// </remarks>
    public const string GoogleAndroidClientId = "298036592294-n5uff4qakgibac545udo1jthfqonvku2.apps.googleusercontent.com";

    /// <param name="context">The application context, which outlives every activity.</param>
    /// <param name="currentActivity">
    /// Resolves the activity on screen. A function because Android may destroy and recreate it, and
    /// a captured reference would leak the old one and hand Custom Tabs a dead context.
    /// </param>
    public static MobilePlatformServices Create(Context context, Func<Activity?> currentActivity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentActivity);

        return new MobilePlatformServices(
            DataRoot: ResolveDataRoot(context),
            SecretProtector: new AndroidKeyStoreSecretProtector(),
            Identity: CreateIdentity(currentActivity),
            OpenExternal: target => OpenExternal(context, target),
            TopLevel: () => TopLevel.GetTopLevel((currentActivity() as AvaloniaMainActivity)?.Content as Control),
            OpenFile: (name, bytes) => OpenFileAsync(context, currentActivity, name, bytes),
            Reminders: new AndroidReminderScheduler(context, currentActivity),
            AgendaChanged: () => Widgets.DaynoteWidgets.RequestUpdate(context),
            Motion: new AndroidMotionPlatform(context, currentActivity),
            Device: DeviceShape);
    }

    /// <summary>The one hinge-and-keyboard source, which the activity attaches to its window.</summary>
    internal static AndroidDeviceShape DeviceShape { get; } = new();

    /// <summary>
    /// The app's private files directory: <c>/data/data/cc.arachat.daynote/files/Daynote</c>.
    /// </summary>
    /// <remarks>
    /// Not the cache directory and not external storage. This one survives a reboot, is covered by
    /// Android's auto-backup, is wiped on uninstall, and no other app can read it - the same
    /// contract the desktop data root has, so the shared infrastructure needs no special case.
    /// </remarks>
    internal static string ResolveDataRoot(Context context)
    {
        string root = Path.Combine(
            context.FilesDir?.AbsolutePath
                ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Daynote");
        Directory.CreateDirectory(root);
        return root;
    }

    private static Daynote.Core.Sync.IIdentityProvider? CreateIdentity(Func<Activity?> currentActivity) =>
        GoogleAndroidClientId.Length == 0
            ? null
            : new MobileGoogleIdentityProvider(
                GoogleAndroidClientId,
                $"{AuthCallbackActivity.Scheme}:/oauth2redirect",
                Daynote.Core.Sync.OAuthClientKind.Android,
                (url, scheme, token) => currentActivity() is { } activity
                    ? AndroidAuthSession.StartAsync(activity, url, scheme, token)
                    : Task.FromResult<Uri?>(null));

    /// <summary>
    /// The authority of the app's <c>FileProvider</c>, declared in AndroidManifest.xml. Only the
    /// folder in <c>Resources/xml/daynote_file_paths.xml</c> is served through it.
    /// </summary>
    internal const string FileProviderAuthority = "cc.arachat.daynote.files";

    /// <summary>
    /// Shows an attachment in whichever app views its type, through a content URI with read access
    /// granted to that one app. A <c>file://</c> URI would throw FileUriExposedException on every
    /// Android since 7, and the store's own copy sits under a hashed name in private storage, so
    /// the bytes are written under their real name into the cache folder the provider serves.
    /// </summary>
    /// <returns>False when no installed app views the type; the phone then offers a copy to save.</returns>
    private static async Task<bool> OpenFileAsync(Context context, Func<Activity?> currentActivity, string name, byte[] bytes)
    {
        string folder = Path.Combine(context.CacheDir!.AbsolutePath, "daynote-open");
        // Yesterday's hand-offs are done with; the viewing app had its read long ago.
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

        using var file = new Java.IO.File(path);
        global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, FileProviderAuthority, file)!;
        string extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        string mime = global::Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream";

        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, mime);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        try
        {
            if (currentActivity() is { } activity)
            {
                activity.StartActivity(intent);
            }
            else
            {
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }

            return true;
        }
        catch (ActivityNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Opens the terms and privacy links in the browser. A failure is not worth a dialog.</summary>
    private static void OpenExternal(Context context, string target)
    {
        try
        {
            using var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(target));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
        }
    }
}
