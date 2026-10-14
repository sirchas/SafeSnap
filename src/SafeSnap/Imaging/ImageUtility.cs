using System.Drawing;
using System.Drawing.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace SafeSnap.Imaging;

public static class ImageUtility
{
    private static MemoryStream? _clipboardPng;

    public static WriteableBitmap Clone(BitmapSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        return new WriteableBitmap(converted);
    }

    public static WriteableBitmap FromGdiBitmap(DrawingBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        try
        {
            var result = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null);
            result.Lock();
            try
            {
                var rowBytes = bitmap.Width * 4;
                var row = new byte[rowBytes];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowBytes);
                    Marshal.Copy(row, 0, IntPtr.Add(result.BackBuffer, y * result.BackBufferStride), rowBytes);
                }

                result.AddDirtyRect(new Int32Rect(0, 0, bitmap.Width, bitmap.Height));
            }
            finally
            {
                result.Unlock();
            }

            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static WriteableBitmap Crop(WriteableBitmap source, Int32Rect region)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var x0 = Math.Max(0, region.X);
        var y0 = Math.Max(0, region.Y);
        var x1 = Math.Min(source.PixelWidth, region.X + region.Width);
        var y1 = Math.Min(source.PixelHeight, region.Y + region.Height);
        var width = x1 - x0;
        var height = y1 - y0;
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Crop region is empty.", nameof(region));
        }

        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        source.Lock();
        result.Lock();
        try
        {
            var rowBytes = width * 4;
            var row = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                var sourceOffset = (y0 + y) * source.BackBufferStride + x0 * 4;
                Marshal.Copy(IntPtr.Add(source.BackBuffer, sourceOffset), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(result.BackBuffer, y * result.BackBufferStride), rowBytes);
            }

            result.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            result.Unlock();
            source.Unlock();
        }

        return result;
    }

    public static void SavePng(BitmapSource source, string path)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var frozen = FreezeCopy(source);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frozen));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void CopyToClipboard(BitmapSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var frozen = FreezeCopy(source);
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frozen));
        encoder.Save(png);
        png.Position = 0;

        var data = new DataObject();
        data.SetImage(frozen);
        // Outlook, Teams, and Word look for a raw PNG stream under this format name.
        data.SetData("PNG", png, false);
        Clipboard.SetDataObject(data, true);
        _clipboardPng = png;
    }

    public static string PicturesFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "SafeSnap");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string DefaultFileName()
    {
        return $"SafeSnap_{DateTime.Now:yyyy-MM-dd_HHmmss}.png";
    }

    private static BitmapSource FreezeCopy(BitmapSource source)
    {
        var clone = new WriteableBitmap(source);
        clone.Freeze();
        return clone;
    }
}
