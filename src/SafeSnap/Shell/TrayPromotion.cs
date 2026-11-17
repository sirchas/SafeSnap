using Microsoft.Win32;

namespace SafeSnap.Shell;

/// <summary>
/// Windows 11 puts every new notification-area icon in the hidden overflow (the ^ flyout) until the
/// user drags it out. It remembers that per exe path under HKCU\Control Panel\NotifyIconSettings, so a
/// fresh portable download or a new install path starts hidden again. SafeSnap asks once to be shown
/// on the taskbar: it only fills in IsPromoted when Windows has not recorded a choice, so a user who
/// later hides the icon in Settings keeps that choice.
/// </summary>
public static class TrayPromotion
{
    internal const string SettingsKeyPath = @"Control Panel\NotifyIconSettings";
    internal const string ExecutablePathValue = "ExecutablePath";
    internal const string IsPromotedValue = "IsPromoted";

    /// <summary>Result of one promotion attempt.</summary>
    public enum Outcome
    {
        /// <summary>Windows has not registered the icon yet; try again shortly.</summary>
        NotRegisteredYet,

        /// <summary>IsPromoted was missing and has been set to 1.</summary>
        Promoted,

        /// <summary>Windows (or the user) already recorded a choice; left unchanged.</summary>
        AlreadyDecided,

        /// <summary>Not supported here (no registry key, older Windows, or access denied).</summary>
        Unavailable
    }

    /// <summary>True when the stored IsPromoted value means nobody has chosen yet.</summary>
    public static bool ShouldPromote(object? isPromotedValue) => isPromotedValue is null;

    /// <summary>
    /// Compares a registered ExecutablePath with the running exe. Windows stores some paths with a
    /// known-folder GUID prefix such as <c>{6D809377-...}\App\App.exe</c>.
    /// </summary>
    public static bool PathMatches(string? registered, string? exePath, Func<Guid, string?> resolveKnownFolder)
    {
        if (string.IsNullOrWhiteSpace(registered) || string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var expanded = ExpandKnownFolder(registered.Trim(), resolveKnownFolder);
        if (expanded is null)
        {
            return false;
        }

        return string.Equals(Normalize(expanded), Normalize(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds this exe's NotifyIconSettings entry and promotes it when no choice is recorded.</summary>
    public static Outcome TryPromote(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Outcome.Unavailable;
        }

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (root is null)
            {
                // Windows 10 and earlier keep tray state in a binary blob; nothing to do there.
                return Outcome.Unavailable;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name, writable: false);
                if (entry?.GetValue(ExecutablePathValue) is not string registered
                    || !PathMatches(registered, exePath, ResolveKnownFolder))
                {
                    continue;
                }

                if (!ShouldPromote(entry.GetValue(IsPromotedValue)))
                {
                    return Outcome.AlreadyDecided;
                }

                using var writable = root.OpenSubKey(name, writable: true);
                if (writable is null)
                {
                    return Outcome.Unavailable;
                }

                writable.SetValue(IsPromotedValue, 1, RegistryValueKind.DWord);
                return Outcome.Promoted;
            }

            return Outcome.NotRegisteredYet;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Outcome.Unavailable;
        }
    }

    private static string? ExpandKnownFolder(string path, Func<Guid, string?> resolveKnownFolder)
    {
        if (!path.StartsWith('{'))
        {
            return path;
        }

        var close = path.IndexOf('}');
        if (close < 0 || !Guid.TryParse(path.AsSpan(1, close - 1), out var folderId))
        {
            return null;
        }

        var folder = resolveKnownFolder(folderId);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        var rest = path[(close + 1)..].TrimStart('\\', '/');
        return Path.Combine(folder, rest);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch (Exception)
        {
            return path.TrimEnd('\\', '/');
        }
    }

    private static string? ResolveKnownFolder(Guid folderId)
    {
        var hr = SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var buffer);
        try
        {
            return hr == 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
