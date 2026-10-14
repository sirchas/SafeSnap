using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShotShield.Imaging;
using Xunit;

namespace ShotShield.Tests;

public class PixelatorTests
{
    [Fact]
    public void DefaultBlockSizeIsEight()
    {
        Assert.Equal(8, Pixelator.DefaultBlockSize);
    }

    [Fact]
    public void WriteableBitmapConvenienceOverloadUsesDefaultBlockSize()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                const int width = 16;
                const int height = 12;
                var source = Paint(width, height, out var stride);
                var left = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                var right = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                left.WritePixels(new Int32Rect(0, 0, width, height), source, stride, 0);
                right.WritePixels(new Int32Rect(0, 0, width, height), source, stride, 0);

                var region = new Int32Rect(2, 1, 9, 8);
                Pixelator.Pixelate(left, region);
                Pixelator.Pixelate(right, region, Pixelator.DefaultBlockSize);

                var leftBuffer = Copy(left);
                var rightBuffer = Copy(right);
                Assert.Equal(leftBuffer, rightBuffer);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }

    [Fact]
    public void BlocksInsideRegionAreUniformAndOutsidePixelsStayPut()
    {
        const int width = 32;
        const int height = 32;
        var original = Paint(width, height, out var stride);
        var buffer = (byte[])original.Clone();
        var region = new Int32Rect(4, 4, 16, 16);

        Pixelator.Pixelate(buffer, width, height, stride, region, 8);

        AssertUniform(buffer, stride, 4, 4, 12, 12);
        AssertUniform(buffer, stride, 12, 4, 20, 12);
        AssertUniform(buffer, stride, 4, 12, 12, 20);
        AssertUniform(buffer, stride, 12, 12, 20, 20);
        AssertBlocksDiffer(buffer, stride, (4, 4), (12, 4));
        AssertOutsideUnchanged(original, buffer, stride, width, height, region);
    }

    [Fact]
    public void PartialBlocksAreClippedToTheRegion()
    {
        const int width = 20;
        const int height = 20;
        var original = Paint(width, height, out var stride);
        var buffer = (byte[])original.Clone();
        var region = new Int32Rect(1, 1, 10, 10);

        Pixelator.Pixelate(buffer, width, height, stride, region, 8);

        AssertUniform(buffer, stride, 1, 1, 9, 9);
        AssertUniform(buffer, stride, 9, 1, 11, 9);
        AssertUniform(buffer, stride, 1, 9, 9, 11);
        AssertUniform(buffer, stride, 9, 9, 11, 11);
        AssertBlocksDiffer(buffer, stride, (1, 1), (9, 1));
        AssertOutsideUnchanged(original, buffer, stride, width, height, region);

        var edge = Read(buffer, stride, 9, 1);
        Assert.Equal(Average(original, stride, 9, 1, 11, 9), edge);
    }

    [Fact]
    public void RegionThatExtendsOutsideTheBitmapClipsAndKeepsBlockOrigin()
    {
        const int width = 12;
        const int height = 10;
        var original = Paint(width, height, out var stride);
        var buffer = (byte[])original.Clone();
        var region = new Int32Rect(-4, -6, 10, 9);

        Pixelator.Pixelate(buffer, width, height, stride, region, 8);

        AssertUniform(buffer, stride, 0, 0, 4, 2);
        AssertUniform(buffer, stride, 4, 0, 6, 2);
        AssertUniform(buffer, stride, 0, 2, 4, 3);
        AssertUniform(buffer, stride, 4, 2, 6, 3);
        AssertBlocksDiffer(buffer, stride, (0, 0), (4, 0));
        Assert.Equal(Average(original, stride, 0, 0, 4, 2), Read(buffer, stride, 0, 0));
        Assert.Equal(Average(original, stride, 4, 2, 6, 3), Read(buffer, stride, 4, 2));
        AssertOutsideUnchanged(original, buffer, stride, width, height, new Int32Rect(0, 0, 6, 3));
        AssertPixelEqual(original, buffer, stride, 6, 0);
        AssertPixelEqual(original, buffer, stride, 0, 3);
        AssertPixelEqual(original, buffer, stride, 11, 9);
    }

    [Fact]
    public void AverageUsesOnlyPixelsInsideTheClippedBlock()
    {
        const int width = 4;
        const int height = 1;
        const int stride = 16;
        var buffer = new byte[stride * height];
        Set(buffer, stride, 0, 0, 0, 0, 0, 255);
        Set(buffer, stride, 1, 0, 10, 1, 2, 255);
        Set(buffer, stride, 2, 0, 20, 3, 4, 255);
        Set(buffer, stride, 3, 0, 255, 9, 9, 200);

        Pixelator.Pixelate(buffer, width, height, stride, new Int32Rect(1, 0, 2, 1), 8);

        AssertPixel(buffer, stride, 0, 0, 0, 0, 0, 255);
        AssertPixel(buffer, stride, 1, 0, 15, 2, 3, 255);
        AssertPixel(buffer, stride, 2, 0, 15, 2, 3, 255);
        AssertPixel(buffer, stride, 3, 0, 255, 9, 9, 200);
    }

    [Fact]
    public void RoundedAverageRoundsHalfUp()
    {
        const int stride = 8;
        var buffer = new byte[stride];
        Set(buffer, stride, 0, 0, 1, 1, 1, 1);
        Set(buffer, stride, 1, 0, 2, 2, 2, 2);

        Pixelator.Pixelate(buffer, 2, 1, stride, new Int32Rect(0, 0, 2, 1), 4);

        AssertPixel(buffer, stride, 0, 0, 2, 2, 2, 2);
        AssertPixel(buffer, stride, 1, 0, 2, 2, 2, 2);
    }

    [Fact]
    public void StridePaddingIsNotModified()
    {
        const int width = 5;
        const int height = 4;
        const int stride = width * 4 + 8;
        var buffer = new byte[stride * height];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0xCD;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Set(buffer, stride, x, y, (byte)(x + 1), (byte)(y + 3), 40, 255);
            }
        }

        Pixelator.Pixelate(buffer, width, height, stride, new Int32Rect(0, 0, width, height), 3);

        for (var y = 0; y < height; y++)
        {
            for (var pad = width * 4; pad < stride; pad++)
            {
                Assert.Equal(0xCD, buffer[y * stride + pad]);
            }
        }

        AssertUniform(buffer, stride, 0, 0, 3, 3);
        AssertUniform(buffer, stride, 3, 0, 5, 3);
    }

    [Fact]
    public void SecondPassDoesNotChangeAPixelatedRegion()
    {
        var original = Paint(16, 16, out var stride);
        var once = (byte[])original.Clone();
        var region = new Int32Rect(2, 3, 11, 9);
        Pixelator.Pixelate(once, 16, 16, stride, region, 5);
        var twice = (byte[])once.Clone();
        Pixelator.Pixelate(twice, 16, 16, stride, region, 5);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void NonOverlappingRegionLeavesEveryPixelAlone()
    {
        var original = Paint(8, 8, out var stride);
        var buffer = (byte[])original.Clone();
        Pixelator.Pixelate(buffer, 8, 8, stride, new Int32Rect(-20, -20, 4, 4), 8);
        Pixelator.Pixelate(buffer, 8, 8, stride, new Int32Rect(30, 1, 5, 5), 8);
        Pixelator.Pixelate(buffer, 8, 8, stride, new Int32Rect(1, 1, 0, 4), 8);
        Assert.Equal(original, buffer);
    }

    [Fact]
    public void BlockSizeBelowOneThrows()
    {
        var buffer = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pixelator.Pixelate(buffer, 2, 2, 8, new Int32Rect(0, 0, 2, 2), 0));
    }

    [Fact]
    public void WriteableBitmapOverloadMatchesTheBufferMethod()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                const int width = 6;
                const int height = 4;
                var source = Paint(width, height, out var stride);
                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), source, stride, 0);

                var expected = (byte[])source.Clone();
                var region = new Int32Rect(1, 1, 4, 3);
                Pixelator.Pixelate(expected, width, height, stride, region, 3);
                Pixelator.Pixelate(bitmap, region, 3);

                var actualStride = bitmap.BackBufferStride;
                var actual = new byte[actualStride * height];
                bitmap.Lock();
                try
                {
                    Marshal.Copy(bitmap.BackBuffer, actual, 0, actual.Length);
                }
                finally
                {
                    bitmap.Unlock();
                }

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        Assert.Equal(Read(expected, stride, x, y), Read(actual, actualStride, x, y));
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }

    private static byte[] Copy(WriteableBitmap bitmap)
    {
        var stride = bitmap.BackBufferStride;
        var buffer = new byte[stride * bitmap.PixelHeight];
        bitmap.Lock();
        try
        {
            Marshal.Copy(bitmap.BackBuffer, buffer, 0, buffer.Length);
        }
        finally
        {
            bitmap.Unlock();
        }

        return buffer;
    }

    private static byte[] Paint(int width, int height, out int stride)
    {
        stride = width * 4;
        var buffer = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Set(
                    buffer,
                    stride,
                    x,
                    y,
                    (byte)(3 * x + 5 * y + 1),
                    (byte)(7 * y + x + 2),
                    (byte)(11 * x + y + 4),
                    (byte)(200 + ((x + y) % 50)));
            }
        }

        return buffer;
    }

    private static void Set(byte[] buffer, int stride, int x, int y, byte b, byte g, byte r, byte a)
    {
        var index = y * stride + x * 4;
        buffer[index] = b;
        buffer[index + 1] = g;
        buffer[index + 2] = r;
        buffer[index + 3] = a;
    }

    private static (byte B, byte G, byte R, byte A) Read(byte[] buffer, int stride, int x, int y)
    {
        var index = y * stride + x * 4;
        return (buffer[index], buffer[index + 1], buffer[index + 2], buffer[index + 3]);
    }

    private static void AssertPixel(byte[] buffer, int stride, int x, int y, byte b, byte g, byte r, byte a)
    {
        Assert.Equal((b, g, r, a), Read(buffer, stride, x, y));
    }

    private static void AssertPixelEqual(byte[] original, byte[] buffer, int stride, int x, int y)
    {
        Assert.Equal(Read(original, stride, x, y), Read(buffer, stride, x, y));
    }

    private static void AssertUniform(byte[] buffer, int stride, int x0, int y0, int x1, int y1)
    {
        var expected = Read(buffer, stride, x0, y0);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                Assert.Equal(expected, Read(buffer, stride, x, y));
            }
        }
    }

    private static void AssertBlocksDiffer(byte[] buffer, int stride, (int X, int Y) first, (int X, int Y) second)
    {
        Assert.NotEqual(Read(buffer, stride, first.X, first.Y), Read(buffer, stride, second.X, second.Y));
    }

    private static (byte B, byte G, byte R, byte A) Average(byte[] buffer, int stride, int x0, int y0, int x1, int y1)
    {
        long sumB = 0;
        long sumG = 0;
        long sumR = 0;
        long sumA = 0;
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var pixel = Read(buffer, stride, x, y);
                sumB += pixel.B;
                sumG += pixel.G;
                sumR += pixel.R;
                sumA += pixel.A;
                count++;
            }
        }

        return (
            (byte)((sumB + count / 2) / count),
            (byte)((sumG + count / 2) / count),
            (byte)((sumR + count / 2) / count),
            (byte)((sumA + count / 2) / count));
    }

    private static void AssertOutsideUnchanged(byte[] original, byte[] buffer, int stride, int width, int height, Int32Rect region)
    {
        var right = region.X + region.Width;
        var bottom = region.Y + region.Height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x >= region.X && x < right && y >= region.Y && y < bottom;
                if (!inside)
                {
                    AssertPixelEqual(original, buffer, stride, x, y);
                }
            }
        }
    }
}
