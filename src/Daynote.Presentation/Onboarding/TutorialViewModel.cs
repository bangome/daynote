using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.Core.Settings;
using Daynote.Core.Startup;

namespace Daynote.App.Onboarding;

/// <summary>
/// One tutorial card. <paramref name="TargetName"/> is the x:Name of the ProductWindow element to
/// spotlight (null = a centered callout with no highlight). <paramref name="IsShortcuts"/> steps also
/// render the shortcut table.
/// </summary>
public sealed record TutorialStep(string Title, string Body, string? TargetName = null, bool IsShortcuts = false);

/// <summary>x:Names of the ProductWindow elements the tutorial spotlights (kept in sync with ProductWindow.xaml).</summary>
public static class TutorialTargets
{
    public const string Search = "SearchBox";
    public const string Settings = "TutSettings";
    public const string Calendar = "TutCalendar";
    public const string Editor = "TutEditor";
    public const string TabTodo = "TutTabTodo";
    public const string TabFiles = "TutTabFiles";
}

/// <summary>A single shortcut line shown on the shortcuts step.</summary>
public sealed record ShortcutHint(string Label, string Gesture);

/// <summary>
/// The first-run onboarding carousel: a sequence of feature cards (including a shortcuts table) shown
/// once on first launch and re-openable from Settings. Finishing or skipping persists
/// <see cref="OnboardingSettings.CompletedKey"/> so it never auto-shows again. Mirrors the consent
/// overlay pattern (ObservableObject + a <see cref="Resolved"/> event the shell listens to).
/// </summary>
public sealed partial class TutorialViewModel : ObservableObject, ILanguageAware
{
    private readonly ISettingsStore _settings;
    private readonly ConfigurableShortcuts _shortcuts;
    private readonly IStartupTaskService _startup;
    private bool _completed;
    private string _summonGesture = ShortcutSettings.SummonHotkeyDefault;

    public TutorialViewModel(ISettingsStore settings, ConfigurableShortcuts shortcuts, IStartupTaskService startup)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));

        Steps = BuildSteps();
        LocalizationService.Instance.Observe(this);
    }

    /// <summary>
    /// The cards are snapshots of catalog text, so a language switch rebuilds the whole deck rather
    /// than trying to mutate each card in place. <see cref="Index"/> is preserved, so a user who
    /// switches languages mid-tour stays on the card they were reading.
    /// </summary>
    void ILanguageAware.OnLanguageChanged()
    {
        Steps = BuildSteps();
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Shortcuts));
    }

    private static IReadOnlyList<TutorialStep> BuildSteps() =>
        [
            new(AppStrings.TutorialWelcomeTitle, AppStrings.TutorialWelcomeBody),
            new(AppStrings.TutorialNotesTitle, AppStrings.TutorialNotesBody, TutorialTargets.Calendar),
            new(AppStrings.TutorialTodoTitle, AppStrings.TutorialTodoBody, TutorialTargets.TabTodo),
            new(AppStrings.TutorialFilesTitle, AppStrings.TutorialFilesBody, TutorialTargets.TabFiles),
            new(AppStrings.TutorialStickyTitle, AppStrings.TutorialStickyBody, TutorialTargets.Editor),
            new(AppStrings.TutorialSearchTitle, AppStrings.TutorialSearchBody, TutorialTargets.Search),
            new(AppStrings.TutorialShortcutsTitle, AppStrings.TutorialShortcutsBody, IsShortcuts: true),
            new(AppStrings.TutorialWrapTitle, AppStrings.TutorialWrapBody, TutorialTargets.Settings),
            new(AppStrings.TutorialClosingTitle, AppStrings.TutorialClosingBody),
        ];

    /// <summary>Raised after the tutorial is finished or skipped so the shell can dismiss it.</summary>
    public event EventHandler? Resolved;

    public IReadOnlyList<TutorialStep> Steps { get; private set; }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep), nameof(IsFirst), nameof(IsLast), nameof(ProgressText))]
    private int _index;

    public TutorialStep CurrentStep => Steps[Index];

    public bool IsFirst => Index == 0;

    public bool IsLast => Index == Steps.Count - 1;

    public string ProgressText => string.Format(
        CultureInfo.CurrentCulture, AppStrings.TutorialProgressFormat, Index + 1, Steps.Count);

    /// <summary>The global + in-app shortcuts shown on the shortcuts step (current gestures).</summary>
    public IReadOnlyList<ShortcutHint> Shortcuts
    {
        get
        {
            var hints = new List<ShortcutHint>
            {
                new(AppStrings.TutorialShortcutSummon, _summonGesture),
                new(AppStrings.TutorialShortcutQuickSticky, "Alt+`"),
            };
            foreach (AppShortcutAction action in _shortcuts.Actions)
            {
                hints.Add(new ShortcutHint(action.Label, _shortcuts.Get(action.Id).ToDisplayString()));
            }

            return hints;
        }
    }

    /// <summary>OS startup-task state, shown as the "Windows 시작 시 실행" checkbox on the closing card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupIsOn), nameof(StartupToggleEnabled))]
    private StartupTaskState _startupState = StartupTaskState.Unavailable;

    public bool StartupIsOn => StartupState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>Only a plain enabled/disabled task can be toggled here (matches the Settings rule).</summary>
    public bool StartupToggleEnabled => StartupState is StartupTaskState.Disabled or StartupTaskState.Enabled;

    /// <summary>Toggles "start with Windows" from the closing card (opt-in; never overrides user/policy states).</summary>
    [RelayCommand]
    private async Task ToggleStartupAsync()
    {
        if (!StartupToggleEnabled)
        {
            return;
        }

        StartupEnableResult result = StartupState == StartupTaskState.Enabled
            ? await _startup.RequestDisableAsync().ConfigureAwait(true)
            : await _startup.RequestEnableAsync().ConfigureAwait(true);
        StartupState = result.State;
    }

    /// <summary>True when the first-run tutorial has not yet been completed (drives auto-show).</summary>
    public bool ShouldAutoShow => !_completed;

    /// <summary>Loads the completed flag so the app can decide whether to auto-show on first run.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _completed = await _settings.GetBoolAsync(OnboardingSettings.CompletedKey, false, cancellationToken).ConfigureAwait(true);
        string? summon = await _settings.GetAsync(ShortcutSettings.SummonHotkeyKey, cancellationToken).ConfigureAwait(true);
        _summonGesture = Hotkey.TryParse(summon, out Hotkey hotkey)
            ? hotkey.ToDisplayString()
            : ShortcutSettings.SummonHotkeyDefault;
        StartupState = await _startup.GetStateAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the tutorial at the first step (used by both first-run and the Settings re-open).</summary>
    public void Open()
    {
        Index = 0;
        OnPropertyChanged(nameof(Shortcuts));
        IsOpen = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (!IsLast)
        {
            Index++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (!IsFirst)
        {
            Index--;
        }
    }

    [RelayCommand]
    private Task Skip() => CompleteAsync();

    [RelayCommand]
    private Task Finish() => CompleteAsync();

    private async Task CompleteAsync()
    {
        _completed = true;
        IsOpen = false;
        await _settings.SetBoolAsync(OnboardingSettings.CompletedKey, true).ConfigureAwait(true);
        Resolved?.Invoke(this, EventArgs.Empty);
    }
}
