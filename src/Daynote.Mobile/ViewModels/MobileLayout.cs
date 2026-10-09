namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Which of the four layouts a window gets (Daynote Tablet §00, Mobile B Foldables §00).
/// </summary>
/// <remarks>
/// Chosen by the window's width, never by the device: a Fold is a phone folded and a tablet
/// unfolded, an iPad in Split View is any of the four, and Stage Manager resizes it at will. The
/// boundaries are the documents' single set, 600 / 840 / 1100 points, which is also where the
/// desktop's own narrow-window rules sit, so a narrow Mac window and an unfolded phone agree.
/// </remarks>
public enum MobileLayout
{
    /// <summary>Under 600: Mobile B as it is, one page under the floating tab bar.</summary>
    Phone,

    /// <summary>600-839: the tab bar becomes a rail, the page a panel, and the note sits beside it.</summary>
    TwoPane,

    /// <summary>840-1099: Desktop B with its sidebar folded away behind a button.</summary>
    TabletCompact,

    /// <summary>1100 and wider: Desktop B, sidebar, day and day panel, with 44-point rows.</summary>
    Tablet,
}

/// <summary>The width rule, on its own so it can be read and tested apart from any window.</summary>
public static class MobileLayouts
{
    public const double TwoPaneWidth = 600;

    public const double TabletCompactWidth = 840;

    public const double TabletWidth = 1100;

    /// <summary>
    /// Below this height the window is a phone on its side, whatever its width: the Foldables
    /// table's cover rule ("높이 &lt; 480pt"), which this app reads as Mobile B since it draws nothing
    /// for a cover screen. An iPhone turned sideways is 874 points wide and 402 high, and a sidebar
    /// and a day panel would leave it a strip of a day in the middle.
    /// </summary>
    public const double MinimumWideHeight = 480;

    public static MobileLayout For(double width, double height = double.PositiveInfinity) => (width, height) switch
    {
        (_, < MinimumWideHeight) => MobileLayout.Phone,
        (>= TabletWidth, _) => MobileLayout.Tablet,
        (>= TabletCompactWidth, _) => MobileLayout.TabletCompact,
        (>= TwoPaneWidth, _) => MobileLayout.TwoPane,
        _ => MobileLayout.Phone,
    };
}
