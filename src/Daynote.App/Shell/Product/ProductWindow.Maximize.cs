using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Maximizing a <c>WindowStyle=None</c> window: keeping it off the taskbar.
/// </summary>
/// <remarks>
/// A window with the standard frame is maximized by the OS to the monitor's WORK area, which excludes
/// the taskbar. A borderless window is not: Windows sizes it to the whole monitor and lets the frame
/// it thinks is there hang past the edges — except there is no frame, so the bottom of the app ends up
/// underneath the taskbar. The fix is to answer <c>WM_GETMINMAXINFO</c> ourselves with the work area of
/// whichever monitor the window is on.
/// </remarks>
public partial class ProductWindow
{
    /// <summary>Hooks the message pump. Called from <c>OnSourceInitialized</c>, once the handle exists.</summary>
    private void AttachMaximizeFix(nint hwnd) => HwndSource.FromHwnd(hwnd)?.AddHook(MaximizeHook);

    private nint MaximizeHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return nint.Zero;
        }

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return nint.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return nint.Zero;
        }

        Rect work = WithRoomForAnAutoHiddenTaskbar(info);
        MinMaxInfo bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        // Everything here is in physical pixels, in monitor coordinates, and the maximized position is
        // relative to the monitor's own origin — which is not (0,0) on a secondary display.
        bounds.MaxPosition = new Point(work.Left - info.Monitor.Left, work.Top - info.Monitor.Top);
        bounds.MaxSize = new Point(work.Right - work.Left, work.Bottom - work.Top);

        // Without the track size the user can still drag the window taller than the work area.
        bounds.MaxTrackSize = bounds.MaxSize;

        Marshal.StructureToPtr(bounds, lParam, fDeleteOld: false);
        handled = true;
        return nint.Zero;
    }

    /// <summary>
    /// An auto-hiding taskbar leaves the work area covering the whole monitor, so filling it would put
    /// the window edge-to-edge and the taskbar would never slide back out — the mouse can only reach the
    /// hidden bar if something is not already claiming that last pixel. So give the pixel back, but only
    /// on the edge the hidden bar is actually on.
    /// </summary>
    private static Rect WithRoomForAnAutoHiddenTaskbar(MonitorInfo info)
    {
        Rect work = info.Work;
        if ((SHAppBarMessage(AbmGetState, new AppBarData { Size = Marshal.SizeOf<AppBarData>() }) & AbsAutoHide) == 0)
        {
            return work;
        }

        foreach (int edge in new[] { AbeLeft, AbeTop, AbeRight, AbeBottom })
        {
            var query = new AppBarData
            {
                Size = Marshal.SizeOf<AppBarData>(),
                Edge = edge,
                Rect = info.Monitor,
            };

            if (SHAppBarMessage(AbmGetAutoHideBarEx, query) == nint.Zero)
            {
                continue;
            }

            switch (edge)
            {
                case AbeLeft: work.Left++; break;
                case AbeTop: work.Top++; break;
                case AbeRight: work.Right--; break;
                default: work.Bottom--; break;
            }
        }

        return work;
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x0002;
    private const int AbmGetState = 0x0004;
    private const int AbmGetAutoHideBarEx = 0x000B;
    private const int AbsAutoHide = 0x0001;
    private const int AbeLeft = 0;
    private const int AbeTop = 1;
    private const int AbeRight = 2;
    private const int AbeBottom = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Native <c>MINMAXINFO</c>. The reserved first field is unused but has to be present.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public nint Hwnd;
        public uint CallbackMessage;
        public int Edge;
        public Rect Rect;
        public int Param;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHAppBarMessage(int message, in AppBarData data);
}
