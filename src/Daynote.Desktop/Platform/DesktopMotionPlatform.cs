using System.Runtime.InteropServices;
using Daynote.Motion;

namespace Daynote.Desktop.Platform;

/// <summary>
/// Reduced motion on the desktops (motion spec §04): NSWorkspace's
/// <c>accessibilityDisplayShouldReduceMotion</c> on the Mac, the client-area animation setting on
/// Windows. No haptics - the spec gives the desktops none.
/// </summary>
/// <remarks>
/// The setting is read again whenever the window comes back to the front (<see cref="Recheck"/>),
/// which is where someone who just changed it in System Settings returns to. Listening for
/// NSWorkspace's change notification would need an Objective-C block or a registered class, a lot
/// of runtime plumbing for a setting that changes perhaps once.
/// </remarks>
public sealed class DesktopMotionPlatform : IMotionPlatform
{
    private bool _reduced = Read();

    public bool PrefersReducedMotion => _reduced;

    public event EventHandler? PreferenceChanged;

    public void Play(HapticKind kind)
    {
    }

    /// <summary>Reads the setting again and says so if it changed.</summary>
    public void Recheck()
    {
        bool now = Read();
        if (now != _reduced)
        {
            _reduced = now;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool Read()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                IntPtr workspace = Mac.Send(Mac.GetClass("NSWorkspace"), Mac.Selector("sharedWorkspace"));
                return workspace != IntPtr.Zero &&
                       Mac.SendBool(workspace, Mac.Selector("accessibilityDisplayShouldReduceMotion"));
            }

            if (OperatingSystem.IsWindows())
            {
                // SPI_GETCLIENTAREAANIMATION: off is Windows' "Show animations in Windows" unticked.
                return Win.SystemParametersInfo(0x1042, 0, out bool animate, 0) && !animate;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return false;
    }

    private static class Mac
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC, EntryPoint = "objc_getClass")]
        public static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(ObjC, EntryPoint = "sel_registerName")]
        public static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SendBool(IntPtr receiver, IntPtr selector);
    }

    private static class Win
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
    }
}
