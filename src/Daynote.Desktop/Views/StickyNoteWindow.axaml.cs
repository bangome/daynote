using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Daynote.App.Localization;

namespace Daynote.Desktop.Views;

/// <summary>
/// The post-it: a small always-on-top window over the same note the main editor shows, so typing in
/// either updates both (they bind to one <c>EditorText</c>). Pinning toggles Topmost.
/// </summary>
public partial class StickyNoteWindow : Window
{
    public StickyNoteWindow()
    {
        InitializeComponent();
        UpdatePinTip();

        // macOS draws its traffic lights over the extended client area, so the strip has to start
        // clear of them. Windows draws nothing there and the same inset is just a hole.
        if (OperatingSystem.IsMacOS())
        {
            Layout.Margin = new Thickness(70, 0, 12, 12);
        }
    }

    /// <summary>
    /// Drops the theme's title bar on Windows, the same way <see cref="MainWindow"/> does.
    /// </summary>
    /// <remarks>
    /// <c>ExtendClientAreaToDecorationsHint</c> alone only moves the client area up under the
    /// decorations; the theme still draws a title bar there. This window draws its own row with the
    /// note's name and its pin and close buttons, so the two stacked — two bars, the same title
    /// twice, two close buttons. <c>BorderOnly</c> removes the theme's and keeps the frame, the
    /// shadow and the resize grips.
    /// <para>
    /// Set after the window opens, because <see cref="Window.WindowDecorations"/> only means anything
    /// once the platform implementation exists. The roles are what make the app's own row behave like
    /// a title bar: drag-to-move on the strip, and a real close button. The pin is marked
    /// <see cref="WindowDecorationsElementRole.User"/> or the non-client hit test would swallow its
    /// clicks.
    /// </para>
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        WindowDecorations = WindowDecorations.BorderOnly;
        WindowDecorationProperties.SetElementRole(TitleBarRow, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(PinButton, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.User);
    }

    private void OnTogglePin(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinTip();
    }

    private void UpdatePinTip()
    {
        PinButton.Opacity = Topmost ? 1 : 0.45;
        ToolTip.SetTip(PinButton, Topmost ? AppStrings.UnpinStickyNote : AppStrings.PinStickyNote);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Puts the caret in the body so the quick-note chord lands the user typing immediately.</summary>
    public void FocusBody() => Body.Focus();
}
