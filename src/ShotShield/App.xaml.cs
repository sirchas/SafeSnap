using System.Threading;

namespace ShotShield;

public partial class App : Application
{
    public const string SingleInstanceMutexName = "SafeSnap.SingleInstance";
    private const string ActivateEventName = "SafeSnap.SingleInstance.Activate";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateEvent;
    private static bool _ownsInstanceMutex;
    private static volatile bool _listenForActivation;
    private static int _listenerStarted;

    public static event Action? ActivationRequested;

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

    public static bool IsScreenshotMode =>
        !string.IsNullOrWhiteSpace(ScreenshotPath) || !string.IsNullOrWhiteSpace(ScreenshotEmptyPath);

    protected override void OnStartup(StartupEventArgs e)
    {
        ReadScreenshotArgs(e.Args);
        if (IsScreenshotMode)
        {
            // Screenshot runs must not take the interactive mutex or leave a tray process behind.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
        else if (!TryBecomePrimaryInstance())
        {
            SignalPrimaryInstance();
            Environment.Exit(0);
            return;
        }

        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        base.OnStartup(e);
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

    private static void SignalPrimaryInstance()
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

        _ownsInstanceMutex = false;
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        _activateEvent?.Dispose();
        _activateEvent = null;
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
    }

    private static void ListenForActivation()
    {
        var handle = _activateEvent;
        if (handle is null)
        {
            return;
        }

        while (_listenForActivation)
        {
            try
            {
                handle.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!_listenForActivation)
            {
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
        }
    }
}
