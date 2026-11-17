using System.Text.Json;

namespace SafeSnap;

internal static class AppSettings
{
    private const string TrayHintKey = "trayHintShown";
    private const string PixelateTipKey = "pixelateTipShown";
    private const string AutoCopyKey = "autoCopy";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSnap",
        "settings.json");

    private static string? _filePathOverride;
    private static bool _isolatedFromDisk;
    private static SettingsModel _isolated = new();

    // Tests point this at a temp file. Production leaves it unset (%LOCALAPPDATA%\SafeSnap\settings.json).
    internal static string? FilePathOverride
    {
        get => _filePathOverride;
        set => _filePathOverride = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static string FilePath => _filePathOverride ?? DefaultFilePath;

    // Screenshot runs must not read or write the real settings file.
    internal static void IsolateFromDisk()
    {
        _isolatedFromDisk = true;
        _isolated = new SettingsModel();
    }

    internal static void ResetIsolation()
    {
        _isolatedFromDisk = false;
        _isolated = new SettingsModel();
    }

    public static bool HasShownTrayHint()
    {
        return Load().TrayHintShown;
    }

    public static void MarkTrayHintShown()
    {
        var settings = Load();
        settings.TrayHintShown = true;
        Save(settings);
    }

    public static bool HasShownPixelateTip()
    {
        return Load().PixelateTipShown;
    }

    public static void MarkPixelateTipShown()
    {
        var settings = Load();
        settings.PixelateTipShown = true;
        Save(settings);
    }

    /// <summary>Auto-copy is on unless the user has turned it off.</summary>
    public static bool IsAutoCopyEnabled()
    {
        return Load().AutoCopy;
    }

    public static void SetAutoCopy(bool enabled)
    {
        var settings = Load();
        settings.AutoCopy = enabled;
        Save(settings);
    }

    private static SettingsModel Load()
    {
        if (_isolatedFromDisk)
        {
            return _isolated;
        }

        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return new SettingsModel();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new SettingsModel
            {
                TrayHintShown = ReadTrue(root, TrayHintKey),
                PixelateTipShown = ReadTrue(root, PixelateTipKey),
                AutoCopy = ReadBool(root, AutoCopyKey, defaultValue: true)
            };
        }
        catch (Exception)
        {
            return new SettingsModel();
        }
    }

    private static void Save(SettingsModel settings)
    {
        if (_isolatedFromDisk)
        {
            _isolated = settings;
            return;
        }

        try
        {
            var path = FilePath;
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(
                new Dictionary<string, bool>
                {
                    [TrayHintKey] = settings.TrayHintShown,
                    [PixelateTipKey] = settings.PixelateTipShown,
                    [AutoCopyKey] = settings.AutoCopy
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json + Environment.NewLine);
        }
        catch (Exception)
        {
            // A failed write only means a one-time hint may appear again or a toggle resets.
        }
    }

    private static bool ReadTrue(JsonElement root, string name)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static bool ReadBool(JsonElement root, string name, bool defaultValue)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private sealed class SettingsModel
    {
        public bool TrayHintShown { get; set; }

        public bool PixelateTipShown { get; set; }

        public bool AutoCopy { get; set; } = true;
    }
}
