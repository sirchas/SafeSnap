namespace SafeSnap.Shell;

/// <summary>Command-line switches, kept pure so they can be unit tested.</summary>
public static class LaunchOptions
{
    /// <summary>Switches that start SafeSnap hidden in the tray (used by the installer's Startup shortcut).</summary>
    public static readonly IReadOnlyList<string> TraySwitches = new[] { "--tray", "--minimized", "/tray" };

    public static bool HasTrayFlag(IEnumerable<string>? args)
    {
        if (args is null)
        {
            return false;
        }

        foreach (var arg in args)
        {
            foreach (var flag in TraySwitches)
            {
                if (string.Equals(arg?.Trim(), flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A second launch normally brings the running window forward. A tray (logon) launch
    /// should stay quiet, so it must not activate the copy that is already running.
    /// </summary>
    public static bool ShouldActivateExistingInstance(bool startInTray) => !startInTray;
}
