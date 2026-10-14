using System.IO;
using SafeSnap;
using Xunit;

namespace SafeSnap.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _directory;
    private readonly string? _realBefore;
    private readonly bool _realExisted;

    public AppSettingsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SafeSnap.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _realExisted = File.Exists(RealSettingsPath);
        _realBefore = _realExisted ? File.ReadAllText(RealSettingsPath) : null;
        AppSettings.ResetIsolation();
        AppSettings.FilePathOverride = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        AppSettings.FilePathOverride = null;
        AppSettings.ResetIsolation();
        RestoreRealSettings();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void FilePath_DefaultsToLocalAppDataSettingsJson()
    {
        AppSettings.FilePathOverride = null;

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSnap",
            "settings.json");
        Assert.Equal(expected, AppSettings.FilePath);

        AppSettings.FilePathOverride = Path.Combine(_directory, "settings.json");
    }

    [Fact]
    public void PixelateTipShown_DefaultsToFalse()
    {
        Assert.False(File.Exists(AppSettings.FilePath));
        Assert.False(AppSettings.HasShownPixelateTip());

        File.WriteAllText(AppSettings.FilePath, "{\n  \"trayHintShown\": true\n}\n");
        Assert.False(AppSettings.HasShownPixelateTip());

        File.WriteAllText(AppSettings.FilePath, "{\n  \"pixelateTipShown\": false\n}\n");
        Assert.False(AppSettings.HasShownPixelateTip());

        File.WriteAllText(AppSettings.FilePath, "not json");
        Assert.False(AppSettings.HasShownPixelateTip());
        AssertRealSettingsUnchanged();
    }

    [Fact]
    public void PixelateTipShown_RoundTrips()
    {
        Assert.False(AppSettings.HasShownPixelateTip());

        File.WriteAllText(
            AppSettings.FilePath,
            "{\n  \"trayHintShown\": true,\n  \"pixelateTipShown\": true\n}\n");
        Assert.True(AppSettings.HasShownPixelateTip());
        Assert.True(AppSettings.HasShownTrayHint());

        File.WriteAllText(AppSettings.FilePath, "{\n  \"trayHintShown\": true\n}\n");
        Assert.False(AppSettings.HasShownPixelateTip());
        Assert.True(AppSettings.HasShownTrayHint());

        AppSettings.MarkPixelateTipShown();
        Assert.True(AppSettings.HasShownPixelateTip());
        Assert.True(AppSettings.HasShownTrayHint());

        var saved = File.ReadAllText(AppSettings.FilePath);
        Assert.Contains("\"pixelateTipShown\": true", saved, StringComparison.Ordinal);
        Assert.Contains("\"trayHintShown\": true", saved, StringComparison.Ordinal);

        AppSettings.MarkTrayHintShown();
        Assert.True(AppSettings.HasShownPixelateTip());
        Assert.True(AppSettings.HasShownTrayHint());
        Assert.Equal(Path.Combine(_directory, "settings.json"), AppSettings.FilePath);
        AssertRealSettingsUnchanged();
    }

    [Fact]
    public void IsolatedMode_DoesNotReadOrWriteTheSettingsFile()
    {
        File.WriteAllText(AppSettings.FilePath, "{\n  \"pixelateTipShown\": true,\n  \"trayHintShown\": true\n}\n");
        var before = File.ReadAllText(AppSettings.FilePath);

        AppSettings.IsolateFromDisk();

        Assert.False(AppSettings.HasShownPixelateTip());
        Assert.False(AppSettings.HasShownTrayHint());
        AppSettings.MarkPixelateTipShown();
        AppSettings.MarkTrayHintShown();

        Assert.Equal(before, File.ReadAllText(AppSettings.FilePath));
        Assert.True(AppSettings.HasShownPixelateTip());
        AssertRealSettingsUnchanged();
    }

    private static string RealSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSnap",
        "settings.json");

    private void AssertRealSettingsUnchanged()
    {
        if (_realExisted)
        {
            Assert.True(File.Exists(RealSettingsPath));
            Assert.Equal(_realBefore, File.ReadAllText(RealSettingsPath));
            return;
        }

        Assert.False(File.Exists(RealSettingsPath));
    }

    private void RestoreRealSettings()
    {
        try
        {
            var current = File.Exists(RealSettingsPath) ? File.ReadAllText(RealSettingsPath) : null;
            if (current == _realBefore)
            {
                return;
            }

            if (_realExisted)
            {
                File.WriteAllText(RealSettingsPath, _realBefore!);
            }
            else if (File.Exists(RealSettingsPath))
            {
                File.Delete(RealSettingsPath);
            }
        }
        catch (Exception)
        {
            // Best-effort restore so a failed test does not leave the user's settings changed.
        }
    }
}
