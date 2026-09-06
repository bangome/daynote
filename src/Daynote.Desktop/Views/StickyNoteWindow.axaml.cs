using Avalonia.Controls;
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
