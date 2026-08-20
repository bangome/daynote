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
/// Failures are mapped from <see cref="AccountFailure"/> to localized copy here. The messages carried
/// on <see cref="AccountException"/> are developer-facing English and must never reach the UI — this
/// app ships in Korean and English, and an untranslated string is a defect.
/// </remarks>
public sealed partial class AccountViewModel : ObservableObject, ILanguageAware
{
    private readonly AccountService accounts;
    private readonly Func<ValueTask<SyncReport>> syncNow;
    private readonly ISyncStore store;
    private readonly IRecoveryKeyExporter exporter;
    private readonly Action<string> revealFolder;
    private readonly string conflictsPath;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? signedInEmail;

    [ObservableProperty]
    private string? lastSyncText;

    [ObservableProperty]
    private bool isLocked;

    /// <summary>The one-time recovery key, shown only immediately after registering.</summary>
    [ObservableProperty]
    private string? recoveryKeyDisplay;

    [ObservableProperty]
    private bool recoveryKeyAcknowledged;

    [ObservableProperty]
    private bool recoveryKeyCopied;

    [ObservableProperty]
    private int replacedNoteCount;

    private SyncStatusView status = SyncStatusView.Hidden;

    public AccountViewModel(
        AccountService accounts,
        ISyncStore store,
        Func<ValueTask<SyncReport>> syncNow,
        IRecoveryKeyExporter exporter,
        Action<string> revealFolder,
        string conflictsPath)
    {
        this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.syncNow = syncNow ?? throw new ArgumentNullException(nameof(syncNow));
        this.exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        this.revealFolder = revealFolder ?? throw new ArgumentNullException(nameof(revealFolder));
        this.conflictsPath = conflictsPath ?? throw new ArgumentNullException(nameof(conflictsPath));
    }

    public bool IsSignedIn => SignedInEmail is not null;

    public bool IsSignedOut => SignedInEmail is null && RecoveryKeyDisplay is null && !IsResetting;

    /// <summary>True while the one-time recovery key is on screen and unacknowledged.</summary>
    public bool IsShowingRecoveryKey => RecoveryKeyDisplay is not null;

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
        }
    }

    public string PasswordHint => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.AccountPasswordHint,
        AccountService.MinimumPasswordLength);

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

        SignedInEmail = state.IsSignedIn && resumed.State != ResumeState.SignedOut ? Email : null;
        IsLocked = state.IsLocked;
        ApplyLastSync(state.LastSyncUtc);
        RefreshStatus(state);
    }

    [RelayCommand]
    private async Task SignInAsync(string? password)
    {
        await RunAsync(async () =>
        {
            await accounts.SignInAsync(Email, password ?? string.Empty).ConfigureAwait(true);
            SignedInEmail = Email;
            IsLocked = false;
            await SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RegisterAsync(string? password)
    {
        await RunAsync(async () =>
        {
            RegisteredAccount account = await accounts
                .RegisterAsync(Email, password ?? string.Empty)
                .ConfigureAwait(true);

            // Shown once and never persisted anywhere: writing it down is the user's job, and keeping
            // a copy would defeat the point of the password never being recoverable by us.
            RecoveryKeyDisplay = account.RecoveryKey.ToDisplayString();
            RecoveryKeyAcknowledged = false;
            RecoveryKeyCopied = false;
            SignedInEmail = Email;
            IsLocked = false;
        }).ConfigureAwait(true);
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
    private async Task DismissRecoveryKeyAsync()
    {
        RecoveryKeyDisplay = null;
        RecoveryKeyCopied = false;
        await SyncAsync().ConfigureAwait(true);
    }

    private bool CanDismissRecoveryKey() => RecoveryKeyAcknowledged;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            await accounts.SignOutAsync().ConfigureAwait(true);
            SignedInEmail = null;
            IsLocked = false;
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
            IsLocked = report.Outcome == SyncOutcome.Locked;

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
        revealFolder(conflictsPath);
        ReplacedNoteCount = 0;
    }

    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(PasswordHint));
        OnPropertyChanged(nameof(ReplacedNotesMessage));
        OnPropertyChanged(nameof(Status));
    }

    private static SyncStatusView FromReport(SyncReport report) => report.Outcome switch
    {
        SyncOutcome.SignedOut => SyncStatusView.Hidden,
        SyncOutcome.Locked => new SyncStatusView(SyncStatusKind.Locked),
        SyncOutcome.ClockSkew => new SyncStatusView(SyncStatusKind.Error),
        // Offline is a state, not a fault: no message, and the next cycle retries.
        SyncOutcome.Offline => new SyncStatusView(SyncStatusKind.Offline),
        SyncOutcome.SignInRequired => new SyncStatusView(SyncStatusKind.Error),
        // An unreadable record is not a transient hiccup: something is wrong with the key or the data
        // and the user needs to know rather than wonder why a note never arrived.
        _ when report.HasUnreadableRecords => new SyncStatusView(SyncStatusKind.Error),
        _ => new SyncStatusView(SyncStatusKind.Synced),
    };

    private void RefreshStatus(SyncStateSnapshot state)
    {
        Status = !state.IsSignedIn
            ? SyncStatusView.Hidden
            : state.IsLocked
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
            if (failure.Failure == AccountFailure.RewrapRequired)
            {
                IsLocked = true;
                SignedInEmail = Email;
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
        AccountFailure.EmailAlreadyRegistered => AppStrings.AccountErrorEmailTaken,
        AccountFailure.InvalidEmail => AppStrings.AccountErrorInvalidEmail,
        AccountFailure.WeakPassword => string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.AccountErrorWeakPassword,
            AccountService.MinimumPasswordLength),
        AccountFailure.RewrapRequired => AppStrings.AccountErrorRewrapRequired,
        AccountFailure.UnsupportedKdfProfile => AppStrings.AccountErrorUnsupportedVersion,
        AccountFailure.Offline => AppStrings.AccountErrorOffline,
        _ => AppStrings.AccountErrorServer,
    };

    partial void OnSignedInEmailChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
    }

    partial void OnRecoveryKeyDisplayChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsShowingRecoveryKey));
        OnPropertyChanged(nameof(IsSignedOut));
    }

    partial void OnRecoveryKeyAcknowledgedChanged(bool value)
    {
        _ = value;
        DismissRecoveryKeyCommand.NotifyCanExecuteChanged();
    }

    partial void OnReplacedNoteCountChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(HasReplacedNotes));
        OnPropertyChanged(nameof(ReplacedNotesMessage));
    }
}

/// <summary>
/// Puts the one-time recovery key somewhere the user keeps it. Abstracted so the view model can be
/// tested without a clipboard or a file dialog.
/// </summary>
public interface IRecoveryKeyExporter
{
    bool TryCopyToClipboard(string recoveryKey);

    bool TrySaveToFile(string recoveryKey);
}
