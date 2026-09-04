using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// The account surface: the settings section and the command-row chip both bind to this one instance,
/// so there is a single answer to "are we signed in" and "what is sync doing".
/// </summary>
/// <remarks>
/// Sign-in is one button. Everything it used to need — an address, a password, a recovery key, a
/// reset code — left with the password model: Google establishes who you are, and the data key
/// arrives with the session. The consequence is stated in the UI, not just the privacy policy: the
/// server can read what it stores (docs/CLOUD_SYNC.md §1).
/// <para>
/// Failures are mapped from <see cref="AccountFailure"/> to localized copy here. The messages carried
/// on <see cref="AccountException"/> are developer-facing English and must never reach the UI — this
/// app ships in Korean and English, and an untranslated string is a defect.
/// </para>
/// </remarks>
public sealed partial class AccountViewModel : ObservableObject, ILanguageAware
{
    private readonly AccountService accounts;
    private readonly Func<ValueTask<SyncReport>> syncNow;
    private readonly ISyncStore store;
    private readonly IRecoveryKeyExporter exporter;
    private readonly Action<string> openExternal;
    private readonly string conflictsPath;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? signedInEmail;

    [ObservableProperty]
    private string? lastSyncText;

    /// <summary>Signed in, but this device holds no data key yet. Re-fetchable without the browser.</summary>
    [ObservableProperty]
    private bool isKeyMissing;

    [ObservableProperty]
    private int replacedNoteCount;

    private SyncStatusView status = SyncStatusView.Hidden;

    public AccountViewModel(
        AccountService accounts,
        ISyncStore store,
        Func<ValueTask<SyncReport>> syncNow,
        IRecoveryKeyExporter exporter,
        Action<string> openExternal,
        string conflictsPath)
    {
        this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.syncNow = syncNow ?? throw new ArgumentNullException(nameof(syncNow));
        this.exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        this.openExternal = openExternal ?? throw new ArgumentNullException(nameof(openExternal));
        this.conflictsPath = conflictsPath ?? throw new ArgumentNullException(nameof(conflictsPath));
    }

    public bool IsSignedIn => SignedInEmail is not null;

    public bool IsSignedOut => SignedInEmail is null;

    public bool HasReplacedNotes => ReplacedNoteCount > 0;

    public SyncStatusView Status
    {
        get => status;
        internal set
        {
            if (status == value)
            {
                return;
            }

            status = value;
            OnPropertyChanged();
            RefreshPresentation();
        }
    }

