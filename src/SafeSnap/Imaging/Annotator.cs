namespace SafeSnap.Imaging;

/// <summary>
/// Tip sits on the drag end. Left and Right are the base corners, and Base is
/// the midpoint where the shaft meets the head.
/// </summary>
public readonly struct Arrowhead
{
    public Arrowhead(Point tip, Point left, Point right, Point basePoint)
    {
        Tip = tip;
        Left = left;
        Right = right;
        Base = basePoint;
    }

    public Point Tip { get; }

    public Point Left { get; }

    public Point Right { get; }

    public Point Base { get; }
}

/// <summary>
/// Destructive highlight and arrow edits on a straight BGRA32 buffer.
/// Highlight blends a color over the rectangle. Arrows are rendered at image
/// pixel size and composited into the bitmap.
/// </summary>
public static class Annotator
{
    public const double HighlightAlpha = 0.40;

    public const double HeadLengthPerThickness = 4.0;

    public const double HeadHalfWidthPerThickness = 1.75;

    /// <summary>
    /// Fixed arrow shaft thickness (the former Large preset).
    /// </summary>
    public const double Thickness = 8;

    public static Color HighlightColor { get; } = Color.FromRgb(255, 230, 0);

    public static Color ArrowColor { get; } = Color.FromRgb(0xE5, 0x39, 0x35);

    public static Arrowhead CreateArrowhead(Point start, Point end, double thickness)
    {
        if (double.IsNaN(thickness) || double.IsInfinity(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness));
        }

        if (!IsFinite(start) || !IsFinite(end))
        {
            throw new ArgumentException("Arrow points must be finite.");
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            throw new ArgumentException("Arrow length must be greater than zero.");
        }

        var ux = dx / length;
        var uy = dy / length;
        var px = -uy;
        var py = ux;

        var headLength = thickness * HeadLengthPerThickness;
        var headHalfWidth = thickness * HeadHalfWidthPerThickness;
        if (headLength > length)
        {
            var scale = length / headLength;
            headLength = length;
            headHalfWidth *= scale;
        }

