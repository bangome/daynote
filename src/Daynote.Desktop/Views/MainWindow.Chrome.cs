using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;

namespace Daynote.Desktop.Views;

/// <summary>
/// The window's title bar, which is the one part of the shell that cannot be written once for both
/// platforms.
/// </summary>
/// <remarks>
/// macOS puts its traffic lights at the top LEFT and draws them itself, so the app leaves a gap there
/// and adds nothing of its own. Windows puts minimize/maximize/close at the top RIGHT — exactly where
/// this app keeps its own actions — so the two collided, and the theme's drawn title bar printed a
/// second "Daynote" over the app's brand.
/// <para>
/// On Windows the app draws the caption buttons itself rather than leaving them to the theme, so they
/// can match the rest of the shell. That costs nothing in behaviour: Avalonia maps the
/// <see cref="WindowDecorationsElementRole"/> of each button onto the Win32 hit-test codes
/// (<c>HTMINBUTTON</c>, <c>HTMAXBUTTON</c>, <c>HTCLOSE</c>), so Windows still treats them as real
/// caption buttons — Snap Layouts appear on hover over maximize, and the system handles the clicks.
/// The same mechanism marks the strip itself as <see cref="WindowDecorationsElementRole.TitleBar"/>
/// for drag-to-move and double-click-to-maximize.
/// </para>
/// <para>
/// Everything interactive that sits inside that strip — the search box, the account button, the action
/// row — has to be marked <see cref="WindowDecorationsElementRole.User"/>, or the non-client hit test
/// swallows the click before the control sees it.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>The gap macOS needs at the left for its traffic lights.</summary>
    private const double TrafficLightInset = 84;

    /// <summary>The app's own inset when nothing of the system's sits in the strip.</summary>
    private const double PlainInset = 16;

    /// <summary>
    /// Applies the platform's title-bar shape. Called once the window is open, because
    /// <see cref="Window.WindowDecorations"/> is only meaningful after the platform impl exists.
    /// </summary>
    private void ApplyPlatformChrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            // The traffic lights are the system's; leave room and draw nothing.
            TitleBarRow.Margin = new Thickness(TrafficLightInset, 0, PlainInset, 0);
            CaptionButtons.IsVisible = false;
            return;
        }

        // The theme's own title bar would print a second title and a second set of buttons over the
        // app's. BorderOnly drops it and keeps the frame, the shadow and the resize grips.
        WindowDecorations = WindowDecorations.BorderOnly;

        TitleBarRow.Margin = new Thickness(PlainInset, 0, 0, 0);
        CaptionButtons.IsVisible = true;

        WindowDecorationProperties.SetElementRole(TitleBarRow, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(MinimizeButton, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(MaximizeButton, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.CloseButton);

        // Without this the title-bar hit test eats every click in the strip.
        foreach (Control control in new Control[] { BrandArea, TutSearch })
        {
            WindowDecorationProperties.SetElementRole(control, WindowDecorationsElementRole.User);
        }
    }

    /// <summary>A single square while the window can grow; two stacked once it has.</summary>
    private const string MaximizeGeometry = "M0.5,0.5 H9.5 V9.5 H0.5 Z";

    private const string RestoreGeometry = "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z";

    /// <summary>The glyph on the maximize button, which has to follow the window state.</summary>
    private void UpdateMaximizeGlyph() => MaximizeGlyph.Data = Avalonia.Media.Geometry.Parse(
        WindowState == WindowState.Maximized ? RestoreGeometry : MaximizeGeometry);
}
