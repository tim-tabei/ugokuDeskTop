using System.IO;

namespace ugokuDeskTop.Services;

internal record WallpaperInfo(
    string DirectoryName,
    string DisplayName,
    bool IsSpecial
);

internal static class WallpaperDiscoveryService
{
    private static readonly HashSet<string> SpecialWallpapers = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio-visualizer"
    };

    public static List<WallpaperInfo> Discover()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));

        var candidates = new Dictionary<string, WallpaperInfo>(StringComparer.OrdinalIgnoreCase);

        // 開発時: ソースディレクトリを優先スキャン
        ScanDirectory(Path.Combine(projectDir, "Wallpapers"), candidates);

        // フォールバック: ビルド出力ディレクトリ
        ScanDirectory(Path.Combine(baseDir, "Wallpapers"), candidates);

        return candidates.Values
            .OrderBy(w => w.IsSpecial)
            .ThenBy(w => w.DisplayName)
            .ToList();
    }

    private static void ScanDirectory(string wallpapersRoot, Dictionary<string, WallpaperInfo> results)
    {
        if (!Directory.Exists(wallpapersRoot)) return;

        foreach (var dir in Directory.GetDirectories(wallpapersRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (results.ContainsKey(dirName)) continue;

            var indexHtml = Path.Combine(dir, "index.html");
            if (!File.Exists(indexHtml)) continue;

            results[dirName] = new WallpaperInfo(
                DirectoryName: dirName,
                DisplayName: ToDisplayName(dirName),
                IsSpecial: SpecialWallpapers.Contains(dirName)
            );
        }
    }

    private static string ToDisplayName(string directoryName)
    {
        return string.Join(' ', directoryName.Split('-')
            .Select(word => char.ToUpper(word[0]) + word[1..]));
    }
}