        var basePoint = new Point(end.X - ux * headLength, end.Y - uy * headLength);
        var left = new Point(basePoint.X + px * headHalfWidth, basePoint.Y + py * headHalfWidth);
        var right = new Point(basePoint.X - px * headHalfWidth, basePoint.Y - py * headHalfWidth);
        return new Arrowhead(end, left, right, basePoint);
    }

    public static void Highlight(byte[] bgra, int width, int height, int stride, Int32Rect region, Color color, double alpha)
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

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        if ((long)stride * height > bgra.Length)
        {
            throw new ArgumentException("Buffer is smaller than stride * height.", nameof(bgra));
        }

        if (double.IsNaN(alpha) || double.IsInfinity(alpha) || alpha < 0 || alpha > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha));
        }

        if (alpha == 0)
        {
            return;
        }

        var clipLeft = Math.Max(0, region.X);
        var clipTop = Math.Max(0, region.Y);
        var clipRight = Math.Min(width, region.X + region.Width);
        var clipBottom = Math.Min(height, region.Y + region.Height);
        if (clipLeft >= clipRight || clipTop >= clipBottom)
        {
            return;
        }

        var srcB = color.B;
        var srcG = color.G;
        var srcR = color.R;
        var inv = 1.0 - alpha;

        for (var y = clipTop; y < clipBottom; y++)
        {
            var row = y * stride + clipLeft * 4;
            for (var x = clipLeft; x < clipRight; x++)
            {
                var destB = bgra[row];
                var destG = bgra[row + 1];
                var destR = bgra[row + 2];
                var destAByte = bgra[row + 3];
                var destA = destAByte / 255.0;
                var outA = alpha + destA * inv;
                if (outA <= 0)
                {
                    bgra[row] = 0;
                    bgra[row + 1] = 0;
                    bgra[row + 2] = 0;
                    bgra[row + 3] = 0;
                }
                else
                {
                    // Straight-alpha source-over. Opaque pixels simplify to
                    // dest * (1 - alpha) + color * alpha, which keeps text readable.
                    bgra[row] = RoundChannel((srcB * alpha + destB * destA * inv) / outA);
                    bgra[row + 1] = RoundChannel((srcG * alpha + destG * destA * inv) / outA);
                    bgra[row + 2] = RoundChannel((srcR * alpha + destR * destA * inv) / outA);
                    bgra[row + 3] = RoundChannel(outA * 255.0);
                }

                row += 4;
            }
        }
    }

    public static void Highlight(WriteableBitmap bitmap, Int32Rect region)
    {
        Highlight(bitmap, region, HighlightColor, HighlightAlpha);
    }

    public static void Highlight(WriteableBitmap bitmap, Int32Rect region, Color color, double alpha)
    {
        EditBuffer(bitmap, (buffer, width, height, stride) => Highlight(buffer, width, height, stride, region, color, alpha));
    }

    public static void DrawArrow(WriteableBitmap bitmap, Point start, Point end)
    {
        DrawArrow(bitmap, start, end, Thickness);
    }

    public static void DrawArrow(WriteableBitmap bitmap, Point start, Point end, double thickness)
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
            throw new InvalidOperationException("Cannot draw on a frozen bitmap.");
        }

        if (double.IsNaN(thickness) || double.IsInfinity(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness));
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx * dx + dy * dy < 1e-12)
        {
            return;
        }

        var head = CreateArrowhead(start, end, thickness);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(ArrowColor);
            brush.Freeze();
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Flat,
                LineJoin = PenLineJoin.Miter
            };
            pen.Freeze();

            if (Distance(start, head.Base) >= 0.5)
            {
                dc.DrawLine(pen, start, ShaftEnd(head, thickness));
            }

            var triangle = new StreamGeometry();
            using (var ctx = triangle.Open())
            {
                ctx.BeginFigure(head.Tip, isFilled: true, isClosed: true);
                ctx.LineTo(head.Left, isStroked: true, isSmoothJoin: false);
                ctx.LineTo(head.Right, isStroked: true, isSmoothJoin: false);
            }

            triangle.Freeze();
            dc.DrawGeometry(brush, null, triangle);
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        Composite(bitmap, rendered);
    }

    private static Point ShaftEnd(Arrowhead head, double thickness)
    {
        var dx = head.Tip.X - head.Base.X;
        var dy = head.Tip.Y - head.Base.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            return head.Base;
        }

        var tuck = Math.Min(thickness, length * 0.45);
        return new Point(head.Base.X + dx / length * tuck, head.Base.Y + dy / length * tuck);
    }

    private static void EditBuffer(WriteableBitmap bitmap, Action<byte[], int, int, int> edit)
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
            throw new InvalidOperationException("Cannot edit a frozen bitmap.");
        }

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = bitmap.BackBufferStride;
        var buffer = new byte[stride * height];

        bitmap.Lock();
        try
        {
            Marshal.Copy(bitmap.BackBuffer, buffer, 0, buffer.Length);
            edit(buffer, width, height, stride);
            Marshal.Copy(buffer, 0, bitmap.BackBuffer, buffer.Length);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    private static void Composite(WriteableBitmap bitmap, BitmapSource source)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var srcStride = width * 4;
        var src = new byte[srcStride * height];
        source.CopyPixels(src, srcStride, 0);

        var destIsPremultiplied = bitmap.Format == PixelFormats.Pbgra32;
        var destStride = bitmap.BackBufferStride;
        var dest = new byte[destStride * height];

        bitmap.Lock();
        try
        {
            Marshal.Copy(bitmap.BackBuffer, dest, 0, dest.Length);
            for (var y = 0; y < height; y++)
            {
                var sRow = y * srcStride;
                var dRow = y * destStride;
                for (var x = 0; x < width; x++)
                {
                    var s = sRow + x * 4;
                    var sa = src[s + 3];
                    if (sa == 0)
                    {
                        continue;
                    }

                    var d = dRow + x * 4;
                    if (sa == 255 && !destIsPremultiplied)
                    {
                        dest[d] = src[s];
                        dest[d + 1] = src[s + 1];
                        dest[d + 2] = src[s + 2];
                        dest[d + 3] = 255;
                        continue;
                    }

                    if (destIsPremultiplied)
                    {
                        var inv = 255 - sa;
                        dest[d] = PremultiplyBlend(src[s], dest[d], inv);
                        dest[d + 1] = PremultiplyBlend(src[s + 1], dest[d + 1], inv);
                        dest[d + 2] = PremultiplyBlend(src[s + 2], dest[d + 2], inv);
                        dest[d + 3] = PremultiplyBlend(sa, dest[d + 3], inv);
                        continue;
                    }

                    BlendStraight(dest, d, src[s], src[s + 1], src[s + 2], sa);
                }
            }

            Marshal.Copy(dest, 0, bitmap.BackBuffer, dest.Length);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    private static void BlendStraight(byte[] dest, int d, byte srcB, byte srcG, byte srcR, byte srcA)
    {
        var destA = dest[d + 3];
        var inv = 255 - srcA;
        var outA = srcA + (destA * inv + 127) / 255;
        if (outA <= 0)
        {
            dest[d] = 0;
            dest[d + 1] = 0;
            dest[d + 2] = 0;
            dest[d + 3] = 0;
            return;
        }

        if (outA > 255)
        {
            outA = 255;
        }

        dest[d] = Unpremultiply(srcB, dest[d], destA, inv, outA);
        dest[d + 1] = Unpremultiply(srcG, dest[d + 1], destA, inv, outA);
        dest[d + 2] = Unpremultiply(srcR, dest[d + 2], destA, inv, outA);
        dest[d + 3] = (byte)outA;
    }

    private static byte Unpremultiply(byte srcPremultiplied, byte destStraight, int destA, int inv, int outA)
    {
        var destPremultiplied = (destStraight * destA + 127) / 255;
        var outPremultiplied = srcPremultiplied + (destPremultiplied * inv + 127) / 255;
        if (outPremultiplied <= 0 || outA <= 0)
        {
            return 0;
        }

        if (outPremultiplied > 255)
        {
            outPremultiplied = 255;
        }

        var straight = (outPremultiplied * 255 + outA / 2) / outA;
        if (straight > 255)
        {
            return 255;
        }

        return (byte)straight;
    }

    private static byte PremultiplyBlend(byte source, byte destination, int inverseAlpha)
    {
        var blended = source + (destination * inverseAlpha + 127) / 255;
        if (blended > 255)
        {
            return 255;
        }

        if (blended < 0)
        {
            return 0;
        }

        return (byte)blended;
    }

    private static byte RoundChannel(double value)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        if (value >= 255)
        {
            return 255;
        }

        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static bool IsFinite(Point point)
    {
        return !double.IsNaN(point.X)
            && !double.IsNaN(point.Y)
            && !double.IsInfinity(point.X)
            && !double.IsInfinity(point.Y);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
