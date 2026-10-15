using SafeSnap.Shell;
using Xunit;

namespace SafeSnap.Tests;

public class TrayPolicyTests
{
    [Fact]
    public void Minimize_HidesToTray_InNormalUse()
        => Assert.True(TrayPolicy.ShouldHideToTrayOnStateChange(isMinimized: true, screenshotMode: false, exitRequested: false, captureInProgress: false));

    [Theory]
    [InlineData(false, false, false, false)] // restored / maximized
    [InlineData(true, true, false, false)]   // screenshot mode
    [InlineData(true, false, true, false)]   // exiting
    [InlineData(true, false, false, true)]   // capture in progress
    public void StateChange_DoesNotHide_OtherwiseExpected(bool minimized, bool screenshot, bool exiting, bool capturing)
        => Assert.False(TrayPolicy.ShouldHideToTrayOnStateChange(minimized, screenshot, exiting, capturing));

    [Fact]
    public void Close_HidesToTray_UnlessExitRequested()
    {
        Assert.True(TrayPolicy.ShouldHideToTrayOnClose(screenshotMode: false, exitRequested: false));
        Assert.False(TrayPolicy.ShouldHideToTrayOnClose(screenshotMode: false, exitRequested: true));
        Assert.False(TrayPolicy.ShouldHideToTrayOnClose(screenshotMode: true, exitRequested: false));
    }
}
