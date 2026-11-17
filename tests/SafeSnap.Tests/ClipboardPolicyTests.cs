using SafeSnap.Shell;
using Xunit;

namespace SafeSnap.Tests;

public class ClipboardPolicyTests
{
    [Fact]
    public void AutoCopy_HappensOnlyWhenEnabledWithAnImage()
    {
        Assert.True(ClipboardPolicy.ShouldAutoCopy(autoCopyEnabled: true, screenshotMode: false, hasImage: true));
        Assert.False(ClipboardPolicy.ShouldAutoCopy(autoCopyEnabled: false, screenshotMode: false, hasImage: true));
        Assert.False(ClipboardPolicy.ShouldAutoCopy(autoCopyEnabled: true, screenshotMode: true, hasImage: true));
        Assert.False(ClipboardPolicy.ShouldAutoCopy(autoCopyEnabled: true, screenshotMode: false, hasImage: false));
    }

    [Fact]
    public void ClipboardStillOurs_TracksTheSequenceNumber()
    {
        Assert.True(ClipboardPolicy.ClipboardStillOurs(42u, 42u));
        Assert.False(ClipboardPolicy.ClipboardStillOurs(42u, 43u));
        Assert.False(ClipboardPolicy.ClipboardStillOurs(null, 42u));
    }
}
