using System.IO;
using System.Text.Json;

namespace ugokuDeskTop.Models;

internal class WallpaperConfig
{
    public string CurrentWallpaper { get; set; } = "particles";
    public string? CustomUrl { get; set; }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ugokuDeskTop");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static WallpaperConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new WallpaperConfig();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<WallpaperConfig>(json) ?? new WallpaperConfig();
        }
        catch
        {
            return new WallpaperConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
