using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.App.Settings;
using Daynote.Core.Settings;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The shortcuts section: the system-wide summon chord and the configurable in-app shortcuts, each
/// with a capture mode the window feeds through <see cref="HandleCapturedChordAsync"/>. Same rules as
/// the WPF settings: one capture at a time, conflicts refused, Escape cancels.
/// </summary>
public sealed partial class DesktopSettingsViewModel
{
    private ShortcutRowViewModel? _capturingRow;

    public ObservableCollection<ShortcutRowViewModel> InAppShortcuts { get; } = [];

    [ObservableProperty]
    private string _summonHotkeyDisplay = ShortcutSettings.SummonHotkeyDefault;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private string? _hotkeyStatusText;

    public bool IsCapturing => IsCapturingHotkey || _capturingRow is not null;

    public string ShortcutsLabel => AppStrings.SettingsShortcutsRow;
    public string SummonHotkeyLabel => AppStrings.SettingsSummonHotkeyLabel;
    public string SummonHotkeyDesc => AppStrings.SettingsSummonHotkeyDesc;
    public string InAppShortcutsLabel => AppStrings.SettingsInAppShortcutsLabel;
    public string HotkeyChange => AppStrings.HotkeyChange;
    public string HotkeyReset => AppStrings.HotkeyReset;

    /// <summary>Quick note is a fixed chord (⌥`), shown for discoverability, not editable.</summary>
    public string QuickStickyLabel => AppStrings.ShortcutQuickSticky;

    public string QuickStickyDisplay => new Hotkey(HotkeyModifiers.Alt, HotkeyKey.Oem3).ToDisplayString();

    private void BuildShortcutRows()
    {
        foreach (AppShortcutAction action in _shortcuts.Actions)
        {
            InAppShortcuts.Add(new ShortcutRowViewModel(
                action.Id, action.LabelKey, _shortcuts.Get(action.Id).ToDisplayString(), StartRowCapture, row => _ = ResetRowAsync(row)));
        }

        _shortcuts.Changed += (_, _) => RefreshShortcutDisplays();
    }

    /// <summary>Reads the persisted summon hotkey (falling back to the default) and registers it.</summary>
    public async Task LoadSummonHotkeyAsync(CancellationToken cancellationToken = default)
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
    private void StartHotkeyCapture()
    {
        ClearCaptureState();
        IsCapturingHotkey = true;
        HotkeyStatusText = AppStrings.HotkeyCapturing;
    }

    [RelayCommand]
    private async Task ResetSummonHotkeyAsync()
    {
        ClearCaptureState();
        if (Hotkey.TryParse(ShortcutSettings.SummonHotkeyDefault, out Hotkey fallback))
        {
            await ApplySummonHotkeyAsync(fallback).ConfigureAwait(true);
        }
    }

    /// <summary>Escape or focus loss: leave capture without changing anything.</summary>
    public void CancelCapture() => ClearCaptureState();

    /// <summary>Routes a chord the window captured to whichever row (or the summon key) is capturing.</summary>
    public async Task HandleCapturedChordAsync(HotkeyModifiers modifiers, HotkeyKey key)
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
        row.StatusText = AppStrings.HotkeyCapturing;
        OnPropertyChanged(nameof(IsCapturing));
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
                row.StatusText = AppStrings.HotkeyConflict;
                break;
            default:
                row.StatusText = AppStrings.HotkeyInvalid;
                break;
        }

        OnPropertyChanged(nameof(IsCapturing));
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
                HotkeyStatusText = AppStrings.HotkeyConflict;
                break;
            default:
                HotkeyStatusText = AppStrings.HotkeyInvalid;
                break;
        }

        OnPropertyChanged(nameof(IsCapturing));
    }

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

        OnPropertyChanged(nameof(IsCapturing));
    }

    private void RefreshShortcutDisplays()
    {
        foreach (ShortcutRowViewModel row in InAppShortcuts)
        {
            row.Display = _shortcuts.Get(row.Id).ToDisplayString();
        }
    }

    partial void OnIsCapturingHotkeyChanged(bool value) => OnPropertyChanged(nameof(IsCapturing));
}
