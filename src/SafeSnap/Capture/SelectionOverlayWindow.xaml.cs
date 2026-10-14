using System.Windows.Shapes;

namespace SafeSnap.Capture;

public partial class SelectionOverlayWindow : Window
{
    private readonly int _originX;
    private readonly int _originY;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly int _bitmapWidth;
    private readonly int _bitmapHeight;
    private bool _dragging;
    private bool _completed;
    private bool _cancelNotified;
    private int _anchorX;
    private int _anchorY;
    private int _currentX;
    private int _currentY;

    public SelectionOverlayWindow(BitmapSource screenshot, int originX, int originY, int pixelWidth, int pixelHeight)
    {
        InitializeComponent();
        FrozenImage.Source = screenshot;
        _originX = originX;
        _originY = originY;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _bitmapWidth = screenshot.PixelWidth;
        _bitmapHeight = screenshot.PixelHeight;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ForcePhysicalBounds();
            SyncCursor();
            UpdateChrome();
        };
        ContentRendered += (_, _) =>
        {
            ForcePhysicalBounds();
            Activate();
            Focus();
            Keyboard.Focus(this);
            SyncCursor();
            UpdateChrome();
        };
        SizeChanged += (_, _) => UpdateChrome();
    }

    public event Action<Int32Rect>? RegionSelected;

    public event Action? SelectionCancelled;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(WindowProc);
        ForcePhysicalBounds();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            // Keep the HWND on the virtual screen in physical pixels. WPF would otherwise
            // place it in device-independent pixels and drift under mixed DPI.
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            pos.x = _originX;
            pos.y = _originY;
            pos.cx = _pixelWidth;
            pos.cy = _pixelHeight;
            pos.flags &= ~(NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            Marshal.StructureToPtr(pos, lParam, false);
        }

        return IntPtr.Zero;
    }

    private void ForcePhysicalBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _originX,
            _originY,
            _pixelWidth,
            _pixelHeight,
            NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = PhysicalPoint();
        _dragging = true;
        _anchorX = _currentX = point.X;
        _anchorY = _currentY = point.Y;
        CaptureMouse();
        UpdateChrome();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = PhysicalPoint();
        _currentX = point.X;
        _currentY = point.Y;
        UpdateChrome();
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        SyncCursor();
        UpdateChrome();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        var point = PhysicalPoint();
        _currentX = point.X;
        _currentY = point.Y;
        var rect = CurrentRect();
        if (rect.Width >= 1 && rect.Height >= 1)
        {
            _completed = true;
            try
            {
                RegionSelected?.Invoke(rect);
            }
            finally
            {
                Close();
            }
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_completed && !_cancelNotified)
        {
            _cancelNotified = true;
            SelectionCancelled?.Invoke();
        }

        base.OnClosed(e);
    }

    private void Cancel()
    {
        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Close();
    }

    private Int32Rect CurrentRect()
    {
        var x = Math.Min(_anchorX, _currentX);
        var y = Math.Min(_anchorY, _currentY);
        var width = Math.Abs(_currentX - _anchorX);
        var height = Math.Abs(_currentY - _anchorY);
        return new Int32Rect(x, y, width, height);
    }

    /// <summary>
    /// Cursor position from GetCursorPos is in physical screen pixels for a PerMonitorV2 process.
    /// Subtracting the virtual-screen origin maps it onto the captured bitmap.
    /// </summary>
    private (int X, int Y) PhysicalPoint()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var x = Math.Clamp(cursor.X - _originX, 0, _bitmapWidth);
        var y = Math.Clamp(cursor.Y - _originY, 0, _bitmapHeight);
        return (x, y);
    }

    private void SyncCursor()
    {
        var point = PhysicalPoint();
        _currentX = point.X;
        _currentY = point.Y;
    }

    private void UpdateChrome()
    {
        var viewWidth = ActualWidth;
        var viewHeight = ActualHeight;
        if (viewWidth <= 0 || viewHeight <= 0 || _bitmapWidth <= 0 || _bitmapHeight <= 0)
        {
            return;
        }

        var scaleX = viewWidth / _bitmapWidth;
        var scaleY = viewHeight / _bitmapHeight;
        var cx = _currentX * scaleX;
        var cy = _currentY * scaleY;

        PlaceLine(CrosshairVShadow, cx, 0, cx, viewHeight);
        PlaceLine(CrosshairV, cx, 0, cx, viewHeight);
        PlaceLine(CrosshairHShadow, 0, cy, viewWidth, cy);
        PlaceLine(CrosshairH, 0, cy, viewWidth, cy);

        if (!_dragging)
        {
            Place(DimTop, 0, 0, viewWidth, viewHeight);
            DimBottom.Visibility = Visibility.Collapsed;
            DimLeft.Visibility = Visibility.Collapsed;
            DimRight.Visibility = Visibility.Collapsed;
            SelectionHalo.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = CurrentRect();
        var dx = rect.X * scaleX;
        var dy = rect.Y * scaleY;
        var dw = rect.Width * scaleX;
        var dh = rect.Height * scaleY;

        Place(DimTop, 0, 0, viewWidth, dy);
        Place(DimBottom, 0, dy + dh, viewWidth, Math.Max(0, viewHeight - (dy + dh)));
        Place(DimLeft, 0, dy, dx, dh);
        Place(DimRight, dx + dw, dy, Math.Max(0, viewWidth - (dx + dw)), dh);

        PlaceBorder(SelectionHalo, dx, dy, dw, dh);
        PlaceBorder(SelectionBorder, dx, dy, dw, dh);

        SizeText.Text = $"{rect.Width} × {rect.Height} px";
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeWidth = SizeBadge.DesiredSize.Width;
        var badgeHeight = SizeBadge.DesiredSize.Height;
        var badgeX = cx + 18;
        var badgeY = cy + 18;
        if (badgeX + badgeWidth > viewWidth - 8)
        {
            badgeX = cx - badgeWidth - 18;
        }

        if (badgeY + badgeHeight > viewHeight - 8)
        {
            badgeY = cy - badgeHeight - 18;
        }

        if (badgeX < 8)
        {
            badgeX = 8;
        }

        if (badgeY < 8)
        {
            badgeY = 8;
        }

        Canvas.SetLeft(SizeBadge, badgeX);
        Canvas.SetTop(SizeBadge, badgeY);
    }

    private static void Place(System.Windows.Shapes.Rectangle rectangle, double x, double y, double width, double height)
    {
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
        rectangle.Visibility = Visibility.Visible;
    }

    private static void PlaceBorder(System.Windows.Shapes.Rectangle rectangle, double x, double y, double width, double height)
    {
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
        rectangle.Visibility = Visibility.Visible;
    }

    private static void PlaceLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        line.Visibility = Visibility.Visible;
    }
}
