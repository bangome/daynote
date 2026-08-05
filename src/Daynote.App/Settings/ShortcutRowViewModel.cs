using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;

namespace Daynote.App.Settings;

/// <summary>
/// One editable in-app shortcut row: its action label, the current chord display, and a transient
/// capture/conflict status. The change button starts capture (the settings view records the next
/// chord) and the reset button restores the default. The actual apply/reset is delegated back to
/// <see cref="SettingsViewModel"/>.
/// </summary>
public sealed partial class ShortcutRowViewModel : ObservableObject, ILanguageAware
{
    private readonly Action<ShortcutRowViewModel> _onStartCapture;
    private readonly Action<ShortcutRowViewModel> _onReset;

    public ShortcutRowViewModel(
        string id,
        string labelKey,
        string display,
        Action<ShortcutRowViewModel> onStartCapture,
        Action<ShortcutRowViewModel> onReset)
    {
        Id = id;
        LabelKey = labelKey;
        _display = display;
        _onStartCapture = onStartCapture;
        _onReset = onReset;
        LocalizationService.Instance.Observe(this);
    }

    public string Id { get; }

    /// <summary>The action's catalog key; <see cref="Label"/> resolves it in the active language.</summary>
    public string LabelKey { get; }

    public string Label => LocalizationService.Instance[LabelKey];

    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(nameof(Label));

    [ObservableProperty]
    private string _display;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private string? _statusText;

    [RelayCommand]
    private void StartCapture() => _onStartCapture(this);

    [RelayCommand]
    private void Reset() => _onReset(this);
}
