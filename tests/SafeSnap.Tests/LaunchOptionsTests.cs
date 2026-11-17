using SafeSnap.Shell;
using Xunit;

namespace SafeSnap.Tests;

public class LaunchOptionsTests
{
    [Theory]
    [InlineData("--tray")]
    [InlineData("--TRAY")]
    [InlineData("--minimized")]
    [InlineData("/tray")]
    public void TrayFlag_IsRecognized(string flag)
        => Assert.True(LaunchOptions.HasTrayFlag(new[] { flag }));

    [Fact]
    public void TrayFlag_AbsentMeansNormalStart()
    {
        Assert.False(LaunchOptions.HasTrayFlag(null));
        Assert.False(LaunchOptions.HasTrayFlag(Array.Empty<string>()));
        Assert.False(LaunchOptions.HasTrayFlag(new[] { "--screenshot", "out.png" }));
        Assert.False(LaunchOptions.HasTrayFlag(new[] { "tray" }));
    }

    [Fact]
    public void TrayFlag_FoundAmongOtherArgs()
        => Assert.True(LaunchOptions.HasTrayFlag(new[] { "--foo", " --tray " }));

    [Fact]
    public void TrayLaunch_DoesNotPopUpTheRunningCopy()
    {
        Assert.False(LaunchOptions.ShouldActivateExistingInstance(startInTray: true));
        Assert.True(LaunchOptions.ShouldActivateExistingInstance(startInTray: false));
    }
}
