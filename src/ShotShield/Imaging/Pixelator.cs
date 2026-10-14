namespace ShotShield.Imaging;

/// <summary>
/// Averages rectangular blocks of a BGRA32 buffer. Blocks are aligned to the
/// region origin and clipped to the region, so pixels outside the region stay untouched.
/// </summary>
public static class Pixelator
{
    public const int DefaultBlockSize = 8;

    public static void Pixelate(byte[] bgra, int width, int height, int stride, Int32Rect region, int blockSize)
    {
        if (bgra is null)
        {
            throw new ArgumentNullException(nameof(bgra));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (blockSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        }

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        if ((long)stride * height > bgra.Length)
        {
            throw new ArgumentException("Buffer is smaller than stride * height.", nameof(bgra));
        }

        var regionLeft = region.X;
        var regionTop = region.Y;
        var regionRight = region.X + region.Width;
        var regionBottom = region.Y + region.Height;

        var clipLeft = Math.Max(0, regionLeft);
        var clipTop = Math.Max(0, regionTop);
        var clipRight = Math.Min(width, regionRight);
        var clipBottom = Math.Min(height, regionBottom);
        if (clipLeft >= clipRight || clipTop >= clipBottom)
        {
            return;
        }

        // Grid origin is the region origin, even when that origin sits outside the bitmap.
        var startBx = (clipLeft - regionLeft) / blockSize;
        var startBy = (clipTop - regionTop) / blockSize;
        var endBx = (clipRight - regionLeft + blockSize - 1) / blockSize;
        var endBy = (clipBottom - regionTop + blockSize - 1) / blockSize;

        for (var by = startBy; by < endBy; by++)
        {
            var blockTop = regionTop + by * blockSize;
            var blockBottom = blockTop + blockSize;
            var y0 = Math.Max(blockTop, clipTop);
            var y1 = Math.Min(blockBottom, clipBottom);
            if (y0 >= y1)
            {
                continue;
            }

            for (var bx = startBx; bx < endBx; bx++)
            {
                var blockLeft = regionLeft + bx * blockSize;
                var blockRight = blockLeft + blockSize;
                var x0 = Math.Max(blockLeft, clipLeft);
                var x1 = Math.Min(blockRight, clipRight);
                if (x0 >= x1)
                {
                    continue;
                }

                long sumB = 0;
                long sumG = 0;
                long sumR = 0;
                long sumA = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    var row = y * stride + x0 * 4;
                    for (var x = x0; x < x1; x++)
                    {
                        sumB += bgra[row];
                        sumG += bgra[row + 1];
                        sumR += bgra[row + 2];
                        sumA += bgra[row + 3];
                        count++;
                        row += 4;
                    }
                }

                var avgB = (byte)((sumB + count / 2) / count);
                var avgG = (byte)((sumG + count / 2) / count);
                var avgR = (byte)((sumR + count / 2) / count);
                var avgA = (byte)((sumA + count / 2) / count);

                for (var y = y0; y < y1; y++)
                {
                    var row = y * stride + x0 * 4;
                    for (var x = x0; x < x1; x++)
                    {
                        bgra[row] = avgB;
                        bgra[row + 1] = avgG;
                        bgra[row + 2] = avgR;
                        bgra[row + 3] = avgA;
                        row += 4;
                    }
                }
            }
        }
    }

    public static void Pixelate(WriteableBitmap bitmap, Int32Rect region)
    {
        Pixelate(bitmap, region, DefaultBlockSize);
    }

    public static void Pixelate(WriteableBitmap bitmap, Int32Rect region, int blockSize)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Pbgra32)
        {
            throw new ArgumentException("WriteableBitmap must be Bgra32 or Pbgra32.", nameof(bitmap));
        }

        if (bitmap.IsFrozen)
        {
            throw new InvalidOperationException("Cannot pixelate a frozen bitmap.");
        }

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = bitmap.BackBufferStride;
        var buffer = new byte[stride * height];

        bitmap.Lock();
        try
        {
            Marshal.Copy(bitmap.BackBuffer, buffer, 0, buffer.Length);
            Pixelate(buffer, width, height, stride, region, blockSize);
            Marshal.Copy(buffer, 0, bitmap.BackBuffer, buffer.Length);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }
}
