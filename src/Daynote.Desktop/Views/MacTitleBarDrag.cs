using Avalonia.Controls;
using Avalonia.Input;

namespace Daynote.Desktop.Views;

/// <summary>
/// Drag-to-move and double-click-to-zoom for an app-drawn title bar on macOS.
/// </summary>
/// <remarks>
/// With <c>ExtendClientAreaToDecorationsHint</c> the content view covers the whole NSWindow, so the
/// system's own title-bar drag region is gone and the strip the app draws is plain client area. On
/// Windows the <see cref="Avalonia.Controls.Chrome.WindowDecorationsElementRole.TitleBar"/> role hands
/// the strip back to the non-client hit test; on macOS that role is not honoured, so the window has to
/// start the move itself with <see cref="Window.BeginMoveDrag"/>.
/// <para>
/// Buttons and text boxes in the strip mark their pointer-press as handled before it bubbles here, so
/// only presses on the strip's own background, the brand image or a plain label start a drag. A
/// double-click toggles zoom, which is what the system title bar does by default.
/// </para>
/// </remarks>
internal static class MacTitleBarDrag
{
    public static void Attach(Window window, Control titleBar)
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                e.Handled = true;
                return;
            }

            window.BeginMoveDrag(e);
        };
    }
}
