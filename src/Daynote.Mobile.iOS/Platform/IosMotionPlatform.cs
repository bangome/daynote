using Daynote.Mobile.Platform;
using Daynote.Motion;
using Foundation;
using GameController;
using UIKit;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// Reduced motion and haptics on iOS (motion spec §04, M1-M3): <c>UIAccessibility</c> and the three
/// feedback generators the spec names.
/// </summary>
/// <remarks>
/// The haptics play whatever the motion setting says; the spec leaves them to the system's own
/// haptic switch, which the generators already obey.
/// </remarks>
internal sealed class IosMotionPlatform : IMotionPlatform
{
    public IosMotionPlatform()
    {
        // The centre keeps the observer for the life of the app, which is this object's life too.
        _ = UIApplication.Notifications.ObserveReduceMotionStatusDidChange(
            (_, _) => PreferenceChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool PrefersReducedMotion => UIAccessibility.IsReduceMotionEnabled;

    public event EventHandler? PreferenceChanged;

    public void Play(HapticKind kind)
    {
        switch (kind)
        {
            case HapticKind.Selection:
                using (var selection = new UISelectionFeedbackGenerator())
                {
                    selection.SelectionChanged();
                }

                break;
            case HapticKind.Success:
                using (var notification = new UINotificationFeedbackGenerator())
                {
                    notification.NotificationOccurred(UINotificationFeedbackType.Success);
                }

                break;
            default:
                // iOS 17.5 wants the generator tied to the view it plays for; earlier ones have no
                // such call.
                if (OperatingSystem.IsIOSVersionAtLeast(17, 5))
                {
                    if (KeyView() is { } view)
                    {
                        using UIImpactFeedbackGenerator tied = UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Light, view);
                        tied.ImpactOccurred();
                    }
                }
                else
                {
                    using var impact = new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Light);
                    impact.ImpactOccurred();
                }

                break;
        }
    }

    /// <summary>The key window's root view, which the app's one window always is.</summary>
    private static UIView? KeyView() =>
        UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(static scene => scene.Windows)
            .FirstOrDefault(static window => window.IsKeyWindow)?.RootViewController?.View;
}

/// <summary>
/// The iPad's keyboard (tablet §02): <c>GCKeyboard</c> reports a Magic Keyboard or any other
/// hardware keyboard, and says when one comes or goes. No iOS device has a hinge to report.
/// </summary>
internal sealed class IosDeviceShape : IDeviceShape
{
    public IosDeviceShape()
    {
        _ = NSNotificationCenter.DefaultCenter.AddObserver(
            GCKeyboard.DidConnectNotification, _ => Changed?.Invoke(this, EventArgs.Empty));
        _ = NSNotificationCenter.DefaultCenter.AddObserver(
            GCKeyboard.DidDisconnectNotification, _ => Changed?.Invoke(this, EventArgs.Empty));
    }

    public Avalonia.Rect? Hinge => null;

    public bool HasHardwareKeyboard => GCKeyboard.CoalescedKeyboard is not null;

    public event EventHandler? Changed;
}
