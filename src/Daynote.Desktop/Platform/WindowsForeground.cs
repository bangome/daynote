using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Daynote.Desktop.Platform;

/// <summary>
/// Bringing a summoned window to the front on Windows.
/// </summary>
/// <remarks>
/// Avalonia's <see cref="Window.Activate"/> is not enough here. Windows refuses to let a background
/// process steal the foreground, and a window summoned from the tray is exactly that — measured on
/// 2026-09-07, the global hotkey made the window visible again but left it behind whatever the user
/// was looking at, which is not a summon.
/// <para>
/// The process that just received the hotkey is one of the cases the rule allows through, so
/// <c>SetForegroundWindow</c> is called directly. If Windows still refuses — the exemption has
/// lapsed, or another app holds a foreground lock — the window is at least visible, which is what
/// <see cref="Window.Activate"/> already achieved. So the return value is ignored on purpose: there
/// is no better fallback, and flashing the taskbar button would be a worse one.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsForeground
{
    internal static void Raise(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == nint.Zero)
        {
            return;
        }

        _ = SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
}
