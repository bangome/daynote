using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Opening and closing the settings dialog.
/// </summary>
/// <remarks>
/// Both ways in tell the dialog it is being shown, because its account page reads the plan from the
/// server and reopening the dialog on the section it was left on changes nothing to react to.
/// </remarks>
public sealed partial class ProductShellViewModel
{
    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        if (IsSettingsOpen)
        {
            SettingsViewModel?.RefreshAccountPage();
        }
    }

    public void OpenSettings()
    {
        IsSettingsOpen = true;
        SettingsViewModel?.RefreshAccountPage();
    }

    public void CloseSettings() => IsSettingsOpen = false;
}
