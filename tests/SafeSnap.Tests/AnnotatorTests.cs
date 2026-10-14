using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SafeSnap.Imaging;
using Xunit;

namespace SafeSnap.Tests;

public class AnnotatorTests
{
    private static readonly Color Yellow = Color.FromRgb(255, 230, 0);

    [Fact]
    public void HighlightMovesInsidePixelsTowardYellowAndLeavesOutsideAlone()
    {
        const int width = 12;
        const int height = 8;
        var original = Paint(width, height, out var stride);
        var buffer = (byte[])original.Clone();
        var region = new Int32Rect(2, 1, 5, 3);

        Annotator.Highlight(buffer, width, height, stride, region, Yellow, 0.40);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x >= 2 && x < 7 && y >= 1 && y < 4;
                var before = Read(original, stride, x, y);
                var after = Read(buffer, stride, x, y);
                if (!inside)
                {
                    Assert.Equal(before, after);
                    continue;
                }

                Assert.True(DistanceToYellow(after) < DistanceToYellow(before));
                Assert.True(after.R > before.R || after.G > before.G);
            }
        }
    }

    [Fact]
    public void OpaqueFortyPercentYellowMatchesSourceOver()
    {
        const int stride = 16;
        var buffer = new byte[stride];
        Set(buffer, stride, 0, 0, 0, 0, 0, 255);
        Set(buffer, stride, 1, 0, 10, 20, 30, 255);
        Set(buffer, stride, 2, 0, 1, 1, 1, 255);
        Set(buffer, stride, 3, 0, 255, 255, 255, 255);

        Annotator.Highlight(buffer, 4, 1, stride, new Int32Rect(0, 0, 4, 1), Yellow, 0.40);

        AssertPixel(buffer, stride, 0, 0, 0, 92, 102, 255);
        AssertPixel(buffer, stride, 1, 0, 6, 104, 120, 255);
        AssertPixel(buffer, stride, 2, 0, 1, 93, 103, 255);
        AssertPixel(buffer, stride, 3, 0, 153, 245, 255, 255);
    }

    [Fact]
    public void PartialDestinationAlphaUsesSourceOver()
    {
        const int stride = 8;
        var buffer = new byte[stride];
        Set(buffer, stride, 0, 0, 10, 20, 30, 128);
        Set(buffer, stride, 1, 0, 9, 9, 9, 128);

        Annotator.Highlight(buffer, 2, 1, stride, new Int32Rect(0, 0, 1, 1), Yellow, 0.40);

        // da = 128/255, srcA = 0.4
        // outA = 0.7011764705882353 -> 179
        // outB = 4.295 -> 4, outG = 139.799 -> 140, outR = 158.356 -> 158
        AssertPixel(buffer, stride, 0, 0, 4, 140, 158, 179);
        AssertPixel(buffer, stride, 1, 0, 9, 9, 9, 128);
    }

    [Fact]
    public void BlendMidpointRoundsAwayFromZero()
    {
        const int stride = 4;
        var buffer = new byte[stride];
        Set(buffer, stride, 0, 0, 0, 0, 0, 255);

        Annotator.Highlight(buffer, 1, 1, stride, new Int32Rect(0, 0, 1, 1), Color.FromRgb(0, 0, 5), 0.5);

        // 5 * 0.5 = 2.5, which rounds away from zero to 3. Alpha stays opaque.
        AssertPixel(buffer, stride, 0, 0, 3, 0, 0, 255);
    }

    [Fact]
    public void ZeroAlphaLeavesEveryByteAlone()
    {
        var original = Paint(6, 4, out var stride);
        var buffer = (byte[])original.Clone();
        Annotator.Highlight(buffer, 6, 4, stride, new Int32Rect(0, 0, 6, 4), Yellow, 0);
        Assert.Equal(original, buffer);
    }

    [Fact]
    public void FullAlphaReplacesRgbAndBecomesOpaque()
    {
        const int stride = 8;
        var buffer = new byte[stride * 2];
        Set(buffer, stride, 0, 0, 8, 8, 8, 128);
        Set(buffer, stride, 1, 0, 1, 2, 3, 255);

        Annotator.Highlight(buffer, 2, 1, stride, new Int32Rect(0, 0, 1, 1), Yellow, 1);

        AssertPixel(buffer, stride, 0, 0, 0, 230, 255, 255);
        AssertPixel(buffer, stride, 1, 0, 1, 2, 3, 255);
    }

    [Fact]
    public void HighlightClipsToTheBitmapAndIgnoresStridePadding()
    {
        const int width = 5;
        const int height = 3;
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
                Set(buffer, stride, x, y, 20, 30, 40, 255);
            }
        }

        var original = (byte[])buffer.Clone();
        Annotator.Highlight(buffer, width, height, stride, new Int32Rect(-2, -1, 4, 3), Yellow, 0.40);

        AssertPixel(buffer, stride, 0, 0, 12, 110, 126, 255);
        AssertPixel(buffer, stride, 1, 1, 12, 110, 126, 255);
        Assert.Equal(Read(original, stride, 2, 0), Read(buffer, stride, 2, 0));
        Assert.Equal(Read(original, stride, 0, 2), Read(buffer, stride, 0, 2));

        for (var y = 0; y < height; y++)
        {
            for (var pad = width * 4; pad < stride; pad++)
            {
                Assert.Equal(0xCD, buffer[y * stride + pad]);
            }
        }
    }

    [Fact]
    public void NonOverlappingHighlightIsANoOp()
    {
        var original = Paint(8, 8, out var stride);
        var buffer = (byte[])original.Clone();
        Annotator.Highlight(buffer, 8, 8, stride, new Int32Rect(-20, -20, 4, 4), Yellow, 0.40);
        Annotator.Highlight(buffer, 8, 8, stride, new Int32Rect(30, 1, 5, 5), Yellow, 0.40);
        Annotator.Highlight(buffer, 8, 8, stride, new Int32Rect(1, 1, 0, 4), Yellow, 0.40);
        Assert.Equal(original, buffer);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AlphaOutsideZeroToOneThrows(double alpha)
    {
        var buffer = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Annotator.Highlight(buffer, 2, 2, 8, new Int32Rect(0, 0, 2, 2), Yellow, alpha));
    }

    [Fact]
    public void WriteableBitmapHighlightMatchesTheBufferMethod()
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
                var region = new Int32Rect(1, 1, 4, 2);
                Annotator.Highlight(expected, width, height, stride, region, Annotator.HighlightColor, Annotator.HighlightAlpha);
                Annotator.Highlight(bitmap, region);

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

    [Theory]
    [InlineData(ArrowSize.Small, 3)]
    [InlineData(ArrowSize.Medium, 5)]
    [InlineData(ArrowSize.Large, 8)]
    public void ArrowThicknessPresetsAreThreeFiveAndEight(ArrowSize size, double thickness)
    {
        Assert.Equal(thickness, Annotator.Thickness(size));
    }

    [Theory]
    [InlineData(0, 0, 120, 0)]
    [InlineData(120, 40, 10, 40)]
    [InlineData(30, 10, 30, 140)]
    [InlineData(30, 140, 30, 10)]
    [InlineData(8, 12, 90, 70)]
    public void ArrowheadTipIsTheEndAndTheHeadFollowsTheDrag(double x0, double y0, double x1, double y1)
    {
        var start = new Point(x0, y0);
        var end = new Point(x1, y1);
        var head = Annotator.CreateArrowhead(start, end, 5);

        Assert.Equal(end.X, head.Tip.X, 6);
        Assert.Equal(end.Y, head.Tip.Y, 6);

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var hx = head.Tip.X - head.Base.X;
        var hy = head.Tip.Y - head.Base.Y;
        Assert.Equal(0, dx * hy - dy * hx, 4);
        Assert.True(dx * hx + dy * hy > 0);

        Assert.Equal(head.Base.X, (head.Left.X + head.Right.X) / 2, 5);
        Assert.Equal(head.Base.Y, (head.Left.Y + head.Right.Y) / 2, 5);

        var area = (head.Left.X - head.Tip.X) * (head.Right.Y - head.Tip.Y)
            - (head.Left.Y - head.Tip.Y) * (head.Right.X - head.Tip.X);
        Assert.NotEqual(0, area);
    }

    [Fact]
    public void ArrowheadScalesInProportionToThickness()
    {
        var start = new Point(0, 0);
        var end = new Point(200, 0);
        var small = Annotator.CreateArrowhead(start, end, 3);
        var large = Annotator.CreateArrowhead(start, end, 8);

        var smallLength = small.Tip.X - small.Base.X;
        var largeLength = large.Tip.X - large.Base.X;
        var smallWidth = Math.Abs(small.Left.Y - small.Right.Y);
        var largeWidth = Math.Abs(large.Left.Y - large.Right.Y);

        Assert.Equal(8.0 / 3.0, largeLength / smallLength, 6);
        Assert.Equal(8.0 / 3.0, largeWidth / smallWidth, 6);
        Assert.Equal(3 * Annotator.HeadLengthPerThickness, smallLength, 6);
        Assert.Equal(3 * Annotator.HeadHalfWidthPerThickness * 2, smallWidth, 6);
        Assert.Equal(small.Tip.Y, small.Base.Y, 6);
        Assert.True(small.Left.Y > small.Tip.Y);
        Assert.True(small.Right.Y < small.Tip.Y);
    }

    [Fact]
    public void ShortDragShrinksTheHeadSoItStaysOnTheSegment()
    {
        var head = Annotator.CreateArrowhead(new Point(0, 0), new Point(10, 0), 8);
        var actual = head.Tip.X - head.Base.X;
        var natural = 8 * Annotator.HeadLengthPerThickness;

        Assert.Equal(10, actual, 6);
        Assert.True(actual < natural);
        Assert.Equal(0, head.Base.X, 6);
        Assert.Equal(10, head.Tip.X, 6);
        Assert.True(Math.Abs(head.Left.Y - head.Right.Y) < 8 * Annotator.HeadHalfWidthPerThickness * 2);
    }

    [Fact]
    public void ArrowheadRejectsAZeroLengthDrag()
    {
        Assert.Throws<ArgumentException>(() =>
            Annotator.CreateArrowhead(new Point(4, 4), new Point(4, 4), 5));
    }

    private static int DistanceToYellow((byte B, byte G, byte R, byte A) pixel)
    {
        var db = pixel.B - Yellow.B;
        var dg = pixel.G - Yellow.G;
        var dr = pixel.R - Yellow.R;
        return db * db + dg * dg + dr * dr;
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
                    (byte)(3 * x + 5 * y + 11),
                    (byte)(7 * y + x + 13),
                    (byte)(4 * x + 2 * y + 17),
                    255);
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
}
