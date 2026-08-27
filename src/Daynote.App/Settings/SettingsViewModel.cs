using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Input;
using Daynote.App.Lifecycle;
using Daynote.App.Localization;
using Daynote.Core.Backup;
using Daynote.Core.Settings;
using Daynote.Core.Startup;

namespace Daynote.App.Settings;

/// <summary>
/// The settings surface: the opt-in startup toggle, storage location, and a privacy statement. The
/// startup toggle reflects the OS state and is disabled (with explicit policy text) when Windows or a
/// policy controls it; it never silently retries an enable (plan Todo 10).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, ILanguageAware
{
    private readonly IStartupTaskService _startup;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly ISettingsStore _settings;
    private readonly IBackupService _backup;
    private readonly IBackupFilePicker _backupPicker;
    private readonly Func<Task<bool>> _flushAsync;
    private readonly Action _requestRestartForRestore;
    private readonly ConfigurableShortcuts _shortcuts;
    private readonly Action _showTutorial;
    private ShortcutRowViewModel? _capturingRow;

    /// <summary>
    /// The cloud-sync section, or null when this build has no sync endpoint configured. Null keeps the
    /// whole section out of the panel rather than showing a feature that cannot work.
    /// </summary>
    public Account.AccountViewModel? Account { get; init; }

    public SettingsViewModel(
        IStartupTaskService startup,
        IGlobalHotkeyService hotkeys,
        ISettingsStore settings,
        IBackupService backup,
        IBackupFilePicker backupPicker,
        ConfigurableShortcuts shortcuts,
        Func<Task<bool>> flushAsync,
        Action requestRestartForRestore,
        Action showTutorial,
        string storageLocation)
    {
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _backupPicker = backupPicker ?? throw new ArgumentNullException(nameof(backupPicker));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _flushAsync = flushAsync ?? throw new ArgumentNullException(nameof(flushAsync));
        _requestRestartForRestore = requestRestartForRestore ?? throw new ArgumentNullException(nameof(requestRestartForRestore));
        _showTutorial = showTutorial ?? throw new ArgumentNullException(nameof(showTutorial));
        StorageLocation = storageLocation ?? throw new ArgumentNullException(nameof(storageLocation));

        foreach (AppShortcutAction action in _shortcuts.Actions)
        {
            InAppShortcuts.Add(new ShortcutRowViewModel(
                action.Id, action.LabelKey, _shortcuts.Get(action.Id).ToDisplayString(),
                StartRowCapture, row => _ = ResetRowAsync(row)));
        }

        // External/loaded changes (e.g. LoadAsync applying persisted overrides) refresh the displays.
        _shortcuts.Changed += (_, _) => RefreshShortcutDisplays();

        AppLanguage active = LocalizationService.Instance.Language;
        LanguageOptions =
        [
            new(AppLanguage.Korean, nameof(Localization.AppStrings.LanguageKorean), active == AppLanguage.Korean),
            new(AppLanguage.English, nameof(Localization.AppStrings.LanguageEnglish), active == AppLanguage.English),
        ];

        LocalizationService.Instance.Observe(this);
    }

    /// <summary>The language segments; exactly one is selected at a time.</summary>
    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; }

    /// <summary>
    /// Switches the UI language immediately and persists the choice. The switch happens before the
    /// write so the UI never appears to lag behind the click; a failed write only costs the user
    /// their choice on the next launch, which beats a settings row that feels stuck.
    /// </summary>
    [RelayCommand]
    private async Task SelectLanguageAsync(AppLanguage language)
    {
        if (LocalizationService.Instance.Language == language)
        {
            return;
        }

        LocalizationService.Instance.SetLanguage(language);
        await _settings.SetAsync(UiSettings.LanguageKey, AppLanguages.ToTag(language)).ConfigureAwait(true);
    }

    /// <summary>
    /// Everything visible here is derived from the catalog, so the cheapest correct refresh is to
    /// invalidate the whole view model. The empty property name is WPF's "re-read every binding".
    /// </summary>
    void ILanguageAware.OnLanguageChanged()
    {
        AppLanguage active = LocalizationService.Instance.Language;
        foreach (LanguageOptionViewModel option in LanguageOptions)
        {
            option.IsSelected = option.Language == active;
        }

        OnPropertyChanged(string.Empty);
    }

    public string StorageLocation { get; }

    /// <summary>Re-opens the onboarding tutorial from Settings.</summary>
    [RelayCommand]
    private void ShowTutorial() => _showTutorial();

    /// <summary>The app author, shown in the settings "제작자" row.</summary>
    public string AuthorName => Localization.AppStrings.AuthorName;

    public string AuthorEmail => Localization.AppStrings.AuthorEmail;

    /// <summary>Transient status under the backup/restore row (progress, success, error, restart notice).</summary>
    [ObservableProperty]
    private string? _backupStatusText;

    /// <summary>Backs up all data to a user-chosen zip. Flushes the in-progress note first for consistency.</summary>
    [RelayCommand]
    private async Task BackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!await _flushAsync().ConfigureAwait(true))
            {
                BackupStatusText = Localization.AppStrings.BackupFlushBlocked;
                return;
            }

            string defaultName = string.Create(
                CultureInfo.InvariantCulture, $"Daynote-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            string? destination = _backupPicker.PickSaveZip(defaultName);
            if (destination is null)
            {
                return; // cancelled
            }

            BackupStatusText = Localization.AppStrings.BackupInProgress;
            await _backup.CreateBackupAsync(destination).ConfigureAwait(true);
            BackupStatusText = Localization.AppStrings.BackupSucceeded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupStatusText = Localization.AppStrings.BackupFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Stages a chosen backup and asks the app to restart so it applies before the db opens.</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string? source = _backupPicker.PickOpenZip();
        if (source is null)
        {
            return; // cancelled
        }

        IsBusy = true;
        try
        {
            RestoreStageResult result = await _backup.StageRestoreAsync(source).ConfigureAwait(true);
            switch (result.Status)
            {
                case RestoreStageStatus.Staged:
                    BackupStatusText = Localization.AppStrings.RestoreStagedRestarting;
                    _requestRestartForRestore();
                    break;
                case RestoreStageStatus.IncompatibleVersion:
                    BackupStatusText = Localization.AppStrings.RestoreIncompatible;
                    break;
                case RestoreStageStatus.InvalidArchive:
                    BackupStatusText = Localization.AppStrings.RestoreInvalid;
                    break;
                default:
                    BackupStatusText = Localization.AppStrings.RestoreFailed;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The editable in-app shortcut rows (one per configurable action).</summary>
    public ObservableCollection<ShortcutRowViewModel> InAppShortcuts { get; } = [];

    /// <summary>The current summon hotkey as a display string (e.g. <c>Ctrl+Alt+D</c>).</summary>
    [ObservableProperty]
    private string _summonHotkeyDisplay = ShortcutSettings.SummonHotkeyDefault;

    /// <summary>True while the UI is waiting for the user to press a new chord.</summary>
    [ObservableProperty]
    private bool _isCapturingHotkey;

    /// <summary>Transient guidance/error under the hotkey row (capturing prompt, conflict, invalid).</summary>
    [ObservableProperty]
    private string? _hotkeyStatusText;

    /// <summary>Enters summon-hotkey capture; the view records the next chord.</summary>
    [RelayCommand]
    private void StartHotkeyCapture()
    {
        ClearCaptureState();
        IsCapturingHotkey = true;
        HotkeyStatusText = Localization.AppStrings.HotkeyCapturing;
    }

    /// <summary>Restores the default summon hotkey.</summary>
    [RelayCommand]
    private async Task ResetSummonHotkeyAsync()
    {
        IsCapturingHotkey = false;
        if (Hotkey.TryParse(ShortcutSettings.SummonHotkeyDefault, out Hotkey fallback))
        {
            await ApplySummonHotkeyAsync(fallback).ConfigureAwait(true);
        }
    }

    // ── Unified capture surface used by the settings view (summon row OR an in-app row) ──

    /// <summary>True while any shortcut row is waiting for the user to press a chord.</summary>
    public bool IsCapturing => IsCapturingHotkey || _capturingRow is not null;

    /// <summary>Leaves capture mode without changing anything (Escape during capture).</summary>
    public void CancelCapture() => ClearCaptureState();

    /// <summary>Routes a chord captured by the view to whichever row is capturing.</summary>
    public async Task HandleCapturedChordAsync(ModifierKeys modifiers, Key key)
    {
        var hotkey = new Hotkey(modifiers, key);
        if (IsCapturingHotkey)
        {
            await ApplySummonHotkeyAsync(hotkey).ConfigureAwait(true);
        }
        else if (_capturingRow is { } row)
        {
            await ApplyRowHotkeyAsync(row, hotkey).ConfigureAwait(true);
        }
    }

    private void StartRowCapture(ShortcutRowViewModel row)
    {
        ClearCaptureState();
        _capturingRow = row;
        row.IsCapturing = true;
        row.StatusText = Localization.AppStrings.HotkeyCapturing;
    }

    private async Task ResetRowAsync(ShortcutRowViewModel row)
    {
        ClearCaptureState();
        await _shortcuts.ResetAsync(row.Id).ConfigureAwait(true);
        row.Display = _shortcuts.Get(row.Id).ToDisplayString();
        row.StatusText = null;
    }

    private async Task ApplyRowHotkeyAsync(ShortcutRowViewModel row, Hotkey hotkey)
    {
        switch (await _shortcuts.SetAsync(row.Id, hotkey).ConfigureAwait(true))
        {
            case ShortcutSetResult.Ok:
                _capturingRow = null;
                row.IsCapturing = false;
                row.StatusText = null;
                row.Display = hotkey.ToDisplayString();
                break;
            case ShortcutSetResult.Conflict:
                _capturingRow = null;
                row.IsCapturing = false;
                row.StatusText = Localization.AppStrings.HotkeyConflict;
                break;
            default:
                row.StatusText = Localization.AppStrings.HotkeyInvalid;
                break;
        }
    }

    private async Task ApplySummonHotkeyAsync(Hotkey hotkey)
    {
        switch (_hotkeys.TrySet(hotkey))
        {
            case HotkeySetResult.Ok:
                IsCapturingHotkey = false;
                HotkeyStatusText = null;
                SummonHotkeyDisplay = hotkey.ToDisplayString();
                await _settings.SetAsync(ShortcutSettings.SummonHotkeyKey, SummonHotkeyDisplay).ConfigureAwait(true);
                break;
            case HotkeySetResult.Conflict:
                IsCapturingHotkey = false;
                HotkeyStatusText = Localization.AppStrings.HotkeyConflict;
                break;
            default:
                HotkeyStatusText = Localization.AppStrings.HotkeyInvalid;
                break;
        }
    }

    /// <summary>Clears any in-progress capture (summon or a row) so only one is ever active.</summary>
    private void ClearCaptureState()
    {
        IsCapturingHotkey = false;
        HotkeyStatusText = null;
        if (_capturingRow is { } row)
        {
            row.IsCapturing = false;
            row.StatusText = null;
            _capturingRow = null;
        }
    }

    private void RefreshShortcutDisplays()
    {
        foreach (ShortcutRowViewModel row in InAppShortcuts)
        {
            row.Display = _shortcuts.Get(row.Id).ToDisplayString();
        }
    }

    /// <summary>
    /// The privacy statement, assembled from what this build can actually do. It used to be one fixed
    /// sentence claiming Daynote never syncs or sends anything over the network, which stopped being
    /// true twice over: cloud sync uploads notes, and the MCP server hands them to an AI client that
    /// may forward them to its own service. Both are opt-in, but silence about them is still a promise
    /// the app cannot keep - and the Store listing has to match this text.
    /// </summary>
    public string PrivacyText => ComposePrivacyText(cloudSyncAvailable: Account is not null);

    /// <summary>
    /// The sync sentence is omitted when this build has no sync endpoint, because such a build makes no
    /// network calls at all and describing an absent feature would be its own inaccuracy.
    /// </summary>
    public static string ComposePrivacyText(bool cloudSyncAvailable)
    {
        string text = Localization.AppStrings.SettingsPrivacyText
            + " " + Localization.AppStrings.SettingsPrivacyMcp;
        return cloudSyncAvailable ? text + " " + Localization.AppStrings.SettingsPrivacySync : text;
    }

    [ObservableProperty]
    private StartupTaskState _startupState = StartupTaskState.Unavailable;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True only when the app may change the startup task (plain Disabled or Enabled states).</summary>
    public bool StartupToggleEnabled => StartupState is StartupTaskState.Disabled or StartupTaskState.Enabled;

    public bool StartupIsOn => StartupState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>Explicit, non-color status text for user/policy/unavailable startup states.</summary>
    public string StartupStateText => StartupState switch
    {
        StartupTaskState.Enabled => Localization.AppStrings.SettingsStartupEnabledText,
        StartupTaskState.Disabled => Localization.AppStrings.SettingsStartupDisabledText,
        StartupTaskState.DisabledByUser => Localization.AppStrings.SettingsStartupDisabledByUserText,
        StartupTaskState.DisabledByPolicy => Localization.AppStrings.SettingsStartupDisabledByPolicyText,
        StartupTaskState.EnabledByPolicy => Localization.AppStrings.SettingsStartupEnabledByPolicyText,
        _ => Localization.AppStrings.SettingsStartupUnavailableText,
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StartupState = await _startup.GetStateAsync(cancellationToken).ConfigureAwait(true);
        await LoadSummonHotkeyAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Reads the persisted summon hotkey (falling back to the default) and registers it.</summary>
    private async Task LoadSummonHotkeyAsync(CancellationToken cancellationToken)
    {
        string? stored = await _settings.GetAsync(ShortcutSettings.SummonHotkeyKey, cancellationToken).ConfigureAwait(true);
        if (!Hotkey.TryParse(stored, out Hotkey hotkey)
            && !Hotkey.TryParse(ShortcutSettings.SummonHotkeyDefault, out hotkey))
        {
            return;
        }

        _hotkeys.TrySet(hotkey);
        SummonHotkeyDisplay = (_hotkeys.Current ?? hotkey).ToDisplayString();
    }

    [RelayCommand]
    private async Task ToggleStartupAsync()
    {
        if (IsBusy || !StartupToggleEnabled)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StartupEnableResult result = StartupState == StartupTaskState.Enabled
                ? await _startup.RequestDisableAsync().ConfigureAwait(true)
                : await _startup.RequestEnableAsync().ConfigureAwait(true);
            StartupState = result.State;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnStartupStateChanged(StartupTaskState value)
    {
        OnPropertyChanged(nameof(StartupToggleEnabled));
        OnPropertyChanged(nameof(StartupIsOn));
        OnPropertyChanged(nameof(StartupStateText));
    }
}
