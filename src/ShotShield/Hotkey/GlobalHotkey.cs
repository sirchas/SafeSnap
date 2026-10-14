using ShotShield.Capture;

namespace ShotShield.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5301;
    private const int HWND_MESSAGE = -3;

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    public bool Attach()
    {
        // Hide() destroys the main window HWND. A message-only window stays alive in the tray
        // so Ctrl+Shift+S still arrives while the window is hidden.
        var parameters = new HwndSourceParameters("SafeSnap.Hotkey")
        {
            ParentWindow = new IntPtr(HWND_MESSAGE),
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _registered = NativeMethods.RegisterHotKey(
            _source.Handle,
            HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_S);
        return _registered;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }
}
