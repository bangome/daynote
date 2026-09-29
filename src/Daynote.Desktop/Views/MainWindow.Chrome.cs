using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The window's own chrome, which is the one part of the shell that cannot be written once for both
/// platforms.
/// </summary>
/// <remarks>
/// There is no title bar in design B: the sidebar runs to the top of the window and the header is the
/// first thing in the main column. macOS still draws its traffic lights at the top left, over the
/// sidebar's brand row, so that row starts clear of them — and when the sidebar is collapsed the
/// header's first button would sit under them instead, so the header takes the inset then. Both rows
/// start a window move when pressed on their background (<see cref="MacTitleBarDrag"/>), which is what
/// the system title bar did.
/// <para>
/// Windows draws minimize / maximize / close at the top right, where the header keeps its actions, so
/// there the main column gets a thin strip of its own with the app-drawn caption buttons. Avalonia maps
/// each button's <see cref="WindowDecorationsElementRole"/> onto the Win32 hit-test codes, so Snap
/// Layouts still open over maximize, and the strip is the <see cref="WindowDecorationsElementRole.TitleBar"/>.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>The gap macOS needs at the left for its traffic lights.</summary>
    private const double TrafficLightInset = 84;

    /// <summary>The header's own left padding, from the design.</summary>
    private const double HeaderInset = 28;

    private DesktopShellViewModel? _chromeShell;

    /// <summary>
    /// Applies the platform's shape. Called once the window is open, because
    /// <see cref="Window.WindowDecorations"/> is only meaningful after the platform impl exists.
    /// </summary>
    private void ApplyPlatformChrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            BrandRow.Margin = new Thickness(TrafficLightInset, BrandRow.Margin.Top, BrandRow.Margin.Right, BrandRow.Margin.Bottom);
            TitleBarRow.IsVisible = false;
            MacTitleBarDrag.Attach(this, BrandRow);
            MacTitleBarDrag.Attach(this, Header);
            ApplyHeaderInset();
            return;
        }

        // The theme's own title bar would print a second title and a second set of buttons over the
        // app's. BorderOnly drops it and keeps the frame, the shadow and the resize grips.
        WindowDecorations = WindowDecorations.BorderOnly;
        TitleBarRow.IsVisible = true;

        // The macOS height centres the traffic lights on the brand row; on Windows the caption strip
        // is 32px, and a taller hint would claim the top of the header as non-client area.
        ExtendClientAreaTitleBarHeightHint = TitleBarRow.Height;

        WindowDecorationProperties.SetElementRole(TitleBarRow, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(MinimizeButton, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(MaximizeButton, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.CloseButton);
    }

    /// <summary>Follows the sidebar's collapse, which decides whether the header sits under the traffic lights.</summary>
    private void AttachChrome(DesktopShellViewModel? shell)
    {
        if (_chromeShell is not null)
        {
            _chromeShell.PropertyChanged -= OnShellPropertyChangedForChrome;
        }

        _chromeShell = shell;
        if (_chromeShell is not null)
        {
            _chromeShell.PropertyChanged += OnShellPropertyChangedForChrome;
        }

        ApplyHeaderInset();
    }

    private void OnShellPropertyChangedForChrome(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopShellViewModel.LeftCollapsed))
        {
            ApplyHeaderInset();
        }
    }

    private void ApplyHeaderInset()
    {
        bool underLights = OperatingSystem.IsMacOS() && _chromeShell is { LeftCollapsed: true };
        Header.Padding = new Thickness(underLights ? TrafficLightInset : HeaderInset, 18, 28, 16);
        UpdateHeaderWrap();
    }

    /// <summary>A single square while the window can grow; two stacked once it has.</summary>
    private const string MaximizeGeometry = "M0.5,0.5 H9.5 V9.5 H0.5 Z";

    private const string RestoreGeometry = "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z";

    /// <summary>The glyph on the maximize button, which has to follow the window state.</summary>
    private void UpdateMaximizeGlyph() => MaximizeGlyph.Data = Avalonia.Media.Geometry.Parse(
        WindowState == WindowState.Maximized ? RestoreGeometry : MaximizeGeometry);
}
