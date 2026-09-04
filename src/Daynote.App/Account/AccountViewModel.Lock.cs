using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// The opt-in lock, as the settings panel sees it (docs/CLOUD_SYNC.md §4.1b).
/// </summary>
/// <remarks>
/// Three surfaces, and only one is ever on screen at a time: the invitation to turn the lock on, the
/// one-time recovery key, and the unlock prompt a device gets when it signs in to a locked account.
/// The recovery key cannot be dismissed unacknowledged — it is shown exactly once, and with the
/// server's copy of the key destroyed it is the only way back in if the passphrase is forgotten.
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>True when the account's key is wrapped under a passphrase the server never sees.</summary>
    [ObservableProperty]
    private bool isLockEnabled;

    /// <summary>True while the passphrase form for turning the lock on is open.</summary>
    [ObservableProperty]
    private bool isEnablingLock;

    /// <summary>True while this device is signed in to a locked account it has not unlocked yet.</summary>
    [ObservableProperty]
    private bool isLocked;

    /// <summary>The one-time recovery key, on screen only immediately after the lock is turned on.</summary>
    [ObservableProperty]
    private string? recoveryKeyDisplay;

    [ObservableProperty]
    private bool recoveryKeyAcknowledged;

    [ObservableProperty]
    private bool recoveryKeyCopied;

    /// <summary>True while the unlock form offers the recovery key instead of the passphrase.</summary>
    [ObservableProperty]
    private bool isUsingRecoveryKey;

    [ObservableProperty]
    private string recoveryKeyEntry = string.Empty;

    public bool IsShowingRecoveryKey => RecoveryKeyDisplay is not null;

    public string PassphraseHint => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        AppStrings.AccountPassphraseHint,
        AccountService.MinimumPassphraseLength);

    /// <summary>Opens or closes the passphrase form. Nothing is sent until it is submitted.</summary>
    [RelayCommand]
    private void ToggleEnableLock()
    {
        IsEnablingLock = !IsEnablingLock;
        ErrorMessage = null;
    }

    /// <summary>Turns the lock on. The passphrase comes from the PasswordBox, never from a binding.</summary>
    [RelayCommand]
    private async Task EnableLockAsync(string? passphrase)
    {
        await RunAsync(async () =>
        {
            RecoveryKey key = await accounts.EnableLockAsync(passphrase ?? string.Empty)
                .ConfigureAwait(true);

            RecoveryKeyDisplay = key.ToDisplayString();
            RecoveryKeyAcknowledged = false;
            RecoveryKeyCopied = false;
            IsEnablingLock = false;
            IsLockEnabled = true;
            OnPropertyChanged(nameof(IsShowingRecoveryKey));
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DisableLockAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.DisableLockAsync().ConfigureAwait(true);
            IsLockEnabled = false;
        }).ConfigureAwait(true);
    }

    /// <summary>Unlocks this device with the passphrase, or with the recovery key when that is lost.</summary>
    [RelayCommand]
    private async Task UnlockAsync(string? passphrase)
    {
        await RunAsync(async () =>
        {
            if (IsUsingRecoveryKey)
            {
                DomainResult<RecoveryKey> parsed = RecoveryKey.Parse(RecoveryKeyEntry);
                if (!parsed.IsSuccess)
                {
                    // Caught here rather than sent to the server: a malformed key is a typo, and a
                    // round trip would only make the user wait to be told so.
                    ErrorMessage = AppStrings.AccountErrorInvalidRecoveryKey;
                    return;
                }

                await accounts.UnlockWithRecoveryKeyAsync(parsed.Value).ConfigureAwait(true);
                RecoveryKeyEntry = string.Empty;
            }
            else
            {
                await accounts.UnlockAsync(passphrase ?? string.Empty).ConfigureAwait(true);
            }

            IsLocked = false;
            IsKeyMissing = false;
            IsLockEnabled = true;
            IsUsingRecoveryKey = false;
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleRecoveryKeyEntry()
    {
        IsUsingRecoveryKey = !IsUsingRecoveryKey;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CopyRecoveryKey()
    {
        if (RecoveryKeyDisplay is { } key && exporter.TryCopyToClipboard(key))
        {
            RecoveryKeyCopied = true;
        }
    }

    [RelayCommand]
    private void SaveRecoveryKey()
    {
        if (RecoveryKeyDisplay is { } key)
        {
            exporter.TrySaveToFile(key);
        }
    }

    /// <summary>
    /// Dismisses the recovery-key screen. Only enabled once the user has confirmed they saved it, so
    /// the key cannot scroll away unnoticed on the one occasion it is visible.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDismissRecoveryKey))]
    private void DismissRecoveryKey()
    {
        RecoveryKeyDisplay = null;
        RecoveryKeyCopied = false;
        OnPropertyChanged(nameof(IsShowingRecoveryKey));
    }

    private bool CanDismissRecoveryKey() => RecoveryKeyAcknowledged;

    partial void OnRecoveryKeyAcknowledgedChanged(bool value)
    {
        _ = value;
        DismissRecoveryKeyCommand.NotifyCanExecuteChanged();
    }
}
