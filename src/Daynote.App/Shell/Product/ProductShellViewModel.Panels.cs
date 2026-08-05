using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The side-panel layout concern of the product shell: the two collapse flags, the titlebar's single
/// both-sidebars toggle, and the width breakpoints that compact the titlebar or auto-collapse the panels.
/// Split from the main partial to keep either file reviewable.
/// </summary>
public sealed partial class ProductShellViewModel
{
    private const double CollapseWidth = 820;

    /// <summary>
    /// Below this width the titlebar simplifies: the app title text hides (glyph stays) and
    /// "오늘로 이동" becomes an icon-only button, leaving room for the shrinking search box.
    /// </summary>
    private const double TitlebarCompactWidth = 900;

    private bool _wasNarrow;

    [ObservableProperty]
    private bool _leftCollapsed;

    [ObservableProperty]
    private bool _rightCollapsed;

    [ObservableProperty]
    private bool _isTitlebarCompact;

    /// <summary>True only when BOTH side panels are collapsed — drives the titlebar toggle's icon
    /// (&lt;&gt; to expand, &gt;&lt; to collapse) so a half-collapsed layout still reads as "expanded".</summary>
    public bool PanelsCollapsed => LeftCollapsed && RightCollapsed;

    /// <summary>
    /// Raised only for EXPLICIT user panel toggles (not width-driven auto-collapse) so the window can
    /// shed/regain exactly the collapsed width, keeping the editor column fixed. Args: (isLeft, nowCollapsed).
    /// </summary>
    public event Action<bool, bool>? PanelUserToggled;

    partial void OnLeftCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(PanelsCollapsed));
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(LeftCollapsedKey, value);
        }
    }

    partial void OnRightCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(PanelsCollapsed));
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(RightCollapsedKey, value);
        }
    }

    [RelayCommand]
    private void ToggleLeft()
    {
        LeftCollapsed = !LeftCollapsed;
        PanelUserToggled?.Invoke(true, LeftCollapsed);
    }

    [RelayCommand]
    private void ToggleRight()
    {
        RightCollapsed = !RightCollapsed;
        PanelUserToggled?.Invoke(false, RightCollapsed);
    }

    /// <summary>
    /// The titlebar's single sidebar toggle: collapses both panels unless both are already collapsed, in
    /// which case it expands both. Each side that actually changes raises <see cref="PanelUserToggled"/>
    /// so the window sheds/regains exactly that panel's width and the editor column stays fixed.
    /// </summary>
    [RelayCommand]
    private void TogglePanels()
    {
        bool collapse = !PanelsCollapsed;
        if (LeftCollapsed != collapse)
        {
            LeftCollapsed = collapse;
            PanelUserToggled?.Invoke(true, collapse);
        }

        if (RightCollapsed != collapse)
        {
            RightCollapsed = collapse;
            PanelUserToggled?.Invoke(false, collapse);
        }
    }

    /// <summary>Expands the right panel if collapsed, through the same user-toggle event so the window
    /// resizes consistently. No-op when it is already open.</summary>
    private void EnsureRightExpanded()
    {
        if (RightCollapsed)
        {
            RightCollapsed = false;
            PanelUserToggled?.Invoke(false, false);
        }
    }

    /// <summary>Reacts to the design's width breakpoint: collapse both panels when narrower than 820 DIP.</summary>
    public void UpdateWidth(double width)
    {
        IsTitlebarCompact = width < TitlebarCompactWidth;

        bool narrow = width < CollapseWidth;
        if (narrow && !_wasNarrow)
        {
            LeftCollapsed = true;
            RightCollapsed = true;
        }

        _wasNarrow = narrow;
    }
}
