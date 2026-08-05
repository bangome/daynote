using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;

namespace Daynote.App.Settings;

/// <summary>
/// One segment in the settings language row. The label is the language's own name (한국어 / English)
/// rather than a translation of it, so a reader who cannot read the current UI language can still
/// find their own — which is the whole point of the row.
/// </summary>
public sealed partial class LanguageOptionViewModel : ObservableObject
{
    public LanguageOptionViewModel(AppLanguage language, string labelKey, bool isSelected)
    {
        Language = language;
        LabelKey = labelKey;
        _isSelected = isSelected;
    }

    public AppLanguage Language { get; }

    public string LabelKey { get; }

    public string Label => LocalizationService.Instance[LabelKey];

    [ObservableProperty]
    private bool _isSelected;
}
