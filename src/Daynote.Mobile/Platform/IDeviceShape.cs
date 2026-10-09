using Avalonia;

namespace Daynote.Mobile.Platform;

/// <summary>
/// What a window cannot tell about the device it is on: where a foldable's hinge crosses it, and
/// whether a hardware keyboard is attached. Both change while the app runs - unfolding, docking an
/// iPad on a keyboard - so both come with an event.
/// </summary>
/// <remarks>
/// <para>
/// The layout is chosen by window width alone (Daynote Tablet §00); this only refines it. A hinge
/// moves the two-column split onto the fold so no panel straddles it (Foldables §01: "패널 경계는
/// 힌지를 피해"). A keyboard swaps the @ bar above the soft keyboard for the desktop's popover under
/// the caret (Tablet §02).
/// </para>
/// <para>
/// Android reports the hinge through Jetpack WindowManager's <c>FoldingFeature</c>; iOS has no
/// foldable to report yet. The keyboard is <c>Configuration.Keyboard</c> on Android and
/// <c>GCKeyboard.CoalescedKeyboard</c> on iOS.
/// </para>
/// </remarks>
public interface IDeviceShape
{
    /// <summary>The hinge's bounds in the window, in points, while it divides the window; otherwise null.</summary>
    Rect? Hinge { get; }

    bool HasHardwareKeyboard { get; }

    event EventHandler? Changed;
}
