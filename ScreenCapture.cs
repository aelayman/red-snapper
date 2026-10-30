using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RedSnapper;

internal static class ScreenCapture
{
    public static (int X, int Y, int W, int H) VirtualScreen() => (
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

    // Visible top-level windows in z-order (front first), in screen pixels.
    public static System.Collections.Generic.List<System.Windows.Rect> VisibleWindows()
    {
        var list = new System.Collections.Generic.List<System.Windows.Rect>();
        uint self = (uint)Environment.ProcessId;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
            if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            NativeMethods.RECT r;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0
                && !NativeMethods.GetWindowRect(hwnd, out r)) return true;
            if (r.Right - r.Left < 20 || r.Bottom - r.Top < 20) return true;
            list.Add(new System.Windows.Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static BitmapSource Capture(int x, int y, int w, int h)
    {
        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null,
                data.Scan0, data.Stride * h, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
