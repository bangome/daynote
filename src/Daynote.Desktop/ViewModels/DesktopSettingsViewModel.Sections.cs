using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.Desktop.ViewModels;

/// <summary>The four pages of the settings dialog (Daynote Desktop B), in the order the list shows them.</summary>
public enum SettingsSection
{
    General,
    Shortcuts,
    Data,
    Account,
}

/// <summary>
/// Which page of the settings dialog is showing. The dialog used to be one long scrolling column; the
/// rows are the same, grouped into the design's four sections.
/// </summary>
public sealed partial class DesktopSettingsViewModel
{
    [ObservableProperty]
    private SettingsSection _section = SettingsSection.General;

    public bool IsGeneralSection => Section == SettingsSection.General;

    public bool IsShortcutsSection => Section == SettingsSection.Shortcuts;

    public bool IsDataSection => Section == SettingsSection.Data;

    public bool IsAccountSection => Section == SettingsSection.Account;

    [RelayCommand]
    private void SelectSection(SettingsSection section) => Section = section;

    partial void OnSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsShortcutsSection));
        OnPropertyChanged(nameof(IsDataSection));
        OnPropertyChanged(nameof(IsAccountSection));
    }
}
