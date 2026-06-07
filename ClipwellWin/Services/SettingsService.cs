using System.IO;
using System.Text.Json;
using ClipwellWin.Models;

namespace ClipwellWin.Services;

public class SettingsService
{
    private static readonly string DataDir = AppPaths.DataDir;

    public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // Save() wird vom UI-Thread und vom Auto-Backup-Hintergrundthread aufgerufen.
    private readonly object _saveGate = new();

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
        lock (_saveGate)
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(Settings, JsonOpts);
            // atomic: temp + replace prevents partial writes on crash
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SettingsPath))
                File.Replace(tmp, SettingsPath, null);
            else
                File.Move(tmp, SettingsPath);
        }
    }
}
