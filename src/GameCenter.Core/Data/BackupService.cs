using System.IO.Compression;
using GameCenter.Core.Config;

namespace GameCenter.Core.Data;

/// <summary>Nút "Sao lưu dữ liệu": nén Saves/ + States/ thành file zip có ngày (mục 14).</summary>
public static class BackupService
{
    public static string CreateBackup(AppPaths paths)
    {
        Directory.CreateDirectory(paths.BackupsDir);
        var zipPath = Path.Combine(paths.BackupsDir, $"SaoLuu_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (dir, name) in new[] { (paths.SavesDir, "Saves"), (paths.StatesDir, "States") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var entry = Path.Combine(name, Path.GetRelativePath(dir, file)).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal);
            }
        }
        return zipPath;
    }
}
