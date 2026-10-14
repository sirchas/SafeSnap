using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingSize = System.Drawing.Size;

namespace ShotShield.Capture;

public sealed class CapturedScreen : IDisposable
{
    public CapturedScreen(DrawingBitmap bitmap, int left, int top)
    {
        Bitmap = bitmap;
        Left = left;
        Top = top;
    }

    public DrawingBitmap Bitmap { get; }

    public int Left { get; }

    public int Top { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

public static class ScreenCapture
{
    /// <summary>
    /// Grabs the virtual desktop in physical pixels. The process is PerMonitorV2 aware,
    /// so <see cref="SystemInformation.VirtualScreen"/> and the screen DC share that coordinate space.
    /// </summary>
    public static CapturedScreen CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The virtual screen is empty.");
        }

        var bitmap = CaptureWithBitBlt(bounds) ?? CaptureWithCopyFromScreen(bounds);
        return new CapturedScreen(bitmap, bounds.Left, bounds.Top);
    }

    private static DrawingBitmap? CaptureWithBitBlt(Rectangle bounds)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            previous = NativeMethods.SelectObject(memDc, hBitmap);
            var copied = NativeMethods.BitBlt(
                memDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDc,
                bounds.Left,
                bounds.Top,
                NativeMethods.SRCCOPY);
            NativeMethods.SelectObject(memDc, previous);
            previous = IntPtr.Zero;
            if (!copied)
            {
                return null;
            }

            // FromHbitmap copies the bits. Delete the GDI object afterwards.
            var bitmap = DrawingBitmap.FromHbitmap(hBitmap);
            NativeMethods.DeleteObject(hBitmap);
            hBitmap = IntPtr.Zero;
            if (bitmap.PixelFormat == DrawingPixelFormat.Format32bppArgb)
            {
                return bitmap;
            }

            var argb = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), DrawingPixelFormat.Format32bppArgb);
            bitmap.Dispose();
            return argb;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero && memDc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDc, previous);
            }

            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static DrawingBitmap CaptureWithCopyFromScreen(Rectangle bounds)
    {
        var bitmap = new DrawingBitmap(bounds.Width, bounds.Height, DrawingPixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            bounds.Left,
            bounds.Top,
            0,
            0,
            new DrawingSize(bounds.Width, bounds.Height),
            CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
