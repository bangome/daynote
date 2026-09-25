using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;
using Daynote.Infrastructure.Sync;

namespace Daynote.Mobile.Platform;

/// <summary>
/// Everything the shared mobile app needs from the OS but cannot reach itself, supplied by the head
/// project (Android or iOS) at startup.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole seam between <c>Daynote.Mobile</c> and the two heads. The shared project targets
/// plain <c>net10.0</c> so it can never touch Java or UIKit; the head targets
/// <c>net10.0-android</c> / <c>net10.0-ios</c> and can. Anything platform-shaped arrives here rather
/// than through a compile-time <c>#if</c>, which is also what makes the shared UI testable headless.
/// </para>
/// <para>
/// The desktop equivalent is the pile of <c>OperatingSystem.IsMacOS()</c> branches in
/// <c>DesktopServiceRegistration</c>. That works when one executable runs on every desktop OS; it
/// does not here, because the Android types simply do not exist in the iOS build.
/// </para>
/// </remarks>
/// <param name="DataRoot">
/// The app's private directory. Sandboxed on both platforms, so unlike the desktop there is no
/// user-visible location and no override.
/// </param>
/// <param name="SecretProtector">
/// Seals the sync session: the iOS Keychain, or the Android KeyStore. Null means this build has no
/// sealed store yet and the app behaves as permanently signed out rather than writing a token in the
/// clear.
/// </param>
/// <param name="Identity">
/// Google sign-in for this platform: an in-app browser tab (Custom Tabs / ASWebAuthenticationSession)
/// returning to a custom scheme. Null disables sign-in.
/// </param>
/// <param name="OpenExternal">Opens a URL in the system browser, for the terms and privacy links.</param>
/// <param name="TopLevel">
/// Resolves the window that owns pickers and the clipboard. A function rather than a value because
/// on Android the activity is recreated on rotation and the old top level is then detached.
/// </param>
public sealed record MobilePlatformServices(
    string DataRoot,
    ISecretProtector? SecretProtector,
    Daynote.Core.Sync.IIdentityProvider? Identity,
    Action<string> OpenExternal,
    Func<Avalonia.Controls.TopLevel?> TopLevel)
{
    /// <summary>
    /// The phone has no login item, no global hotkey, no MCP registration and no updater; the store
    /// updates the app. These are the do-nothing implementations the shared view models still expect.
    /// </summary>
    public static IStartupTaskGateway StartupGateway { get; } = new NoStartupTaskGateway();

    private sealed class NoStartupTaskGateway : IStartupTaskGateway
    {
        public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(StartupTaskState.Unavailable);

        public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(StartupTaskState.Unavailable);

        public ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(StartupTaskState.Unavailable);
    }
}
