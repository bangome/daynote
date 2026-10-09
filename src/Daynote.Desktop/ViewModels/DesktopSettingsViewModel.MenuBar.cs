using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.Core.Settings;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The menu bar's two settings (menu bar design §01, §99): whether the status item is shown, and
/// the chord that opens quick capture from anywhere.
/// </summary>
/// <remarks>
/// The chord is configurable because ⌥⌘Space and Ctrl+Alt+Space are both chords other apps
/// already like (§99) — macOS itself gives ⌥⌘Space to Finder's search window. It is a second chord
/// beside the summon key, never the same one: the service refuses a chord the other already holds.
/// </remarks>
public sealed partial class DesktopSettingsViewModel
{
    /// <summary>Whether the status item is in the menu bar. On unless the user turned it off.</summary>
    [ObservableProperty]
    private bool _menuBarVisible = true;

    [ObservableProperty]
    private string _captureHotkeyDisplay = DefaultCaptureHotkey;

    [ObservableProperty]
    private bool _isCapturingCaptureHotkey;

    [ObservableProperty]
    private string? _captureHotkeyStatusText;

    /// <summary>Mac only: on Windows the tray icon is how a hidden window comes back, so it stays.</summary>
    public static bool ShowsMenuBarRow => OperatingSystem.IsMacOS();

    public string MenuBarLabel => AppStrings.SettingsMenuBarLabel;
    public string MenuBarDesc => AppStrings.SettingsMenuBarDesc;
    public string CaptureHotkeyLabel => AppStrings.SettingsCaptureHotkeyLabel;
    public string CaptureHotkeyDesc => AppStrings.SettingsCaptureHotkeyDesc;

    /// <summary>The platform's default quick-capture chord, as stored.</summary>
    public static string DefaultCaptureHotkey => OperatingSystem.IsMacOS()
        ? ShortcutSettings.CaptureHotkeyDefaultMac
        : ShortcutSettings.CaptureHotkeyDefaultWindows;

    /// <summary>Reads both settings and registers the chord. Called once at start-up.</summary>
    public async Task LoadMenuBarAsync(CancellationToken cancellationToken = default)
    {
        MenuBarVisible = await _settings
            .GetBoolAsync(ShortcutSettings.MenuBarVisibleKey, true, cancellationToken).ConfigureAwait(true);

        string? stored = await _settings.GetAsync(ShortcutSettings.CaptureHotkeyKey, cancellationToken).ConfigureAwait(true);
        if (!Hotkey.TryParse(stored, out Hotkey hotkey)
            && !Hotkey.TryParse(DefaultCaptureHotkey, out hotkey))
        {
            return;
        }

        // Another app (or the summon key) may already hold it. Then nothing is registered, and the
        // row says so rather than showing a chord that does nothing.
        HotkeySetResult result = _hotkeys.TrySetCapture(hotkey);
        if (_hotkeys.CurrentCapture is { } registered)
        {
            CaptureHotkeyDisplay = registered.ToDisplayString();
        }
        else
        {
            CaptureHotkeyDisplay = AppStrings.SettingsCaptureHotkeyNone;
        }

        CaptureHotkeyStatusText = result == HotkeySetResult.Ok ? null : AppStrings.HotkeyConflict;
    }

    [RelayCommand]
    private async Task ToggleMenuBarAsync()
    {
        MenuBarVisible = !MenuBarVisible;
        await _settings.SetBoolAsync(ShortcutSettings.MenuBarVisibleKey, MenuBarVisible).ConfigureAwait(true);
    }

    [RelayCommand]
    private void StartCaptureHotkeyCapture()
    {
        ClearCaptureState();
        IsCapturingCaptureHotkey = true;
        CaptureHotkeyStatusText = AppStrings.HotkeyCapturing;
    }

    [RelayCommand]
    private async Task ResetCaptureHotkeyAsync()
    {
        ClearCaptureState();
        if (Hotkey.TryParse(DefaultCaptureHotkey, out Hotkey fallback))
        {
            await ApplyCaptureHotkeyAsync(fallback).ConfigureAwait(true);
        }
    }

    private async Task ApplyCaptureHotkeyAsync(Hotkey hotkey)
    {
        switch (_hotkeys.TrySetCapture(hotkey))
        {
            case HotkeySetResult.Ok:
                IsCapturingCaptureHotkey = false;
                CaptureHotkeyStatusText = null;
                CaptureHotkeyDisplay = hotkey.ToDisplayString();
                await _settings.SetAsync(ShortcutSettings.CaptureHotkeyKey, CaptureHotkeyDisplay).ConfigureAwait(true);
                break;
            case HotkeySetResult.Conflict:
                IsCapturingCaptureHotkey = false;
                CaptureHotkeyStatusText = AppStrings.HotkeyConflict;
                break;
            default:
                CaptureHotkeyStatusText = AppStrings.HotkeyInvalid;
                break;
        }

        OnPropertyChanged(nameof(IsCapturing));
    }

    partial void OnIsCapturingCaptureHotkeyChanged(bool value) => OnPropertyChanged(nameof(IsCapturing));
}
