namespace SafeSnap.Shell;

/// <summary>Pure decisions for tray behavior, kept separate from the window so they can be unit tested.</summary>
public static class TrayPolicy
{
    /// <summary>True when a window state change should send the main window to the tray.</summary>
    public static bool ShouldHideToTrayOnStateChange(bool isMinimized, bool screenshotMode, bool exitRequested, bool captureInProgress)
        => isMinimized && !screenshotMode && !exitRequested && !captureInProgress;

    /// <summary>True when closing the window (X) should hide to the tray instead of exiting.</summary>
    public static bool ShouldHideToTrayOnClose(bool screenshotMode, bool exitRequested)
        => !screenshotMode && !exitRequested;
}
