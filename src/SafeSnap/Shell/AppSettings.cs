using System.Text.Json;

namespace SafeSnap;

internal static class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSnap");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static bool HasShownTrayHint()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            return document.RootElement.TryGetProperty("trayHintShown", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void MarkTrayHintShown()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(
                new Dictionary<string, bool> { ["trayHintShown"] = true },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json + Environment.NewLine);
        }
        catch (Exception)
        {
            // A failed write only means the one-time hint may appear again.
        }
    }
}
