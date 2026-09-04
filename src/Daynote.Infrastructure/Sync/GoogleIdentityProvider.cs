using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Google sign-in for a desktop app: the system browser plus a loopback listener (RFC 8252).
/// </summary>
/// <remarks>
/// <para>
/// The app never embeds a web view. Signing in happens in the browser the user already trusts and
/// is already signed into, which is both the recommendation and the only flow Google still supports
/// for installed apps — the old out-of-band "copy this code" redirect was withdrawn.
/// </para>
/// <para>
/// The loopback listener binds an OS-assigned port on 127.0.0.1 and lives only for the duration of
/// one sign-in. PKCE binds the returned code to this attempt; the code is then handed to the Worker,
/// which owns the client secret and performs the exchange, so no secret ships inside this binary.
/// </para>
/// </remarks>
public sealed class GoogleIdentityProvider : IIdentityProvider
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>Identity only. Anything beyond these would drag the project into Google review.</summary>
    private const string Scopes = "openid email profile";

    /// <summary>
    /// Long enough for a real sign-in — a password, a second factor, picking an account — and short
    /// enough that an abandoned attempt eventually releases the port instead of leaking a listener.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly string clientId;
    private readonly Action<string> openBrowser;

    public GoogleIdentityProvider(string clientId, Action<string>? openBrowser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        this.clientId = clientId;
        this.openBrowser = openBrowser ?? LaunchDefaultBrowser;
    }

    public async ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        string verifier = CreateVerifier();
        string state = CreateVerifier();

        using var listener = new HttpListener();
        string redirectUri = Bind(listener);

        var url = new StringBuilder(AuthorizeEndpoint)
            .Append("?response_type=code")
            .Append("&client_id=").Append(Uri.EscapeDataString(clientId))
            .Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri))
            .Append("&scope=").Append(Uri.EscapeDataString(Scopes))
            .Append("&code_challenge=").Append(Challenge(verifier))
            .Append("&code_challenge_method=S256")
            .Append("&state=").Append(state)
            // Without this, a browser already signed into one Google account skips the chooser, and
            // someone with several accounts can never pick which one Daynote uses.
            .Append("&prompt=select_account")
            .ToString();

        openBrowser(url);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AccountException(
                AccountFailure.SignInCancelled,
                "The sign-in was not completed. Try again when you are ready.");
        }

        try
        {
            return ReadGrant(context, state, verifier, redirectUri);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Binds 127.0.0.1 on a port the OS picks. A fixed port would collide with whatever else is
    /// listening and would make two Daynote windows fight over one sign-in.
    /// </summary>
    private static string Bind(HttpListener listener)
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        string redirectUri = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        return redirectUri;
    }

    private static IdentityGrant ReadGrant(
        HttpListenerContext context,
        string expectedState,
        string verifier,
        string redirectUri)
    {
        System.Collections.Specialized.NameValueCollection query = context.Request.QueryString;
        string? code = query["code"];
        string? error = query["error"];
        bool stateMatches = string.Equals(query["state"], expectedState, StringComparison.Ordinal);

        Respond(context, error is null && code is not null && stateMatches);

        if (error is not null)
        {
            // access_denied is the user pressing Cancel on the consent screen — expected, not a fault.
            throw new AccountException(
                AccountFailure.SignInCancelled,
                error == "access_denied"
                    ? "Sign-in was cancelled."
                    : $"Google refused the sign-in ({error}).");
        }

        if (!stateMatches)
        {
            // Something answered our loopback port that did not start this flow.
            throw new AccountException(AccountFailure.ServerError, "The sign-in response did not match this attempt.");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new AccountException(AccountFailure.SignInCancelled, "Google returned no authorization code.");
        }

        return new IdentityGrant(code, verifier, redirectUri);
    }

    /// <summary>
    /// The page the browser is left showing. Deliberately self-contained: it is served from a
    /// loopback port that closes a moment later, so it can load nothing.
    /// </summary>
    private static void Respond(HttpListenerContext context, bool success)
    {
        string message = success
            ? "Daynote is signed in. You can close this tab."
            : "Daynote could not complete the sign-in. Return to the app and try again.";

        byte[] body = Encoding.UTF8.GetBytes(
            $"<!doctype html><meta charset=\"utf-8\"><title>Daynote</title>" +
            $"<body style=\"font:15px system-ui;margin:64px;color:#1a1a1e\">{message}</body>");

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.Close();
    }

    private static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static void LaunchDefaultBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
