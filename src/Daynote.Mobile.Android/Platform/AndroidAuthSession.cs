using Android.App;
using Android.Content;
using AndroidX.Browser.CustomTabs;

namespace Daynote.Mobile.Android.Platform;

/// <summary>
/// Google sign-in on Android, as a Custom Tab: the user's own browser, with its cookies and its
/// password manager, drawn inside the app.
/// </summary>
/// <remarks>
/// <para>
/// Not a WebView. Google blocks sign-in from embedded web views outright (a web view lets the host
/// app read the password), and a Custom Tab is the sanctioned alternative — same browser process,
/// same session, no access for us.
/// </para>
/// <para>
/// The redirect comes back through <see cref="AuthCallbackActivity"/>, which the manifest registers
/// for the app's private scheme, and lands in <see cref="Complete"/>. Only one sign-in can be in
/// flight; starting another abandons the first, which matches what the UI allows.
/// </para>
/// </remarks>
public static class AndroidAuthSession
{
    private static TaskCompletionSource<Uri?>? _pending;

    /// <summary>Opens the tab and waits for the redirect, or null if the user came back without one.</summary>
    public static Task<Uri?> StartAsync(
        Activity activity, string authorizationUrl, string callbackScheme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _ = callbackScheme;

        // A tab still open from an abandoned attempt must not resolve this one.
        _pending?.TrySetResult(null);

        var completion = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion;
        cancellationToken.Register(() => completion.TrySetResult(null));

        using CustomTabsIntent intent = new CustomTabsIntent.Builder().SetShowTitle(true)!.Build()
            ?? throw new InvalidOperationException("Custom Tabs produced no intent.");
        intent.Intent?.AddFlags(ActivityFlags.NoHistory);
        if (global::Android.Net.Uri.Parse(authorizationUrl) is { } uri)
        {
            intent.LaunchUrl(activity, uri);
        }
        else
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    /// <summary>Called by the activity that receives the redirect.</summary>
    public static void Complete(Uri callback)
    {
        TaskCompletionSource<Uri?>? pending = _pending;
        _pending = null;
        pending?.TrySetResult(callback);
    }

    /// <summary>
    /// Called when the user returns to the app without having completed sign-in — dismissing the tab
    /// leaves no redirect behind, so nothing else would ever resolve the wait.
    /// </summary>
    public static void Abandon()
    {
        TaskCompletionSource<Uri?>? pending = _pending;
        _pending = null;
        pending?.TrySetResult(null);
    }
}

/// <summary>
/// Receives the OAuth redirect on the app's private scheme and hands it to the main activity.
/// </summary>
/// <remarks>
/// A separate, <c>NoDisplay</c> activity rather than an intent filter on <c>MainActivity</c>: the
/// browser has to be able to resolve the scheme even while the app is backgrounded, and routing it
/// through a throwaway activity means the main activity is simply brought forward with the redirect
/// as a new intent, keeping its Avalonia surface and the editor state intact.
/// </remarks>
[Activity(NoHistory = true, LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = AuthCallbackActivity.Scheme)]
public sealed class AuthCallbackActivity : Activity
{
    /// <summary>
    /// The app's private redirect scheme. It must equal the scheme in the Google client's redirect
    /// URI; see docs/MOBILE_PORT.md, which is also where the value below is generated from.
    /// </summary>
    public const string Scheme = "cc.arachat.daynote";

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Intent?.Data is { } data)
        {
            AndroidAuthSession.Complete(new Uri(data.ToString()!));
        }

        StartActivity(new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.ReorderToFront));
        Finish();
    }
}
