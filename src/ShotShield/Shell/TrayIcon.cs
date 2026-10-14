namespace ShotShield;

internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private readonly MemoryStream? _iconStream;
    private readonly Action _open;
    private readonly Action _capture;
    private readonly Action _exit;
    private bool _disposed;

    public TrayIcon(Action open, Action capture, Action exit)
    {
        _open = open;
        _capture = capture;
        _exit = exit;
        _ownedIcon = LoadIcon(out var iconStream);
        _iconStream = iconStream;
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application,
            Text = "SafeSnap",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => Run(_open);
    }

    public void ShowFirstHint()
    {
        if (_disposed)
        {
            return;
        }

        _icon.BalloonTipTitle = "SafeSnap";
        _icon.BalloonTipText = "SafeSnap is still running. Double-click this icon to open it.";
        _icon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = null;
        _icon.Dispose();
        menu?.Dispose();
        _ownedIcon?.Dispose();
        _iconStream?.Dispose();
    }

    private System.Windows.Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("New Capture", null, (_, _) => Run(_capture));
        menu.Items.Add("Open", null, (_, _) => Run(_open));
        menu.Items.Add("Exit", null, (_, _) =>
        {
            // Exit disposes this icon. Defer until the tray message has returned.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                _exit();
                return;
            }

            dispatcher.BeginInvoke(_exit);
        });
        return menu;
    }

    private static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private static System.Drawing.Icon? LoadIcon(out MemoryStream? stream)
    {
        stream = null;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
            if (resource?.Stream is not null)
            {
                var copy = new MemoryStream();
                resource.Stream.CopyTo(copy);
                resource.Stream.Dispose();
                copy.Position = 0;
                stream = copy;
                return new System.Drawing.Icon(copy);
            }
        }
        catch (Exception)
        {
            stream?.Dispose();
            stream = null;
        }

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(path);
            }
        }
        catch (Exception)
        {
            // NotifyIcon falls back to the shell application icon.
        }

        return null;
    }
}
