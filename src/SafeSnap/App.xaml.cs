using System.Threading;

namespace SafeSnap;

public partial class App : Application
{
    public const string SingleInstanceMutexName = "SafeSnap.SingleInstance";
    private const string ActivateEventName = "SafeSnap.SingleInstance.Activate";

    // The installer and uninstaller set this to ask a running SafeSnap to exit cleanly
    // (closing the window only hides it to the tray). Keep in sync with installer\SafeSnap.iss.
    public const string ExitEventName = "SafeSnap.SingleInstance.Exit";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateEvent;
    private static EventWaitHandle? _exitEvent;
    private static bool _ownsInstanceMutex;
    private static volatile bool _listenForActivation;
    private static int _listenerStarted;

    public static event Action? ActivationRequested;

    public static event Action? ExitRequested;

    static App()
    {
        // Headless and disconnected sessions skip composition, so RenderTargetBitmap
        // stays transparent unless rendering is explicitly allowed with no display.
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        AppContext.SetSwitch("System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldNotRenderInNonInteractiveWindowStation", false);
        AppContext.SetSwitch("System.Windows.Media.ShouldNotRenderInNonInteractiveWindowStation", false);
    }

    public static string? ScreenshotPath { get; private set; }

    public static string? ScreenshotEmptyPath { get; private set; }

    public static string? ScreenshotTipPath { get; private set; }

    /// <summary>True when launched with --tray (the installer's Startup shortcut): start hidden in the tray.</summary>
    public static bool StartInTray { get; private set; }

    public static bool IsScreenshotMode =>
        !string.IsNullOrWhiteSpace(ScreenshotPath)
        || !string.IsNullOrWhiteSpace(ScreenshotEmptyPath)
        || !string.IsNullOrWhiteSpace(ScreenshotTipPath);

    protected override void OnStartup(StartupEventArgs e)
    {
        ReadScreenshotArgs(e.Args);
        StartInTray = SafeSnap.Shell.LaunchOptions.HasTrayFlag(e.Args);
        if (IsScreenshotMode)
        {
            // Screenshot runs must not take the interactive mutex, leave a tray process behind,
            // or read/write the real settings file.
            AppSettings.IsolateFromDisk();
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
        else if (!TryBecomePrimaryInstance())
        {
            // A logon (--tray) launch must not pop up a copy that is already running.
            SignalPrimaryInstance(activate: SafeSnap.Shell.LaunchOptions.ShouldActivateExistingInstance(StartInTray));
            Environment.Exit(0);
            return;
        }

        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        if (StartInTray && !IsScreenshotMode)
        {
            window.StartHiddenInTray();
        }
        else
        {
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listenForActivation = false;
        try
        {
            _activateEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
            // The listener is exiting with the process.
        }

        try
        {
            _exitEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
            // The listener is exiting with the process.
        }

        ReleaseInstanceMutex();
        base.OnExit(e);
    }

    public static void StartActivationListener()
    {
        if (IsScreenshotMode || _activateEvent is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _listenerStarted, 1) != 0)
        {
            return;
        }

        _listenForActivation = true;
        var thread = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "SafeSnap.Activation"
        };
        thread.Start();
    }

    private static bool TryBecomePrimaryInstance()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ActivateEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
        _instanceMutex = new Mutex(false, SingleInstanceMutexName);
        try
        {
            _ownsInstanceMutex = _instanceMutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsInstanceMutex = true;
        }

        return _ownsInstanceMutex;
    }

    private static void SignalPrimaryInstance(bool activate)
    {
        if (activate)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                _activateEvent?.Set();
            }
        }

        _ownsInstanceMutex = false;
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        _activateEvent?.Dispose();
        _activateEvent = null;
        _exitEvent?.Dispose();
        _exitEvent = null;
    }

    private static void ReleaseInstanceMutex()
    {
        if (_ownsInstanceMutex)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released.
            }

            _ownsInstanceMutex = false;
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;
        _activateEvent?.Dispose();
        _activateEvent = null;
        _exitEvent?.Dispose();
        _exitEvent = null;
    }

    private static void ListenForActivation()
    {
        var handle = _activateEvent;
        var exit = _exitEvent;
        if (handle is null || exit is null)
        {
            return;
        }

        var handles = new WaitHandle[] { handle, exit };
        while (_listenForActivation)
        {
            int signaled;
            try
            {
                signaled = WaitHandle.WaitAny(handles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!_listenForActivation)
            {
                return;
            }

            if (signaled == 1)
            {
                // Installer/uninstaller asked us to quit. Stop listening and exit like tray > Exit.
                _listenForActivation = false;
                Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ExitRequested is null)
                    {
                        Current?.Shutdown(0);
                        return;
                    }

                    ExitRequested.Invoke();
                }));
                return;
            }

            try
            {
                handle.Reset();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            Current?.Dispatcher.BeginInvoke(new Action(() => ActivationRequested?.Invoke()));
        }
    }

    private static void ReadScreenshotArgs(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotPath = args[i + 1];
            }
            else if (string.Equals(args[i], "--screenshot-empty", StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotEmptyPath = args[i + 1];
            }
            else if (string.Equals(args[i], "--screenshot-tip", StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotTipPath = args[i + 1];
            }
        }
    }
}
