using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SafeSnap.Tests;

/// <summary>Guards the installer script's close/relaunch behavior against accidental edits.</summary>
public class InstallerScriptTests
{
    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "installer", "SafeSnap.iss")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "installer", "SafeSnap.iss"));
    }

    [Fact]
    public void ExitEventName_MatchesTheApp()
    {
        var match = Regex.Match(Script(), "#define ExitEvent \"([^\"]+)\"");
        Assert.True(match.Success);
        Assert.Equal(SafeSnap.App.ExitEventName, match.Groups[1].Value);
    }

    [Fact]
    public void InstanceMutex_MatchesTheApp()
    {
        var match = Regex.Match(Script(), "#define InstanceMutex \"([^\"]+)\"");
        Assert.True(match.Success);
        Assert.Equal(SafeSnap.App.SingleInstanceMutexName, match.Groups[1].Value);
    }

    [Fact]
    public void ClosesSafeSnapBeforeInstallAndUninstall_WithoutShellCommands()
    {
        var script = Script();
        // Clean exit first, then Inno's Restart Manager as the force-close fallback.
        Assert.Contains("CloseApplications=force", script, StringComparison.Ordinal);
        Assert.Contains("RestartApplications=no", script, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"function PrepareToInstall\(var NeedsRestart: Boolean\): String;\s*begin\s*AskSafeSnapToExit\(\);"), script);
        Assert.Matches(new Regex(@"function InitializeUninstall\(\): Boolean;\s*begin\s*AskSafeSnapToExit\(\);"), script);
        Assert.Contains("CheckForMutexes('{#InstanceMutex}')", script, StringComparison.Ordinal);

        // No shell commands at all: they look like malware to antivirus heuristics.
        foreach (var banned in new[] { "taskkill", "tasklist", "{cmd}", "cmd.exe", "powershell", "Exec(", "[UninstallRun]", "AppMutex" })
        {
            Assert.DoesNotContain(banned, script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RelaunchesAfterInstall()
    {
        var run = Script().Split("[Run]")[1].Split("[Code]")[0];
        var lines = run.Split('\n').Where(l => l.StartsWith("Filename:", StringComparison.Ordinal)).ToList();

        // Interactive: Finish-page checkbox, ticked by default.
        Assert.Contains(lines, l => l.Contains("postinstall") && l.Contains("skipifsilent") && !l.Contains("unchecked"));
        // Silent: relaunch to the tray.
        Assert.Contains(lines, l => l.Contains("--tray") && l.Contains("skipifnotsilent"));
    }

    [Fact]
    public void StartupShortcut_StartsInTray()
        => Assert.Matches(new Regex(@"Name: ""\{autostartup\}\\\{#AppName\}"";.*Parameters: ""--tray"";.*Tasks: startupicon"), Script());
}
