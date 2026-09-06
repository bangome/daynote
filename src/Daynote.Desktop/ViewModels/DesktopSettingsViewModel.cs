using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.App.Settings;
using Daynote.Core.Backup;
using Daynote.Core.Mcp;
using Daynote.Core.Settings;
using Daynote.Core.Startup;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The Avalonia settings panel: language, open-at-login, the data folder, and Claude MCP registration.
/// Mirrors the matching rows of the WPF <c>SettingsViewModel</c>; shortcuts, backup and the account
/// section follow once their ports are framework-neutral.
/// </summary>
public sealed partial class DesktopSettingsViewModel : ObservableObject, ILanguageAware
{
    private readonly ISettingsStore _settings;
    private readonly IStartupTaskService _startup;
    private readonly IMcpRegistrationService _mcp;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly ConfigurableShortcuts _shortcuts;
    private readonly IBackupService _backup;
    private readonly IBackupArchivePicker _backupPicker;
    private readonly Func<Task<bool>> _flushAsync;
    private readonly Action _requestRestartForRestore;
    private readonly Action<string> _openExternal;

    public DesktopSettingsViewModel(
        ISettingsStore settings,
        IStartupTaskService startup,
        IMcpRegistrationService mcp,
        IGlobalHotkeyService hotkeys,
        ConfigurableShortcuts shortcuts,
        IBackupService backup,
        IBackupArchivePicker backupPicker,
        Func<Task<bool>> flushAsync,
        Action requestRestartForRestore,
        string dataRoot,
        Action<string> openExternal)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _backupPicker = backupPicker ?? throw new ArgumentNullException(nameof(backupPicker));
        _flushAsync = flushAsync ?? throw new ArgumentNullException(nameof(flushAsync));
        _requestRestartForRestore = requestRestartForRestore ?? throw new ArgumentNullException(nameof(requestRestartForRestore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        DataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
        _openExternal = openExternal ?? throw new ArgumentNullException(nameof(openExternal));
        BuildShortcutRows();
        LocalizationService.Instance.Observe(this);
    }

    public string DataRoot { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private StartupTaskState _startupState = StartupTaskState.Unavailable;

    [ObservableProperty]
    private string? _mcpStatusText;

    public bool IsKorean => LocalizationService.Instance.Language == AppLanguage.Korean;

    public bool IsEnglish => LocalizationService.Instance.Language == AppLanguage.English;

    /// <summary>True only when the app may change the login item (plain Disabled or Enabled states).</summary>
    public bool StartupToggleEnabled => StartupState is StartupTaskState.Disabled or StartupTaskState.Enabled;

    public bool StartupIsOn => StartupState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public string StartupStateText => StartupState switch
    {
        StartupTaskState.Enabled => AppStrings.SettingsStartupEnabledText,
        StartupTaskState.Disabled => AppStrings.SettingsStartupDisabledText,
        StartupTaskState.DisabledByUser => AppStrings.SettingsStartupDisabledByUserText,
        StartupTaskState.DisabledByPolicy => AppStrings.SettingsStartupDisabledByPolicyText,
        StartupTaskState.EnabledByPolicy => AppStrings.SettingsStartupEnabledByPolicyText,
        _ => AppStrings.SettingsStartupUnavailableText,
    };

    public bool McpAvailable => _mcp.ServerCommand is not null;

    public string McpCodeCommand => _mcp.ClaudeCodeCommand ?? string.Empty;

    // Catalog labels, re-read wholesale on a language switch (see OnLanguageChanged).
    public string Title => AppStrings.SettingsTitle;
    public string LanguageLabel => AppStrings.SettingsLanguageLabel;
    public string LanguageDesc => AppStrings.SettingsLanguageDesc;
    public string KoreanLabel => AppStrings.LanguageKorean;
    public string EnglishLabel => AppStrings.LanguageEnglish;
    public string StartupLabel => AppStrings.SettingsStartupLabel;
    public string StorageLabel => AppStrings.SettingsStorageLabel;
    public string McpLabel => AppStrings.SettingsMcpLabel;
    public string McpDesc => AppStrings.SettingsMcpDesc;
    public string McpRegister => AppStrings.SettingsMcpRegister;
    public string McpCodeHint => AppStrings.SettingsMcpCodeHint;
    public string McpUnavailable => AppStrings.SettingsMcpUnavailable;
    public string PrivacyLabel => AppStrings.SettingsPrivacyLabel;
    public string PrivacyText => AppStrings.SettingsPrivacyText;
    public string Close => AppStrings.CloseSettings;
    public string TutorialLabel => AppStrings.SettingsTutorialLabel;
    public string TutorialButton => AppStrings.SettingsTutorialButton;

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        StartupState = await _startup.GetStateAsync(cancellationToken).ConfigureAwait(true);

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

    [RelayCommand]
    private Task SelectKorean() => SelectLanguageAsync(AppLanguage.Korean);

    [RelayCommand]
    private Task SelectEnglish() => SelectLanguageAsync(AppLanguage.English);

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

    [RelayCommand]
    private void OpenDataFolder() => _openExternal(DataRoot);

    [RelayCommand]
    private async Task RegisterMcpAsync()
    {
        IsBusy = true;
        try
        {
            McpRegistrationResult result = await _mcp.RegisterClaudeDesktopAsync().ConfigureAwait(true);
            McpStatusText = result.Outcome switch
            {
                McpRegistrationOutcome.Registered => AppStrings.SettingsMcpRegistered,
                McpRegistrationOutcome.AlreadyRegistered => AppStrings.SettingsMcpAlreadyRegistered,
                McpRegistrationOutcome.Unavailable => AppStrings.SettingsMcpUnavailable,
                _ => string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsMcpFailedFormat, result.ConfigPath),
            };
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

    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);
}
