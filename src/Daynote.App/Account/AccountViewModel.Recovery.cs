using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// The password-reset and unlock surface (docs/CLOUD_SYNC.md §4.8).
/// </summary>
/// <remarks>
/// The flow is deliberately two screens rather than one. A reset gets the account back; opening the
/// notes is a separate question with a separate answer, and merging them into one form would let
/// someone change their password without ever being told the cloud copy is now locked.
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>True while the reset form is on screen.</summary>
    [ObservableProperty]
    private bool isResetting;

    [ObservableProperty]
    private string resetCode = string.Empty;

    [ObservableProperty]
    private string? resetSentTo;

    /// <summary>True while the unlock form is on screen, after a reset left the account locked.</summary>
    [ObservableProperty]
    private bool isUnlocking;

    [ObservableProperty]
    private string recoveryKeyEntry = string.Empty;

    public string ResetSentMessage => ResetSentTo is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, AppStrings.AccountResetSentFormat, ResetSentTo);

    [RelayCommand]
    private void BeginReset()
    {
        IsResetting = true;
        ErrorMessage = null;
        ResetSentTo = null;
        ResetCode = string.Empty;
    }

    [RelayCommand]
    private void CancelReset()
    {
        IsResetting = false;
        ResetCode = string.Empty;
        ResetSentTo = null;
    }

    [RelayCommand]
    private async Task RequestResetCodeAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.RequestPasswordResetAsync(Email).ConfigureAwait(true);
            // Reported the same way whether or not the address is registered, because the server
            // deliberately will not say and repeating that here would undo it.
            ResetSentTo = Email;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ConfirmResetAsync(string? newPassword)
    {
        await RunAsync(async () =>
        {
            await accounts
                .ConfirmPasswordResetAsync(Email, ResetCode, newPassword ?? string.Empty)
                .ConfigureAwait(true);

            IsResetting = false;
            ResetCode = string.Empty;
            SignedInEmail = Email;

            SyncStateSnapshot state = await store.ReadStateAsync().ConfigureAwait(true);
            IsLocked = state.IsLocked;
            // Locked means the reset happened somewhere the key was not cached, so the recovery key is
            // the only way forward. Unlocked means this PC had it and there is nothing left to ask.
            IsUnlocking = state.IsLocked;
            if (!state.IsLocked)
            {
                await SyncAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UnlockAsync(string? password)
    {
        await RunAsync(async () =>
        {
            var parsed = RecoveryKey.Parse(RecoveryKeyEntry);
            if (!parsed.IsSuccess)
            {
                // Caught here rather than sent to the server: a malformed key is a typo, and a round
                // trip would only make the user wait to be told so.
                ErrorMessage = AppStrings.AccountErrorInvalidRecoveryKeyEntered;
                return;
            }

            await accounts
                .UnlockWithRecoveryKeyAsync(parsed.Value, password ?? string.Empty)
                .ConfigureAwait(true);

            IsUnlocking = false;
            IsLocked = false;
            RecoveryKeyEntry = string.Empty;
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// The last resort when there is no recovery key and no earlier device. Destructive to the cloud
    /// copy and nothing else, which is why it is offered at all rather than leaving the user stuck.
    /// </summary>
    [RelayCommand]
    private async Task DiscardCloudCopyAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.DiscardCloudCopyAsync().ConfigureAwait(true);
            SignedInEmail = null;
            IsLocked = false;
            IsUnlocking = false;
            RecoveryKeyEntry = string.Empty;
            Status = SyncStatusView.Hidden;
        }).ConfigureAwait(true);
    }

    partial void OnResetSentToChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(ResetSentMessage));
    }

    partial void OnIsResettingChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsSignedOut));
    }
}
