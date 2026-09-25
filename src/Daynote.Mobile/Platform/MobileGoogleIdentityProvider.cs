using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Daynote.Core.Sync;

namespace Daynote.Mobile.Platform;

/// <summary>
/// Opens an authentication session for one sign-in and returns the URI the provider redirected to,
/// or null when the user dismissed it.
/// </summary>
/// <remarks>
/// Implemented by each head over the platform's own browser-with-a-callback: Custom Tabs on Android,
/// <c>ASWebAuthenticationSession</c> on iOS. Both hand the app the redirect without the app ever
/// seeing the password, which is what a web view would break.
/// </remarks>
/// <param name="authorizationUrl">Where to send the user.</param>
/// <param name="callbackScheme">The custom scheme the redirect will use, which the session watches for.</param>
public delegate Task<Uri?> AuthenticationSession(
    string authorizationUrl, string callbackScheme, CancellationToken cancellationToken);

/// <summary>
/// Google sign-in on a phone: the platform's authentication session plus PKCE and a custom-scheme
/// redirect (RFC 8252 §7.1).
/// </summary>
/// <remarks>
/// <para>
/// The desktop provider cannot be reused, and not for a small reason: it binds a loopback HTTP port
/// and waits for the browser to call back. Neither phone OS lets an app hold a listening socket the
/// system browser can reach while it is in the background, so installed apps there use a private URI
/// scheme instead — the OS routes the redirect straight to the app that registered it.
/// </para>
/// <para>
/// The client id belongs to a Google OAuth client of type iOS or Android, not the desktop one in
/// <c>DaynoteAppOptions</c>: Google binds mobile clients to the bundle id or the package name plus
/// signing certificate, and rejects an authorize call that mixes them up. Those clients have no
/// client secret at all, so, as on the desktop, nothing secret ships in the binary; the Worker still
/// performs the code exchange. See docs/MOBILE_PORT.md for the console setup and the Worker change
/// that has to accompany it.
/// </para>
/// </remarks>
public sealed class MobileGoogleIdentityProvider : IIdentityProvider
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>Identity only. Anything beyond these would drag the project into Google review.</summary>
    private const string Scopes = "openid email profile";

    private readonly string _clientId;
    private readonly string _redirectUri;
    private readonly AuthenticationSession _session;

    /// <param name="clientId">The iOS or Android OAuth client id for this build.</param>
    /// <param name="redirectUri">
    /// The redirect this client is registered with. Google's convention for a mobile client is the
    /// reversed client id as the scheme, e.g.
    /// <c>com.googleusercontent.apps.123-abc:/oauth2redirect</c>.
    /// </param>
    public MobileGoogleIdentityProvider(string clientId, string redirectUri, AuthenticationSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        _clientId = clientId;
        _redirectUri = redirectUri;
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        string verifier = CreateVerifier();
        string state = CreateVerifier();

        string url = new StringBuilder(AuthorizeEndpoint)
            .Append("?response_type=code")
            .Append("&client_id=").Append(Uri.EscapeDataString(_clientId))
            .Append("&redirect_uri=").Append(Uri.EscapeDataString(_redirectUri))
            .Append("&scope=").Append(Uri.EscapeDataString(Scopes))
            .Append("&code_challenge=").Append(Challenge(verifier))
            .Append("&code_challenge_method=S256")
            .Append("&state=").Append(state)
            // Without this, a browser already signed into one Google account skips the chooser, and
            // someone with several accounts can never pick which one Daynote uses.
            .Append("&prompt=select_account")
            .ToString();

        string scheme = _redirectUri[.._redirectUri.IndexOf(':', StringComparison.Ordinal)];

        Uri? callback;
        try
        {
            callback = await _session(url, scheme, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AccountException(
                AccountFailure.SignInCancelled, "The sign-in was not completed. Try again when you are ready.");
        }

        if (callback is null)
        {
            // Dismissing the sheet is an ordinary outcome, not a fault.
            throw new AccountException(AccountFailure.SignInCancelled, "Sign-in was cancelled.");
        }

        return ReadGrant(callback, state, verifier);
    }

    private IdentityGrant ReadGrant(Uri callback, string expectedState, string verifier)
    {
        // The parameters may be in the query or, for some error redirects, the fragment.
        System.Collections.Specialized.NameValueCollection query =
            System.Web.HttpUtility.ParseQueryString(
                callback.Query.Length > 1 ? callback.Query : callback.Fragment.TrimStart('#'));

        string? error = query["error"];
        if (error is not null)
        {
            throw new AccountException(
                AccountFailure.SignInCancelled,
                error == "access_denied" ? "Sign-in was cancelled." : $"Google refused the sign-in ({error}).");
        }

        if (!string.Equals(query["state"], expectedState, StringComparison.Ordinal))
        {
            // Something answered our scheme that did not start this flow.
            throw new AccountException(AccountFailure.ServerError, "The sign-in response did not match this attempt.");
        }

        if (query["code"] is not { Length: > 0 } code)
        {
            throw new AccountException(AccountFailure.ServerError, "Google returned no authorization code.");
        }

        return new IdentityGrant(code, verifier, _redirectUri);
    }

    private static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
