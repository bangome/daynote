using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daynote.Mobile.Views;

/// <summary>
/// The phone's single view. Everything it does is in the markup; this exists only to settle how the
/// window sits against the system bars.
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Asks the platform to keep the content clear of the status bar, the notch and the gesture bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app does NOT draw edge to edge, and that is a decision rather than an omission. Drawing
    /// under the bars means padding the content by <c>InsetsManager.SafeAreaPadding</c>, and on
    /// Android that value arrives in physical pixels while a <c>Thickness</c> is logical: on a 3x
    /// display the 156-pixel status bar inset became 156 logical units, about 52 points of empty
    /// space too much, and the month header sat a fifth of the way down the screen. Letting the
    /// platform inset the window removes the unit question entirely.
    /// </para>
    /// <para>
    /// A notes app gains nothing from bleeding colour behind the clock, so the trade is free here.
    /// If it is ever wanted, turn the preference back on and divide the inset by
    /// <c>TopLevel.RenderScaling</c> read at apply time, not at attach time, when it is still 1.
    /// </para>
    /// </remarks>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = false;
        }
    }
}
