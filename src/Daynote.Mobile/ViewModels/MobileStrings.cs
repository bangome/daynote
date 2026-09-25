using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Bindable view over the static <see cref="AppStrings"/> catalog, the phone's copy of the desktop
/// <c>AppStringsProxy</c>: Avalonia's compiled bindings want an instance path, and one
/// <c>PropertyChanged(string.Empty)</c> re-reads every label after a language switch.
/// </summary>
/// <remarks>
/// Deliberately not shared with the desktop proxy. That one is a fixed list of the labels the desktop
/// window happens to use; this one exposes the catalog by key, which is all the phone views need and
/// means a new label never has to be added in two places.
/// </remarks>
public sealed class MobileStrings : ObservableObject
{
    public static MobileStrings Instance { get; } = new();

    public MobileStrings() => LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

    /// <summary>Any catalog key by name: <c>{Binding Strings[AccountSignInTitle]}</c>.</summary>
    public string this[string key] => LocalizationService.Instance[key];

    /// <summary>
    /// The to-do panel's empty line, which the catalog stores in three pieces so the middle one can
    /// be the literal syntax the user types.
    /// </summary>
    public string TodoEmpty =>
        AppStrings.TodoEmptyPrefix + AppStrings.TodoEmptyCode + AppStrings.TodoEmptySuffix;
}
