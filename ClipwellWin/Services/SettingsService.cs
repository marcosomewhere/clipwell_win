using System.IO;
using System.Text.Json;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public class SettingsService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipwell");

    public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(DataDir);
        if (!File.Exists(SettingsPath)) { Save(); return; }
        try
        {
            var json = File.ReadAllText(SettingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new();
        }
        catch { Settings = new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JsonOpts));
    }
}
