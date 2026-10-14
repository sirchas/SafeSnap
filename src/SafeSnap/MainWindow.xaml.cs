using SafeSnap.Capture;
using SafeSnap.Hotkey;
using SafeSnap.Imaging;

namespace SafeSnap;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const double ImageCornerRadius = 14;
    private const double WorkspaceMargin = 6;
    private const double MinimumArrowDrag = 5;

    private static readonly SolidColorBrush PixelateFillBrush = FrozenBrush(Color.FromArgb(0x33, 0x3B, 0x9E, 0xFF));
    private static readonly SolidColorBrush PixelateStrokeBrush = FrozenBrush(Color.FromArgb(0xFF, 0x3B, 0x9E, 0xFF));
    private static readonly SolidColorBrush HighlightFillBrush = FrozenBrush(Color.FromArgb(0x66, 0xFF, 0xE6, 0x00));
    private static readonly SolidColorBrush HighlightStrokeBrush = FrozenBrush(Color.FromArgb(0xFF, 0xC4, 0x96, 0x00));

    private readonly EditHistory _history = new();
    private readonly bool _screenshotMode;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;
    private WriteableBitmap? _current;
    private bool _captureInProgress;
    private bool _screenshotStarted;
    private bool _pixelDrag;
    private Point _pixelStart;
    private Point _pixelCurrent;
    private int _delaySeconds;
    private DispatcherTimer? _toastHideTimer;
    private bool _toastClosing;
    private bool _screenshotShadowFallback;
    private bool _arrangingShadow;
    private bool _exitRequested;
    private EditorTool _tool = EditorTool.Pixelate;
    private ArrowSize _arrowSize = ArrowSize.Medium;

    public MainWindow()
    {
        _screenshotMode = App.IsScreenshotMode;
        InitializeComponent();
        if (_screenshotMode)
        {
            ToastRoot.Visibility = Visibility.Collapsed;
            ToastRoot.Opacity = 0;
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
            ApplyOpaqueScreenshotBackdrop();
        }
        else
        {
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica);
        }

        ContentRendered += OnContentRendered;
        Closed += (_, _) =>
        {
            App.ActivationRequested -= ActivateFromExternalRequest;
            _toastHideTimer?.Stop();
            _hotkey?.Dispose();
            _hotkey = null;
            _tray?.Dispose();
            _tray = null;
        };
        UpdateChrome();
        ApplyToolChrome();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_screenshotMode)
        {
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
            ApplyOpaqueScreenshotBackdrop();
            return;
        }

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(new Action(() => _ = BeginCaptureAsync()));
        if (!_hotkey.Attach())
        {
            StatusText.Text = "Ctrl+Shift+S is already registered by another window.";
        }

        _tray = new TrayIcon(RestoreMainWindow, () => _ = BeginCaptureAsync(), ExitFromTray);
        App.ActivationRequested += ActivateFromExternalRequest;
        App.StartActivationListener();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_screenshotMode && !_exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyImage();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveImage();
            e.Handled = true;
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_screenshotStarted || !_screenshotMode)
        {
            return;
        }

        _screenshotStarted = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ = RenderScreenshotWhenIdleAsync()));
    }

    private async Task RenderScreenshotWhenIdleAsync()
    {
        try
        {
            ToastRoot.Visibility = Visibility.Collapsed;
            ToastRoot.Opacity = 0;
            HidePixelateTip();

            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            if (!string.IsNullOrWhiteSpace(App.ScreenshotEmptyPath))
            {
                HidePixelateTip();
                UpdateLayout();
                RenderWindowToPng(App.ScreenshotEmptyPath);
            }

            if (!string.IsNullOrWhiteSpace(App.ScreenshotPath))
            {
                HidePixelateTip();
                var demo = CreateWorkspaceDemoImage();
                DemoImage.ApplySampleAnnotations(demo);
                SetImage(demo, resetHistory: true);
                UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                RenderWindowToPng(App.ScreenshotPath);
            }

            if (!string.IsNullOrWhiteSpace(App.ScreenshotTipPath))
            {
                SetImage(CreateWorkspaceDemoImage(), resetHistory: true);
                ShowPixelateTip(animate: false);
                UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                RenderWindowToPng(App.ScreenshotTipPath);
            }

            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            try
            {
                var path = (App.ScreenshotPath ?? App.ScreenshotTipPath ?? App.ScreenshotEmptyPath) + ".error.txt";
                File.WriteAllText(path, ex.ToString());
            }
            catch (Exception)
            {
                // The process exit code is the signal the caller checks.
            }

            Application.Current.Shutdown(1);
        }
    }

    private void RenderWindowToPng(string path)
    {
        // Mica is not part of the visual tree. An opaque plate keeps the PNG from
        // coming out black or transparent, and the layout root is what actually paints.
        ApplyOpaqueScreenshotBackdrop();
        UpdateImageSlotMargin();
        UpdateLayout();
        LayoutImageShadow();
        UpdateLayout();

        var direct = RenderLayoutRoot(useVisualBrush: false);
        if (IsUsableCapture(direct) && ShadowsLookSoft(direct))
        {
            ImageUtility.SavePng(direct, path);
            return;
        }

        var brushed = RenderLayoutRoot(useVisualBrush: true);
        if (IsUsableCapture(brushed) && ShadowsLookSoft(brushed))
        {
            ImageUtility.SavePng(brushed, path);
            return;
        }

        // Shader effects often do not survive RenderTargetBitmap. The fallback is
        // concentric translucent rounded rects with no hard offset edge.
        EnableScreenshotShadowFallback();
        UpdateLayout();

        var fallback = RenderLayoutRoot(useVisualBrush: true);
        if (!IsUsableCapture(fallback))
        {
            fallback = RenderLayoutRoot(useVisualBrush: false);
        }

        if (!IsUsableCapture(fallback))
        {
            throw new InvalidOperationException("Screenshot render was blank.");
        }

        ImageUtility.SavePng(fallback, path);
    }

    private void ApplyOpaqueScreenshotBackdrop()
    {
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;

        var plate = OpaqueColor("SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush", Color.FromRgb(243, 243, 243));
        var canvas = OpaqueColor("SolidBackgroundFillColorSecondary", "SolidBackgroundFillColorSecondaryBrush", Color.FromRgb(238, 238, 238));
        var card = OpaqueColor("SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush", Color.FromRgb(255, 255, 255));
        Background = FrozenBrush(plate);
        RootLayout.Background = FrozenBrush(plate);
        CanvasBackdrop.Background = FrozenBrush(canvas);
        PixelateTip.Background = FrozenBrush(card);
    }

    private void EnableScreenshotShadowFallback()
    {
        if (_screenshotShadowFallback)
        {
            return;
        }

        _screenshotShadowFallback = true;
        ImageShadow.Effect = null;
        ImageShadowFallback.Visibility = Visibility.Visible;
    }

    private BitmapSource RenderLayoutRoot(bool useVisualBrush)
    {
        var root = RootLayout;
        var dipWidth = Math.Max(1d, root.ActualWidth);
        var dipHeight = Math.Max(1d, root.ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(root);
        var pixelWidth = Math.Max(1, (int)Math.Round(dipWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(dipHeight * dpi.DpiScaleY));
        var target = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        if (!useVisualBrush)
        {
            target.Render(root);
            return target;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, dipWidth, dipHeight);
            context.DrawRectangle((Brush)root.Background ?? Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(root)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds
            }, null, bounds);
        }

        target.Render(visual);
        return target;
    }

    private bool ShadowsLookSoft(BitmapSource source)
    {
        if (ImageSlot.Visibility != Visibility.Visible)
        {
            return true;
        }

        if (ImageShadow.ActualWidth <= 1 || ImageShadow.ActualHeight <= 1)
        {
            return false;
        }

        if (EdgeShadowLooksSoft(source, ImageShadow, sampleBelow: true))
        {
            return true;
        }

        // A 6px vertical margin can clip the downward blur. The side letterbox still shows it.
        return EdgeShadowLooksSoft(source, ImageShadow, sampleBelow: false);
    }

    private bool EdgeShadowLooksSoft(BitmapSource source, FrameworkElement element, bool sampleBelow)
    {
        if (element.ActualWidth < 8 || element.ActualHeight < 8)
        {
            return false;
        }

        GeneralTransform transform;
        try
        {
            transform = element.TransformToAncestor(RootLayout);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var topLeft = transform.Transform(new Point(0, 0));
        var bottomRight = transform.Transform(new Point(element.ActualWidth, element.ActualHeight));
        var pixels = ReadBgra(source);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var scaleX = width / Math.Max(1d, RootLayout.ActualWidth);
        var scaleY = height / Math.Max(1d, RootLayout.ActualHeight);
        var scanY = (topLeft.Y + bottomRight.Y) / 2d;
        if (!TrySurfaceLuminance(pixels, width, height, scaleX, scaleY, sampleBelow, scanY, out var background))
        {
            return false;
        }

        var length = ShadowSampleLength(bottomRight, sampleBelow, scaleX, scaleY, width, height);
        if (length < 6)
        {
            return false;
        }

        var diffs = new double[length];

        if (!sampleBelow)
        {
            var midY = (int)Math.Round((topLeft.Y + bottomRight.Y) / 2d * scaleY);
            var edge = (int)Math.Round(bottomRight.X * scaleX);
            for (var i = 0; i < length; i++)
            {
                var sample = Luminance(pixels, width, height, edge + 3 + i, midY);
                if (sample < 0)
                {
                    return false;
                }

                diffs[i] = background - sample;
            }
        }
        else
        {
            var midX = (int)Math.Round((topLeft.X + bottomRight.X) / 2d * scaleX);
            var edge = (int)Math.Round(bottomRight.Y * scaleY);
            for (var i = 0; i < length; i++)
            {
                var sample = Luminance(pixels, width, height, midX, edge + 3 + i);
                if (sample < 0)
                {
                    return false;
                }

                diffs[i] = background - sample;
            }
        }

        return IsSoftDarkening(diffs);
    }

    private bool TrySurfaceLuminance(
        byte[] pixels,
        int width,
        int height,
        double scaleX,
        double scaleY,
        bool sampleBelow,
        double scanY,
        out int luminance)
    {
        luminance = -1;
        if (CanvasBackdrop.Background is SolidColorBrush surface)
        {
            var color = surface.Color;
            luminance = (color.R * 54 + color.G * 183 + color.B * 19) >> 8;
            return true;
        }

        try
        {
            var clear = sampleBelow
                ? CanvasBackdrop.TransformToAncestor(RootLayout).Transform(new Point(4, 3))
                : CanvasBackdrop.TransformToAncestor(RootLayout).Transform(new Point(2, 8));
            var x = (int)Math.Round(clear.X * scaleX);
            var y = (int)Math.Round((sampleBelow ? clear.Y : scanY) * scaleY);
            luminance = Luminance(pixels, width, height, x, y);
            return luminance >= 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private int ShadowSampleLength(Point edge, bool sampleBelow, double scaleX, double scaleY, int pixelWidth, int pixelHeight)
    {
        const int max = 28;
        const int gap = 3;
        try
        {
            var canvasBottomRight = CanvasBackdrop.TransformToAncestor(RootLayout)
                .Transform(new Point(CanvasBackdrop.ActualWidth, CanvasBackdrop.ActualHeight));
            int edgePx;
            int last;
            if (sampleBelow)
            {
                edgePx = (int)Math.Round(edge.Y * scaleY);
                var canvasPx = (int)Math.Floor(canvasBottomRight.Y * scaleY) - 1;
                last = Math.Min(canvasPx, pixelHeight - 1);
            }
            else
            {
                edgePx = (int)Math.Round(edge.X * scaleX);
                var canvasPx = (int)Math.Floor(canvasBottomRight.X * scaleX) - 1;
                last = Math.Min(canvasPx, pixelWidth - 1);
            }

            var count = last - (edgePx + gap) + 1;
            if (count < 0)
            {
                return 0;
            }

            return Math.Min(max, count);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static bool IsSoftDarkening(double[] diffs)
    {
        var length = diffs.Length;
        var window = length >= 16 ? 8 : Math.Max(2, length / 3);
        var max = 0d;
        var early = 0d;
        var late = 0d;
        var maxStep = 0d;
        var plateau = 0;
        for (var i = 0; i < length; i++)
        {
            if (diffs[i] > max)
            {
                max = diffs[i];
            }

            if (i < window)
            {
                early += diffs[i];
            }

            if (i >= length - window)
            {
                late += diffs[i];
            }

            if (i > 0)
            {
                var step = Math.Abs(diffs[i] - diffs[i - 1]);
                if (step > maxStep)
                {
                    maxStep = step;
                }
            }
        }

        early /= window;
        late /= window;
        var midBand = 0d;
        var midCount = 0;
        var midStart = length >= 16 ? 6 : Math.Max(0, length / 2 - window / 2);
        var midEnd = length >= 16 ? 14 : midStart + window;
        for (var i = midStart; i < midEnd && i < length; i++)
        {
            midBand += diffs[i];
            midCount++;
        }

        if (midCount == 0)
        {
            return false;
        }

        midBand /= midCount;
        // A real blur is still darker well outside the edge. A 1px border is not.
        if (max < 4d || max > 140d || early + 1.5d < late || midBand < 2d)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (diffs[i] > max * 0.75d && diffs[i] > 8d)
            {
                plateau++;
            }
        }

        var plateauLimit = length >= 16 ? 5 : Math.Max(3, length / 3);
        if (plateau >= plateauLimit && maxStep > 14d)
        {
            return false;
        }

        return maxStep < 22d;
    }

    private static bool IsUsableCapture(BitmapSource source)
    {
        if (source.PixelWidth < 32 || source.PixelHeight < 32)
        {
            return false;
        }

        var pixels = ReadBgra(source);
        var count = source.PixelWidth * source.PixelHeight;
        long transparent = 0;
        long lumSum = 0;
        long lumSq = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];
            if (a < 240)
            {
                transparent++;
            }

            var lum = (r * 54 + g * 183 + b * 19) >> 8;
            lumSum += lum;
            lumSq += (long)lum * lum;
        }

        if (transparent * 50 > count)
        {
            return false;
        }

        var mean = lumSum / (double)count;
        var variance = lumSq / (double)count - mean * mean;
        return mean >= 16d && variance >= 12d;
    }

    private static byte[] ReadBgra(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static int Luminance(byte[] pixels, int width, int height, int x, int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return -1;
        }

        var i = (y * width + x) * 4;
        var b = pixels[i];
        var g = pixels[i + 1];
        var r = pixels[i + 2];
        return (r * 54 + g * 183 + b * 19) >> 8;
    }

    private static Color OpaqueColor(string colorKey, string brushKey, Color fallback)
    {
        if (Application.Current.TryFindResource(colorKey) is Color color && color.A == 255)
        {
            return color;
        }

        if (Application.Current.TryFindResource(brushKey) is SolidColorBrush brush && brush.Color.A == 255)
        {
            return brush.Color;
        }

        return fallback;
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _ = BeginCaptureAsync();
    }

    private void DelayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item ||
            !int.TryParse(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), out var seconds))
        {
            return;
        }

        _delaySeconds = Math.Clamp(seconds, 0, 5);
        DelayButton.Content = _delaySeconds.ToString(CultureInfo.InvariantCulture) + " s";
        Delay0Item.IsChecked = _delaySeconds == 0;
        Delay3Item.IsChecked = _delaySeconds == 3;
        Delay5Item.IsChecked = _delaySeconds == 5;
    }

    private async Task BeginCaptureAsync()
    {
        if (_captureInProgress || _screenshotMode)
        {
            return;
        }

        _captureInProgress = true;
        CapturedScreen? captured = null;
        try
        {
            Hide();
            await Task.Delay(TimeSpan.FromMilliseconds(_delaySeconds * 1000 + 300));

            captured = ScreenCapture.CaptureVirtualScreen();
            var shot = ImageUtility.FromGdiBitmap(captured.Bitmap);
            var overlay = new SelectionOverlayWindow(shot, captured.Left, captured.Top, captured.Width, captured.Height);
            overlay.RegionSelected += rect =>
            {
                try
                {
                    SetImage(ImageUtility.Crop(shot, rect), resetHistory: true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "SafeSnap", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            overlay.Closed += (_, _) =>
            {
                _captureInProgress = false;
                captured.Dispose();
                RestoreMainWindow();
            };
            overlay.Show();
        }
        catch (Exception ex)
        {
            captured?.Dispose();
            _captureInProgress = false;
            RestoreMainWindow();
            MessageBox.Show(this, ex.Message, "SafeSnap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        if (_tray is null || AppSettings.HasShownTrayHint())
        {
            return;
        }

        AppSettings.MarkTrayHintShown();
        _tray.ShowFirstHint();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        _tray?.Dispose();
        _tray = null;
        _hotkey?.Dispose();
        _hotkey = null;
        Close();
    }

    internal void ActivateFromExternalRequest()
    {
        if (_screenshotMode || _exitRequested)
        {
            return;
        }

        if (_captureInProgress)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is SelectionOverlayWindow overlay)
                {
                    overlay.Activate();
                    return;
                }
            }
        }

        RestoreMainWindow();
    }

    private void RestoreMainWindow()
    {
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        Activate();
        NativeMethods.SetForegroundWindow(handle);
        Topmost = true;
        Topmost = false;
    }

    private WriteableBitmap CreateWorkspaceDemoImage()
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Floor(Math.Max(0, CanvasArea.ActualWidth - (2 * WorkspaceMargin))));
        var height = Math.Max(1, (int)Math.Floor(Math.Max(0, CanvasArea.ActualHeight - (2 * WorkspaceMargin))));
        return DemoImage.Create(width, height);
    }

    private void SetImage(WriteableBitmap bitmap, bool resetHistory)
    {
        _current = bitmap;
        ImageHost.Width = bitmap.PixelWidth;
        ImageHost.Height = bitmap.PixelHeight;
        CanvasImage.Width = bitmap.PixelWidth;
        CanvasImage.Height = bitmap.PixelHeight;
        CanvasImage.Source = null;
        CanvasImage.Source = bitmap;
        UpdateImageSlotMargin();
        ImageSlot.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ApplyImageClip();
        LayoutImageShadow();
        if (resetHistory)
        {
            _history.Clear();
        }

        UpdateChrome();
        MaybeShowPixelateTip();
    }

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyImageClip();
    }

    private void ImageViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutImageShadow();
        UpdateChrome();
    }

    private void UpdateImageSlotMargin()
    {
        var margin = new Thickness(WorkspaceMargin);
        if (ImageSlot.Margin != margin)
        {
            ImageSlot.Margin = margin;
        }
    }

    private double FittedImageScale()
    {
        if (_current is null || ImageViewbox.ActualWidth <= 0 || ImageViewbox.ActualHeight <= 0)
        {
            return 0;
        }

        var scale = Math.Min(
            ImageViewbox.ActualWidth / _current.PixelWidth,
            ImageViewbox.ActualHeight / _current.PixelHeight);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 0;
        }

        return scale;
    }

    private void LayoutImageShadow()
    {
        if (_arrangingShadow || _current is null || ImageSlot.Visibility != Visibility.Visible)
        {
            return;
        }

        var scale = FittedImageScale();
        if (scale <= 0)
        {
            return;
        }

        var width = _current.PixelWidth * scale;
        var height = _current.PixelHeight * scale;
        var radius = ImageCornerRadius * scale;
        _arrangingShadow = true;
        try
        {
            ImageShadow.Width = width;
            ImageShadow.Height = height;
            ImageShadow.CornerRadius = new CornerRadius(radius);
            ImageShadowFallback.Width = width;
            ImageShadowFallback.Height = height;
        }
        finally
        {
            _arrangingShadow = false;
        }
    }

    private void ApplyImageClip()
    {
        if (ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0)
        {
            return;
        }

        ImageHost.Clip = new RectangleGeometry(
            new Rect(0, 0, ImageHost.ActualWidth, ImageHost.ActualHeight),
            ImageCornerRadius,
            ImageCornerRadius);
    }

    private void MaybeShowPixelateTip()
    {
        // Screenshot modes never consult settings. --screenshot-tip shows the card itself.
        if (_screenshotMode || _current is null || PixelateTipHost.Visibility == Visibility.Visible)
        {
            return;
        }

        if (AppSettings.HasShownPixelateTip())
        {
            return;
        }

        ShowPixelateTip(animate: true);
    }

    private void ShowPixelateTip(bool animate)
    {
        PixelateTipHost.BeginAnimation(OpacityProperty, null);
        PixelateTipSlide.BeginAnimation(TranslateTransform.YProperty, null);
        PixelateTipHost.Visibility = Visibility.Visible;

        if (!animate)
        {
            PixelateTipHost.Opacity = 1;
            PixelateTipSlide.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PixelateTipHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        PixelateTipSlide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private void HidePixelateTip()
    {
        PixelateTipHost.BeginAnimation(OpacityProperty, null);
        PixelateTipSlide.BeginAnimation(TranslateTransform.YProperty, null);
        PixelateTipHost.Opacity = 1;
        PixelateTipSlide.Y = 0;
        PixelateTipHost.Visibility = Visibility.Collapsed;
    }

    private void DismissPixelateTip()
    {
        if (PixelateTipHost.Visibility != Visibility.Visible)
        {
            return;
        }

        HidePixelateTip();
        if (!_screenshotMode)
        {
            AppSettings.MarkPixelateTipShown();
        }
    }

    private void PixelateTipGotIt_Click(object sender, RoutedEventArgs e)
    {
        DismissPixelateTip();
        e.Handled = true;
    }

    private void PixelateTool_Click(object sender, RoutedEventArgs e)
    {
        SetTool(EditorTool.Pixelate);
    }

    private void HighlightTool_Click(object sender, RoutedEventArgs e)
    {
        SetTool(EditorTool.Highlight);
    }

    private void ArrowTool_Click(object sender, RoutedEventArgs e)
    {
        SetTool(EditorTool.Arrow);
    }

    private void ArrowSmallButton_Click(object sender, RoutedEventArgs e)
    {
        SetArrowSize(ArrowSize.Small);
    }

    private void ArrowMediumButton_Click(object sender, RoutedEventArgs e)
    {
        SetArrowSize(ArrowSize.Medium);
    }

    private void ArrowLargeButton_Click(object sender, RoutedEventArgs e)
    {
        SetArrowSize(ArrowSize.Large);
    }

    private void SetTool(EditorTool tool)
    {
        _tool = tool;
        ApplyToolChrome();
        if (_pixelDrag)
        {
            UpdateDragPreview();
        }
    }

    private void SetArrowSize(ArrowSize size)
    {
        _arrowSize = size;
        ApplyToolChrome();
        if (_pixelDrag && _tool == EditorTool.Arrow)
        {
            UpdateDragPreview();
        }
    }

    private void ApplyToolChrome()
    {
        PixelateToolButton.Tag = _tool == EditorTool.Pixelate ? "Selected" : null;
        HighlightToolButton.Tag = _tool == EditorTool.Highlight ? "Selected" : null;
        ArrowToolButton.Tag = _tool == EditorTool.Arrow ? "Selected" : null;
        ArrowSmallButton.Tag = _arrowSize == ArrowSize.Small ? "Selected" : null;
        ArrowMediumButton.Tag = _arrowSize == ArrowSize.Medium ? "Selected" : null;
        ArrowLargeButton.Tag = _arrowSize == ArrowSize.Large ? "Selected" : null;
        ArrowSizeBand.Visibility = _tool == EditorTool.Arrow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        _pixelDrag = true;
        _pixelStart = e.GetPosition(ImageHost);
        _pixelCurrent = _pixelStart;
        ImageHost.CaptureMouse();
        UpdateDragPreview();
        e.Handled = true;
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pixelDrag)
        {
            return;
        }

        _pixelCurrent = e.GetPosition(ImageHost);
        if (Math.Abs(_pixelCurrent.X - _pixelStart.X) >= 1 || Math.Abs(_pixelCurrent.Y - _pixelStart.Y) >= 1)
        {
            DismissPixelateTip();
        }

        UpdateDragPreview();
        e.Handled = true;
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pixelDrag)
        {
            return;
        }

        _pixelCurrent = e.GetPosition(ImageHost);
        FinishDrag();
        e.Handled = true;
    }

    private void ImageHost_LostMouseCapture(object sender, MouseEventArgs e)
    {
        FinishDrag();
    }

    private void FinishDrag()
    {
        if (!_pixelDrag)
        {
            return;
        }

        _pixelDrag = false;
        if (ImageHost.IsMouseCaptured)
        {
            ImageHost.ReleaseMouseCapture();
        }

        HideDragPreview();
        if (_current is null)
        {
            return;
        }

        var bitmap = _current;
        if (_tool == EditorTool.Arrow)
        {
            var start = ClampToImage(_pixelStart);
            var end = ClampToImage(_pixelCurrent);
            if (Distance(start, end) < MinimumArrowDrag)
            {
                return;
            }

            CommitEdit(() => Annotator.DrawArrow(bitmap, start, end, _arrowSize));
            return;
        }

        var rect = PixelRect(_pixelStart, _pixelCurrent);
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        CommitEdit(() =>
        {
            if (_tool == EditorTool.Highlight)
            {
                Annotator.Highlight(bitmap, rect);
            }
            else
            {
                Pixelator.Pixelate(bitmap, rect);
            }
        });
    }

    private void CommitEdit(Action edit)
    {
        if (_current is null)
        {
            return;
        }

        DismissPixelateTip();
        var snapshot = ImageUtility.Clone(_current);
        edit();
        _history.PushUndo(snapshot);
        CanvasImage.Source = null;
        CanvasImage.Source = _current;
        UpdateChrome();
    }

    private void UpdateDragPreview()
    {
        if (_tool == EditorTool.Arrow)
        {
            ShowArrowPreview(ClampToImage(_pixelStart), ClampToImage(_pixelCurrent));
            return;
        }

        HideArrowPreview();
        ShowPixelRect(_pixelStart, _pixelCurrent);
    }

    private void HideDragPreview()
    {
        PixelateRect.Visibility = Visibility.Collapsed;
        HideArrowPreview();
    }

    private void HideArrowPreview()
    {
        ArrowShaftPreview.Visibility = Visibility.Collapsed;
        ArrowHeadPreview.Visibility = Visibility.Collapsed;
    }

    private void ShowArrowPreview(Point start, Point end)
    {
        PixelateRect.Visibility = Visibility.Collapsed;
        if (Distance(start, end) < 1)
        {
            HideArrowPreview();
            return;
        }

        var thickness = Annotator.Thickness(_arrowSize);
        var head = Annotator.CreateArrowhead(start, end, thickness);
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(head.Tip, isFilled: true, isClosed: true);
            ctx.LineTo(head.Left, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(head.Right, isStroked: true, isSmoothJoin: false);
        }

        triangle.Freeze();
        ArrowHeadPreview.Data = triangle;
        ArrowHeadPreview.Visibility = Visibility.Visible;

        if (Distance(start, head.Base) < 0.5)
        {
            ArrowShaftPreview.Visibility = Visibility.Collapsed;
            return;
        }

        ArrowShaftPreview.Data = new LineGeometry(start, PreviewShaftEnd(head, thickness));
        ArrowShaftPreview.StrokeThickness = thickness;
        ArrowShaftPreview.Visibility = Visibility.Visible;
    }

    private static Point PreviewShaftEnd(Arrowhead head, double thickness)
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

    private void ShowPixelRect(Point start, Point end)
    {
        var rect = PixelRect(start, end);
        if (rect.Width < 1 || rect.Height < 1)
        {
            PixelateRect.Visibility = Visibility.Collapsed;
            return;
        }

        if (_tool == EditorTool.Highlight)
        {
            PixelateRect.Fill = HighlightFillBrush;
            PixelateRect.Stroke = HighlightStrokeBrush;
        }
        else
        {
            PixelateRect.Fill = PixelateFillBrush;
            PixelateRect.Stroke = PixelateStrokeBrush;
        }

        var scale = FittedImageScale();
        if (scale <= 0)
        {
            scale = 1;
        }

        PixelateRect.StrokeThickness = 2 / scale;
        PixelateRect.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        PixelateRect.Width = rect.Width;
        PixelateRect.Height = rect.Height;
        PixelateRect.HorizontalAlignment = HorizontalAlignment.Left;
        PixelateRect.VerticalAlignment = VerticalAlignment.Top;
        PixelateRect.Visibility = Visibility.Visible;
    }

    private Point ClampToImage(Point point)
    {
        var width = _current?.PixelWidth ?? 0;
        var height = _current?.PixelHeight ?? 0;
        var maxX = Math.Max(0, width - 1);
        var maxY = Math.Max(0, height - 1);
        var x = point.X;
        var y = point.Y;
        if (double.IsNaN(x) || x < 0)
        {
            x = 0;
        }
        else if (x > maxX)
        {
            x = maxX;
        }

        if (double.IsNaN(y) || y < 0)
        {
            y = 0;
        }
        else if (y > maxY)
        {
            y = maxY;
        }

        return new Point(x, y);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private Int32Rect PixelRect(Point start, Point end)
    {
        var width = _current?.PixelWidth ?? 0;
        var height = _current?.PixelHeight ?? 0;
        var x0 = ClampToPixel(Math.Floor(Math.Min(start.X, end.X)), width);
        var y0 = ClampToPixel(Math.Floor(Math.Min(start.Y, end.Y)), height);
        var x1 = ClampToPixel(Math.Ceiling(Math.Max(start.X, end.X)), width);
        var y1 = ClampToPixel(Math.Ceiling(Math.Max(start.Y, end.Y)), height);
        return new Int32Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static int ClampToPixel(double value, int limit)
    {
        var pixel = (int)Math.Floor(value);
        if (pixel < 0)
        {
            return 0;
        }

        if (pixel > limit)
        {
            return limit;
        }

        return pixel;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyImage();

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveImage();

    private void Undo()
    {
        if (_current is null)
        {
            return;
        }

        var restored = _history.Undo(_current);
        if (restored is null)
        {
            return;
        }

        SetImage(restored, resetHistory: false);
    }

    private void Redo()
    {
        if (_current is null)
        {
            return;
        }

        var restored = _history.Redo(_current);
        if (restored is null)
        {
            return;
        }

        SetImage(restored, resetHistory: false);
    }

    private void CopyImage()
    {
        if (_current is null)
        {
            return;
        }

        try
        {
            ImageUtility.CopyToClipboard(_current);
            ShowToast("Copied to clipboard");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SafeSnap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveImage()
    {
        if (_current is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save capture",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = ImageUtility.DefaultFileName(),
            InitialDirectory = ImageUtility.PicturesFolder()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ImageUtility.SavePng(_current, dialog.FileName);
            ShowToast("Saved");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SafeSnap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowToast(string message)
    {
        if (_screenshotMode)
        {
            return;
        }

        ToastText.Text = message;
        ToastRoot.Visibility = Visibility.Visible;
        _toastClosing = false;

        ToastCheck.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
        ToastCheck.StrokeDashOffset = 24;
        ToastRoot.BeginAnimation(OpacityProperty, null);
        ToastSlide.BeginAnimation(TranslateTransform.YProperty, null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        ToastSlide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        ToastCheck.BeginAnimation(
            System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(90),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        _toastHideTimer?.Stop();
        _toastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _toastHideTimer.Tick += OnToastHideTick;
        _toastHideTimer.Start();
    }

    private void OnToastHideTick(object? sender, EventArgs e)
    {
        _toastHideTimer?.Stop();
        HideToast();
    }

    private void HideToast()
    {
        if (_toastClosing)
        {
            return;
        }

        _toastClosing = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (_toastClosing)
            {
                ToastRoot.Visibility = Visibility.Collapsed;
                ToastCheck.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            }
        };
        ToastRoot.BeginAnimation(OpacityProperty, fade);
        ToastSlide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, 12, TimeSpan.FromMilliseconds(220)));
    }

    private int CurrentZoomPercent()
    {
        if (_current is null || ImageViewbox.ActualWidth <= 0 || ImageViewbox.ActualHeight <= 0)
        {
            return 0;
        }

        try
        {
            var transform = CanvasImage.TransformToAncestor(ImageViewbox);
            var bounds = transform.TransformBounds(new Rect(0, 0, _current.PixelWidth, _current.PixelHeight));
            if (bounds.Width <= 0)
            {
                return 0;
            }

            return (int)Math.Round(bounds.Width / _current.PixelWidth * 100);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private void UpdateChrome()
    {
        var hasImage = _current is not null;
        ImageHost.Cursor = hasImage ? Cursors.Cross : Cursors.Arrow;
        ImageHost.IsHitTestVisible = hasImage;
        UndoButton.IsEnabled = hasImage && _history.CanUndo;
        RedoButton.IsEnabled = hasImage && _history.CanRedo;
        CopyButton.IsEnabled = hasImage;
        SaveButton.IsEnabled = hasImage;

        if (!hasImage)
        {
            if (StatusText.Text.StartsWith("Ctrl+Shift+S is already registered", StringComparison.Ordinal))
            {
                return;
            }

            StatusText.Text = "Ready";
            return;
        }

        var zoom = CurrentZoomPercent();
        var size = $"{_current!.PixelWidth} × {_current.PixelHeight} px";
        StatusText.Text = zoom > 0 ? $"{size}  ·  {zoom}%" : size;
    }

    private enum EditorTool
    {
        Pixelate,
        Highlight,
        Arrow
    }
}
