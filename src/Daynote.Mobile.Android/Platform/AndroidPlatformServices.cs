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
    public const string GoogleAndroidClientId = "";

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
            TopLevel: () => TopLevel.GetTopLevel((currentActivity() as AvaloniaMainActivity)?.Content as Control));
    }

    /// <summary>
    /// The app's private files directory: <c>/data/data/cc.arachat.daynote/files/Daynote</c>.
    /// </summary>
    /// <remarks>
    /// Not the cache directory and not external storage. This one survives a reboot, is covered by
    /// Android's auto-backup, is wiped on uninstall, and no other app can read it - the same
    /// contract the desktop data root has, so the shared infrastructure needs no special case.
    /// </remarks>
    private static string ResolveDataRoot(Context context)
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
                (url, scheme, token) => currentActivity() is { } activity
                    ? AndroidAuthSession.StartAsync(activity, url, scheme, token)
                    : Task.FromResult<Uri?>(null));

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
