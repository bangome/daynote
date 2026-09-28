using AuthenticationServices;
using Daynote.Core.Sync;
using Foundation;
using UIKit;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// Sign in with Apple, through the system sheet (<see cref="ASAuthorizationController"/>).
/// </summary>
/// <remarks>
/// <para>
/// App Review guideline 4.8: an app that offers a third-party sign-in (Google) must offer this too.
/// The native sheet is used rather than Apple's web flow because it is what users recognise, it
/// supports Face ID and "Hide My Email", and it needs no redirect scheme.
/// </para>
/// <para>
/// Only the one-time authorization code leaves the device. The Worker redeems it with Apple
/// directly and trusts the ID token that comes back over that connection, exactly as it does for
/// Google; the identity token the sheet also returns is ignored here. The nonce ties that token to
/// this attempt.
/// </para>
/// </remarks>
public sealed class IosAppleIdentityProvider : IAppleIdentityProvider
{
    /// <summary>
    /// The attempt whose sheet is up. Held here because nothing else holds it: the controller keeps
    /// only weak references to its delegate and presentation provider, and the task the caller awaits
    /// does not point back at the attempt. Without this field a collection while the sheet is open
    /// would take the delegate with it, the answer would never arrive, and sign-in would hang.
    /// </summary>
    private Attempt? inFlight;

    public async ValueTask<AppleIdentityGrant> AuthorizeAsync(string nonceSha256Hex, CancellationToken cancellationToken = default)
    {
        var attempt = new Attempt();
        inFlight = attempt;
        try
        {
            return await attempt.RunAsync(nonceSha256Hex, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(inFlight, attempt))
            {
                inFlight = null;
            }
        }
    }

    /// <summary>One sheet: the controller and the delegate it reports to.</summary>
    private sealed class Attempt : NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        private readonly TaskCompletionSource<AppleIdentityGrant> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ASAuthorizationController? controller;

        public async Task<AppleIdentityGrant> RunAsync(string nonceSha256Hex, CancellationToken cancellationToken)
        {
            ASAuthorizationAppleIdRequest request = new ASAuthorizationAppleIdProvider().CreateRequest();
            // The address identifies the account in the UI; the name is not used for anything.
            request.RequestedScopes = [ASAuthorizationScope.Email];
            request.Nonce = nonceSha256Hex;

            controller = new ASAuthorizationController([request])
            {
                Delegate = this,
                PresentationContextProvider = this,
            };

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                // Dismisses the sheet where iOS can; the result is reported as a cancellation either way.
                if (OperatingSystem.IsIOSVersionAtLeast(16))
                {
                    controller?.Cancel();
                }

                completion.TrySetException(Cancelled());
            });

            controller.PerformRequests();
            return await completion.Task.ConfigureAwait(true);
        }

        [Export("authorizationController:didCompleteWithAuthorization:")]
        public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
        {
            if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is { AuthorizationCode: { } code }
                && NSString.FromData(code, NSStringEncoding.UTF8)?.ToString() is { Length: > 0 } text)
            {
                completion.TrySetResult(new AppleIdentityGrant(text));
                return;
            }

            completion.TrySetException(new AccountException(
                AccountFailure.ServerError, "Apple returned no authorization code. Try again."));
        }

        [Export("authorizationController:didCompleteWithError:")]
        public void DidComplete(ASAuthorizationController controller, NSError error)
        {
            // Closing the sheet is a decision, not a fault, and reads that way in the UI.
            completion.TrySetException(
                error.Code == (long)ASAuthorizationError.Canceled
                    ? Cancelled()
                    : new AccountException(AccountFailure.ServerError, "Sign in with Apple did not complete. Try again."));
        }

        public UIWindow GetPresentationAnchor(ASAuthorizationController controller) =>
            UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(scene => scene.Windows)
                .FirstOrDefault(window => window.IsKeyWindow)
            ?? throw new InvalidOperationException("Sign-in was started before the app had a window.");

        private static AccountException Cancelled() =>
            new(AccountFailure.SignInCancelled, "The sign-in was not completed. Try again when you are ready.");
    }
}
