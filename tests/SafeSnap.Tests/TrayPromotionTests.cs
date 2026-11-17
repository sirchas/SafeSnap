using SafeSnap.Shell;
using Xunit;

namespace SafeSnap.Tests;

public class TrayPromotionTests
{
    private static readonly Guid ProgramFilesX64 = new("6D809377-6AF0-444B-8957-A3773F02200E");

    private static string? Resolve(Guid id) => id == ProgramFilesX64 ? @"C:\Program Files" : null;

    [Fact]
    public void Promotes_OnlyWhenNoChoiceIsRecorded()
    {
        Assert.True(TrayPromotion.ShouldPromote(null));
        Assert.False(TrayPromotion.ShouldPromote(0)); // user hid it in Settings
        Assert.False(TrayPromotion.ShouldPromote(1)); // already shown
    }

    [Fact]
    public void PathMatches_PlainPathsIgnoringCase()
    {
        Assert.True(TrayPromotion.PathMatches(@"C:\Users\me\Desktop\SafeSnap.exe", @"c:\users\ME\desktop\safesnap.EXE", Resolve));
        Assert.False(TrayPromotion.PathMatches(@"C:\Users\me\Downloads\SafeSnap-1.1.1-portable-win-x64.exe", @"C:\Users\me\Desktop\SafeSnap.exe", Resolve));
    }

    [Fact]
    public void PathMatches_ExpandsKnownFolderPrefix()
    {
        Assert.True(TrayPromotion.PathMatches(@"{6D809377-6AF0-444B-8957-A3773F02200E}\SafeSnap\SafeSnap.exe", @"C:\Program Files\SafeSnap\SafeSnap.exe", Resolve));
        Assert.False(TrayPromotion.PathMatches(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe", @"C:\Windows\explorer.exe", Resolve));
        Assert.False(TrayPromotion.PathMatches(@"{not-a-guid}\x.exe", @"C:\x.exe", Resolve));
    }

    [Theory]
    [InlineData(null, @"C:\a.exe")]
    [InlineData("", @"C:\a.exe")]
    [InlineData(@"C:\a.exe", null)]
    public void PathMatches_RejectsMissingPaths(string? registered, string? exe)
        => Assert.False(TrayPromotion.PathMatches(registered, exe, Resolve));
}
