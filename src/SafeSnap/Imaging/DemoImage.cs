namespace SafeSnap.Imaging;

public static class DemoImage
{
    public const int DesignWidth = 880;
    public const int DesignHeight = 500;

    public static WriteableBitmap Create()
    {
        return Create(DesignWidth, DesignHeight);
    }

    public static WriteableBitmap Create(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        const double pixelsPerDip = 96.0;
        var sx = width / (double)DesignWidth;
        var sy = height / (double)DesignHeight;
        var fontScale = Math.Min(sx, sy);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 248, 252)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 58, 112)), null, new Rect(0, 0, width, 78 * sy));

            dc.DrawText(
                Text("SafeSnap demo capture", 28 * fontScale, Brushes.White, pixelsPerDip),
                new Point(28 * sx, 18 * sy));

            dc.DrawText(
                Text(
                    "Quarterly notes for the review packet. Account token and the balance line below are redacted.",
                    18 * fontScale,
                    new SolidColorBrush(Color.FromRgb(32, 40, 52)),
                    pixelsPerDip),
                new Point(28 * sx, 108 * sy));

            dc.DrawText(
                Text("Customer: Northwind Field Office", 16 * fontScale, new SolidColorBrush(Color.FromRgb(32, 40, 52)), pixelsPerDip),
                new Point(28 * sx, 168 * sy));
            dc.DrawText(
                Text("Prepared by: A. Okonkwo", 16 * fontScale, new SolidColorBrush(Color.FromRgb(32, 40, 52)), pixelsPerDip),
                new Point(28 * sx, 196 * sy));

            var secretBounds = new Rect(28 * sx, 250 * sy, 360 * sx, 92 * sy);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 214, 102)), null, secretBounds);
            dc.DrawText(
                Text("SECRET  ACCT-4491-2287", 26 * fontScale, new SolidColorBrush(Color.FromRgb(120, 32, 24)), pixelsPerDip),
                new Point(44 * sx, 274 * sy));

            dc.DrawText(
                Text("The highlighted line is pixelated. The sentences around it stay sharp.", 16 * fontScale, new SolidColorBrush(Color.FromRgb(32, 40, 52)), pixelsPerDip),
                new Point(28 * sx, 370 * sy));
        }

        var rendered = new RenderTargetBitmap(width, height, pixelsPerDip, pixelsPerDip, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var bitmap = ImageUtility.Clone(rendered);
        var region = new Int32Rect(
            (int)Math.Round(28 * sx),
            (int)Math.Round(250 * sy),
            Math.Max(1, (int)Math.Round(360 * sx)),
            Math.Max(1, (int)Math.Round(92 * sy)));
        Pixelator.Pixelate(bitmap, region);
        return bitmap;
    }

    /// <summary>
    /// Adds one highlight and one arrow on top of the pixelated demo capture.
    /// Used by the annotated screenshot mode.
    /// </summary>
    public static void ApplySampleAnnotations(WriteableBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var sx = bitmap.PixelWidth / (double)DesignWidth;
        var sy = bitmap.PixelHeight / (double)DesignHeight;

        var highlight = new Int32Rect(
            (int)Math.Round(24 * sx),
            (int)Math.Round(158 * sy),
            Math.Max(1, (int)Math.Round(340 * sx)),
            Math.Max(1, (int)Math.Round(54 * sy)));
        Annotator.Highlight(bitmap, highlight);

        var start = new Point(760 * sx, 188 * sy);
        var end = new Point(400 * sx, 296 * sy);
        var scale = Math.Max(1d, Math.Min(sx, sy));
        Annotator.DrawArrow(bitmap, start, end, Annotator.Thickness * scale);
    }

    private static FormattedText Text(string value, double size, Brush brush, double pixelsPerDip)
    {
        return new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            pixelsPerDip);
    }
}
