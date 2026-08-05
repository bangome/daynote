using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Daynote.UiQa.Automation;

/// <summary>
/// Captures a real product window to a PNG for evidence. It reads the window's bounding rectangle
/// from UI Automation and copies just that on-screen region with GDI, so screenshots contain only
/// the Daynote window and never unrelated desktop content.
/// </summary>
public static class ScreenCapture
{
    public static string CaptureWindow(AutomationElement window, string evidenceDirectory, string name)
    {
        ArgumentNullException.ThrowIfNull(window);
        Directory.CreateDirectory(evidenceDirectory);

        Rect bounds = window.Current.BoundingRectangle;
        int x = (int)Math.Round(bounds.X);
        int y = (int)Math.Round(bounds.Y);
        int width = Math.Max(1, (int)Math.Round(bounds.Width));
        int height = Math.Max(1, (int)Math.Round(bounds.Height));

        string path = Path.Combine(evidenceDirectory, $"{name}.png");
        BitmapSource source = CaptureRegion(x, y, width, height);
        using FileStream stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        return path;
    }

    private static BitmapSource CaptureRegion(int x, int y, int width, int height)
    {
        nint screenDc = Native.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new InvalidOperationException("Could not obtain the screen device context.");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        try
        {
            memoryDc = Native.CreateCompatibleDC(screenDc);
            bitmap = Native.CreateCompatibleBitmap(screenDc, width, height);
            nint previous = Native.SelectObject(memoryDc, bitmap);
            const uint SRCCOPY = 0x00CC0020;
            if (!Native.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
            {
                throw new InvalidOperationException("BitBlt failed while capturing the window region.");
            }

            Native.SelectObject(memoryDc, previous);
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != nint.Zero)
            {
                Native.DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                Native.DeleteDC(memoryDc);
            }

            Native.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern nint GetDC(nint hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint hwnd, nint dc);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleDC(nint dc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(nint dc);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

        [DllImport("gdi32.dll")]
        public static extern nint SelectObject(nint dc, nint handle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(nint handle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            nint destDc,
            int destX,
            int destY,
            int width,
            int height,
            nint sourceDc,
            int sourceX,
            int sourceY,
            uint rasterOperation);
    }
}
