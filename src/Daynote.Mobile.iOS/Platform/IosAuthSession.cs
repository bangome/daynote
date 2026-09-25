using AuthenticationServices;
using Foundation;
using UIKit;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// Google sign-in on iOS, through <see cref="ASWebAuthenticationSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the only flow Apple and Google both accept. It runs Safari's own view — the user's
/// cookies, their password manager, their passkeys — in a sheet the app cannot read, and hands back
/// only the redirect. An embedded <c>WKWebView</c> would be refused by Google outright, because the
/// host app can read what is typed into one.
/// </para>
/// <para>
/// <c>PrefersEphemeralWebBrowserSession</c> is deliberately left off: sharing Safari's session is
/// the point, so someone already signed into Google is one tap from done. It also means iOS shows
/// the "wants to use google.com to sign in" consent sheet, which is expected and is Apple's, not
/// ours.
/// </para>
/// </remarks>
public sealed class IosAuthSession : NSObject, IASWebAuthenticationPresentationContextProviding
{
    /// <summary>Opens the sheet and waits for the redirect, or null when the user dismissed it.</summary>
    public Task<Uri?> StartAsync(string authorizationUrl, string callbackScheme, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Finish(NSUrl? callback, NSError? error)
        {
            if (error is not null || callback is null)
            {
                // Cancellation and every other failure read the same to the caller, which is right:
                // there is nothing the user can do differently about either.
                completion.TrySetResult(null);
                return;
            }

            completion.TrySetResult(new Uri(callback.AbsoluteString!));
        }

        // iOS 17.4 replaced the plain scheme string with a callback object, and marked the old
        // overload obsolete. Both are kept because the app supports iOS 15.
        ASWebAuthenticationSession session =
            OperatingSystem.IsIOSVersionAtLeast(17, 4)
                ? new ASWebAuthenticationSession(
                    new NSUrl(authorizationUrl),
                    ASWebAuthenticationSessionCallback.Create(callbackScheme),
                    Finish)
                : CreateLegacySession(authorizationUrl, callbackScheme, Finish);

        session.PresentationContextProvider = this;

        cancellationToken.Register(() =>
        {
            session.Cancel();
            completion.TrySetResult(null);
        });

        if (!session.Start())
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    /// <summary>The pre-17.4 constructor, isolated so the obsolete call has one suppressed site.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Interoperability",
        "CA1422:Validate platform compatibility",
        Justification = "The 17.4 overload is used above when it exists; this is the iOS 15/16 path.")]
    private static ASWebAuthenticationSession CreateLegacySession(
        string authorizationUrl, string callbackScheme, Action<NSUrl?, NSError?> finish) =>
        new(new NSUrl(authorizationUrl), callbackScheme, (callback, error) => finish(callback, error));

    /// <summary>
    /// The window the sheet is presented from. Avalonia keeps exactly one.
    /// </summary>
    /// <remarks>
    /// Found through the connected scenes rather than <c>UIApplication.KeyWindow</c>, which is
    /// obsolete because it cannot say which scene it means on a device showing two.
    /// </remarks>
    public UIWindow GetPresentationAnchor(ASWebAuthenticationSession session) =>
        UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)
        ?? throw new InvalidOperationException("Sign-in was started before the app had a window.");
}