    public string ReplacedNotesMessage => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.AccountConflictsFormat,
        ReplacedNoteCount);

    /// <summary>
    /// Reads the persisted state at startup. Deliberately does not sync: launching must not depend on
    /// the network, and a signed-in user should see their notes before anything is fetched.
    /// </summary>
    public async Task InitializeAsync()
    {
        SyncStateSnapshot state = await store.ReadStateAsync().ConfigureAwait(true);
        ResumedSession resumed = await accounts.ResumeAsync().ConfigureAwait(true);
        resumed.Session?.DataKey.Dispose();

        IsKeyMissing = resumed.State == ResumeState.KeyMissing;
        IsLocked = resumed.State == ResumeState.Locked;
        IsLockEnabled = IsLocked;
        SignedInEmail = state.IsSignedIn && resumed.State != ResumeState.SignedOut ? resumed.Email : null;
        ApplyLastSync(state.LastSyncUtc);
        RefreshStatus(state);
    }

    /// <summary>
    /// Opens the browser, signs in, and syncs. The browser wait is long, so this is the one command
    /// that can sit busy for minutes; closing the window resolves it as a cancellation, not an error.
    /// </summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        await RunAsync(async () =>
        {
            SignedInEmail = await accounts.SignInAsync().ConfigureAwait(true);
            IsKeyMissing = false;
            IsLocked = false;
            IsLockEnabled = false;
            await RefreshBillingAsync().ConfigureAwait(true);
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>Re-fetches the data key for a session that still has valid tokens.</summary>
    [RelayCommand]
    private async Task RestoreKeyAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.RestoreDataKeyAsync().ConfigureAwait(true);
            IsKeyMissing = false;
            await store.SetLockedAsync(false).ConfigureAwait(true);
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.SignOutAsync().ConfigureAwait(true);
            Entitlement = Entitlement.Unknown;
            Billing = BillingLinks.None;
            SignedInEmail = null;
            IsKeyMissing = false;
            IsLocked = false;
            IsLockEnabled = false;
            IsEnablingLock = false;
            ReplacedNoteCount = 0;
            LastSyncText = null;
            Status = SyncStatusView.Hidden;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (!IsSignedIn)
        {
            return;
        }

        Status = new SyncStatusView(SyncStatusKind.Syncing);
        try
        {
            SyncReport report = await syncNow().ConfigureAwait(true);
            ReplacedNoteCount += report.ConflictsSaved;
            IsKeyMissing = report.Outcome == SyncOutcome.Locked;
            if (report.Outcome == SyncOutcome.SubscriptionRequired)
            {
                // The server just said the subscription lapsed. Re-reading it here means the panel
                // and the chip agree without the user having to reopen settings.
                await RefreshBillingAsync().ConfigureAwait(true);
            }

            SyncStateSnapshot state = await store.ReadStateAsync().ConfigureAwait(true);
            ApplyLastSync(state.LastSyncUtc);
            Status = FromReport(report);
        }
        catch (AccountException failure)
        {
            ErrorMessage = Describe(failure.Failure);
            Status = new SyncStatusView(
                failure.Failure == AccountFailure.Offline ? SyncStatusKind.Offline : SyncStatusKind.Error);
        }
    }

    [RelayCommand]
    private void OpenConflictsFolder()
    {
        openExternal(conflictsPath);
        ReplacedNoteCount = 0;
    }

    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(ReplacedNotesMessage));
        OnPropertyChanged(nameof(EntitlementSummary));
        OnPropertyChanged(nameof(PassphraseHint));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(PriceUnit));
        OnPropertyChanged(nameof(PriceSub));
        OnPropertyChanged(nameof(SubscriptionPlanText));
        RefreshPresentation();
    }

    private static SyncStatusView FromReport(SyncReport report) => report.Outcome switch
    {
        SyncOutcome.SignedOut => SyncStatusView.Hidden,
        SyncOutcome.Locked => new SyncStatusView(SyncStatusKind.Locked),
        SyncOutcome.ClockSkew => new SyncStatusView(SyncStatusKind.Error),
        // Offline is a state, not a fault: no message, and the next cycle retries.
        SyncOutcome.Offline => new SyncStatusView(SyncStatusKind.Offline),
        SyncOutcome.SignInRequired => new SyncStatusView(SyncStatusKind.Error),
        SyncOutcome.SubscriptionRequired => new SyncStatusView(SyncStatusKind.Unpaid),
        // An unreadable record is not a transient hiccup: something is wrong with the key or the data
        // and the user needs to know rather than wonder why a note never arrived.
        _ when report.HasUnreadableRecords => new SyncStatusView(SyncStatusKind.Error),
        _ => new SyncStatusView(SyncStatusKind.Synced),
    };

    private void RefreshStatus(SyncStateSnapshot state)
    {
        Status = !state.IsSignedIn
            ? SyncStatusView.Hidden
            : state.IsLocked || IsKeyMissing || IsLocked
                ? new SyncStatusView(SyncStatusKind.Locked)
                : new SyncStatusView(state.LastSyncUtc is null ? SyncStatusKind.Pending : SyncStatusKind.Synced);
    }

    private void ApplyLastSync(DateTimeOffset? lastSyncUtc) =>
        LastSyncText = lastSyncUtc is { } instant
            ? string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.AccountLastSyncFormat,
                instant.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : AppStrings.AccountNeverSynced;

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (AccountException failure)
        {
            ErrorMessage = Describe(failure.Failure);
            if (failure.Failure == AccountFailure.LockedOut)
            {
                // Signed in, but this device holds nothing that opens the notes. Reporting it as a
                // signed-out account would hide the only route to the unlock prompt.
                IsLocked = true;
                IsLockEnabled = true;
                IsKeyMissing = false;
                SignedInEmail ??= AppStrings.AccountLockedTitle;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(AccountFailure failure) => failure switch
    {
        AccountFailure.InvalidCredentials => AppStrings.AccountErrorInvalidCredentials,
        // The lock failures were the whole point of asking for a passphrase, so each one says what
        // the user can actually do about it rather than falling through to "try again later".
        AccountFailure.InvalidPassphrase => AppStrings.AccountErrorInvalidPassphrase,
        AccountFailure.InvalidRecoveryKey => AppStrings.AccountErrorInvalidRecoveryKey,
        AccountFailure.LockedOut => AppStrings.AccountErrorLockedOut,
        AccountFailure.WeakPassphrase => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            AppStrings.AccountErrorWeakPassphrase,
            AccountService.MinimumPassphraseLength),
        AccountFailure.UnsupportedKdfProfile => AppStrings.AccountErrorUnsupportedVersion,
        // Closing the browser is a decision, not a failure, so it gets a plain sentence rather than
        // an error banner that implies something broke.
        AccountFailure.SignInCancelled => AppStrings.AccountErrorSignInCancelled,
        AccountFailure.UnverifiedIdentity => AppStrings.AccountErrorUnverifiedIdentity,
        AccountFailure.Offline => AppStrings.AccountErrorOffline,
        _ => AppStrings.AccountErrorServer,
    };

    partial void OnSignedInEmailChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        RefreshPresentation();
    }

    partial void OnReplacedNoteCountChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(HasReplacedNotes));
        OnPropertyChanged(nameof(ReplacedNotesMessage));
    }
}
