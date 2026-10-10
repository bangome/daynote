using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// Deleting the account, and Sign in with Apple. Both exist because the app stores require them:
/// an app that creates accounts must let people delete them from inside it (App Store 5.1.1(v),
/// Google Play's account deletion policy), and an iOS app offering Google sign-in must also offer
/// Sign in with Apple (App Store 4.8).
/// </summary>
/// <remarks>
/// Deletion is two steps on purpose — a button that asks, then one that does — because it is
/// immediate and cannot be undone on the server. Only the server copy goes: the notes on this device
/// are the user's, and losing an account is no reason to lose them.
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>The confirmation is showing: the next press deletes.</summary>
    [ObservableProperty]
    private bool isConfirmingDelete;

    private AccountNotice notice;

    /// <summary>
    /// A plain outcome worth saying out loud, such as the account having been deleted. Held as a
    /// kind rather than a sentence so a language switch rewrites it too.
    /// </summary>
    public string? NoticeMessage => notice switch
    {
        AccountNotice.Deleted => AppStrings.AccountDeleted,
        AccountNotice.SessionAlreadyGone => AppStrings.AccountDeletedSessionGone,
        AccountNotice.SessionEnded => AppStrings.AccountSessionEnded,
        _ => null,
    };

    /// <summary>The server ended the session and the device signed out; the notice says so until a sign-in.</summary>
    public bool IsSessionEnded => notice == AccountNotice.SessionEnded;

    private AccountNotice Notice
    {
        get => notice;
        set
        {
            if (notice != value)
            {
                notice = value;
                OnPropertyChanged(nameof(NoticeMessage));
                OnPropertyChanged(nameof(IsSessionEnded));
            }
        }
    }

    /// <summary>
    /// True while the Google flow has the browser open, which is the only time "your browser will
    /// open" is true; the Apple sheet is not a browser.
    /// </summary>
    [ObservableProperty]
    private bool isBrowserSignInRunning;

    /// <summary>True where Sign in with Apple exists (iOS); the button is absent everywhere else.</summary>
    public bool CanSignInWithApple => accounts.CanSignInWithApple;

    [RelayCommand]
    private void BeginDeleteAccount()
    {
        ErrorMessage = null;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDeleteAccount()
    {
        ErrorMessage = null;
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        await RunAsync(async () =>
        {
            // A run still on the wire would push into, or pull from, an account that is going away.
            await syncInFlight.ConfigureAwait(true);
            AccountDeletion outcome = await accounts.DeleteAccountAsync().ConfigureAwait(true);
            IsConfirmingDelete = false;
            ResetToSignedOut();
            if (accounts.IsAccountProfile)
            {
                // The account's notes live in its own folder, which is about to be left. Whether they
                // stay on as local notes is asked next (docs/PROFILES.md §5.5), for a session the server
                // had already forgotten as much as for a delete that just went through.
                WasSessionAlreadyGone = outcome == AccountDeletion.SessionAlreadyGone;
                IsChoosingDeletedNotes = true;
                return;
            }

            Notice = outcome == AccountDeletion.Deleted ? AccountNotice.Deleted : AccountNotice.SessionAlreadyGone;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SignInWithAppleAsync()
    {
        await RunAsync(async () =>
        {
            Notice = AccountNotice.None;
            SignInResult result = await accounts.SignInWithAppleAsync().ConfigureAwait(true);
            if (await HandOffIfNeededAsync(result).ConfigureAwait(true))
            {
                return;
            }

            SignedInEmail = NameFor(result.Email);
            IsKeyMissing = false;
            IsLocked = false;
            IsLockEnabled = false;
            await RefreshBillingAsync().ConfigureAwait(true);
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Apple may withhold the address entirely. The account is still real, so it gets a name — here
    /// and when the stored session is read back at start-up.
    /// </summary>
    private static string NameFor(string? email) =>
        string.IsNullOrWhiteSpace(email) ? AppStrings.AccountAppleFallbackName : email;

    private enum AccountNotice
    {
        None,
        Deleted,
        SessionAlreadyGone,
        SessionEnded,
    }
}
