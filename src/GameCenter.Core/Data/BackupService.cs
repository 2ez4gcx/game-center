using System.IO.Compression;
using GameCenter.Core.Config;
using GameCenter.Core.Emulation;
using GameCenter.Core.Localization;

namespace GameCenter.Core.Data;

/// <summary>Nút "Sao lưu dữ liệu": nén Saves/ + States/ thành file zip có ngày (mục 14).</summary>
public static class BackupService
{
    public static string CreateBackup(AppPaths paths)
    {
        Directory.CreateDirectory(paths.BackupsDir);
        var zipPath = Path.Combine(paths.BackupsDir, $"{Loc.T("backup.file_prefix")}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var sources = new[]
        {
            (paths.SavesDir, "Saves"),
            (paths.StatesDir, "States"),
            // Thẻ nhớ PS2 kiểu cũ của LRPS2 nằm trong thư mục system (BIOS/pcsx2/memcards)
            (Path.Combine(Path.GetDirectoryName(Ps2Bios.CoreDir(paths.BiosDir))!, "memcards"), "PS2-Memcards"),
        };
        foreach (var (dir, name) in sources)
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

    /// <summary>
    /// Đổi thư mục dữ liệu: chép save, save state, sao lưu tự động và BIOS sang thư mục mới để không "mất" save.
    /// Không ghi đè file đã có ở thư mục mới, không xóa gì ở thư mục cũ. Game (Games/) không chép vì có thể rất lớn.
    /// Trả về số file đã chép.
    /// </summary>
    public static int CopyUserData(AppPaths from, AppPaths to)
    {
        int copied = 0;
        foreach (var (src, dst) in new[] { (from.SavesDir, to.SavesDir), (from.StatesDir, to.StatesDir), (from.BiosDir, to.BiosDir), (from.BackupsDir, to.BackupsDir) })
        {
            if (!Directory.Exists(src) || string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(dst, Path.GetRelativePath(src, file));
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
                copied++;
            }
        }
        return copied;
    }
}
