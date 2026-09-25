using Avalonia.Controls;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Platform;
using Foundation;
using UIKit;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>Fills in <see cref="MobilePlatformServices"/> with the iOS implementations.</summary>
public static class IosPlatformServices
{
    /// <summary>
    /// The Google OAuth client of type iOS for this build.
    /// </summary>
    /// <remarks>
    /// Empty until that client exists in the Google console, and sign-in is simply absent while it
    /// is: <c>MobileSyncRegistration</c> registers no account at all without an identity provider,
    /// so the app runs local-only rather than showing a button that cannot work. The console steps,
    /// and the Worker change that has to accompany them, are in docs/MOBILE_PORT.md.
    /// </remarks>
    public const string GoogleIosClientId = "";

    /// <summary>The scheme declared in Info.plist under CFBundleURLTypes. The two must agree.</summary>
    private const string CallbackScheme = "cc.arachat.daynote";

    public static MobilePlatformServices Create() =>
        new(
            DataRoot: ResolveDataRoot(),
            SecretProtector: new MacKeychainSecretProtector(),
            Identity: CreateIdentity(),
            OpenExternal: OpenExternal,
            TopLevel: ResolveTopLevel);

    /// <summary>
    /// <c>Library/Daynote</c> inside the app container.
    /// </summary>
    /// <remarks>
    /// Library rather than Documents or Caches, and each for a reason. Documents is exposed to the
    /// Files app, and a user who deleted daynote.db there would lose every note. Caches is evicted
    /// by iOS under storage pressure, which would do the same thing without asking. Library is
    /// private, persistent, and included in the iCloud/iTunes backup.
    /// </remarks>
    private static string ResolveDataRoot()
    {
        string library = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        string root = Path.Combine(library, "Daynote");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// The Keychain protector is the macOS one, unchanged.
    /// </summary>
    /// <remarks>
    /// iOS and macOS share Security.framework and CoreFoundation at the same paths, and a generic
    /// password item behaves the same way on both. On iOS the item is scoped to the app by the
    /// system rather than by an access group, so nothing else can read it, and it is destroyed with
    /// the app on uninstall.
    /// </remarks>
    private static Daynote.Core.Sync.IIdentityProvider? CreateIdentity() =>
        GoogleIosClientId.Length == 0
            ? null
            : new MobileGoogleIdentityProvider(
                GoogleIosClientId,
                $"{CallbackScheme}:/oauth2redirect",
                (url, scheme, token) => new IosAuthSession().StartAsync(url, scheme, token));

    private static void OpenExternal(string target)
    {
        if (NSUrl.FromString(target) is { } url)
        {
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
        }
    }

    private static TopLevel? ResolveTopLevel() =>
        Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime { MainView: { } view }
                ? TopLevel.GetTopLevel(view)
                : null;
}
