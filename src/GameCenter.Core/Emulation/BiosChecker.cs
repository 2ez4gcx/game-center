using System.Text;
using GameCenter.Core.Config;

namespace GameCenter.Core.Emulation;

public enum BiosState
{
    /// <summary>Platform không cần BIOS.</summary>
    NotNeeded,
    Present,
    /// <summary>Thiếu BIOS nhưng core có thay thế (OpenBIOS/HLE) → vẫn chạy.</summary>
    UsingFallback,
    /// <summary>Không thể chạy → hiển thị hướng dẫn.</summary>
    Missing,
}

/// <summary>Luồng kiểm tra BIOS (mục 11). Game Center không bao giờ đóng gói hoặc tải BIOS.</summary>
public static class BiosChecker
{
    /// <summary>Chuẩn bị trước khi chạy game: đưa BIOS người dùng đã chép vào đúng chỗ core tìm.</summary>
    public static void Prepare(PlatformDefinition platform, string biosDir)
    {
        if (platform.Bios?.Detect == Ps2Bios.DetectId) Ps2Bios.Sync(biosDir);
    }

    public static BiosState Check(PlatformDefinition platform, string biosDir)
    {
        var bios = platform.Bios;
        if (bios == null) return BiosState.NotNeeded;
        bool present;
        if (bios.Detect == Ps2Bios.DetectId)
            present = Ps2Bios.HasBios(biosDir);
        else if (bios.Files.Count == 0)
            return BiosState.NotNeeded;
        else
            present = Directory.Exists(biosDir) && Directory.EnumerateFiles(biosDir)
                .Any(f => bios.Files.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        if (present) return BiosState.Present;
        return bios.Required ? BiosState.Missing : BiosState.UsingFallback;
    }
}

/// <summary>
/// BIOS PS2 cho core LRPS2. Core tìm trong &lt;system&gt;/pcsx2/bios/ và nhận mọi file 4–8 MB có bảng ROMDIR
/// (mục "RESET" rồi "ROMDIR"), tên file không quan trọng. Người dùng thường chép BIOS thẳng vào thư mục BIOS,
/// nên trước khi chạy game Game Center tự chép BIOS ở BIOS/ và BIOS/PS2/ sang BIOS/pcsx2/bios/.
/// </summary>
public static class Ps2Bios
{
    public const string DetectId = "ps2";
    public const long MinSize = 4L * 1024 * 1024;
    public const long MaxSize = 8L * 1024 * 1024;

    /// <summary>Thư mục LRPS2 tìm BIOS (chữ thường: quan trọng trên Linux / macOS).</summary>
    public static string CoreDir(string biosDir) => Path.Combine(biosDir, "pcsx2", "bios");

    /// <summary>Thư mục người dùng có thể chép BIOS PS2 vào.</summary>
    public static IEnumerable<string> UserDirs(string biosDir) => new[] { biosDir, Path.Combine(biosDir, "PS2") };

    /// <summary>Bảng ROMDIR nằm ngay sau module RESET ở đầu BIOS; chỉ cần đọc phần đầu file.</summary>
    private const int RomdirSearchSize = 512 * 1024;

    /// <summary>File là BIOS PS2: cỡ 4–8 MB, có mục ROMDIR "RESET" và ngay sau là "ROMDIR" (cách PCSX2 kiểm tra).</summary>
    public static bool IsBios(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length < MinSize || fi.Length > MaxSize) return false;
            var data = new byte[RomdirSearchSize];
            int n;
            using (var fs = fi.OpenRead()) n = fs.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
            for (int i = 0; i + 32 <= n; i += 16)
                if (Name(data, i) == "RESET" && Name(data, i + 16) == "ROMDIR") return true;
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Tên mục ROMDIR: 10 byte ASCII, đệm bằng 0.</summary>
    private static string Name(byte[] data, int offset)
    {
        int len = 0;
        while (len < 10 && data[offset + len] != 0) len++;
        return Encoding.ASCII.GetString(data, offset, len);
    }

    public static bool HasBios(string biosDir)
    {
        var dir = CoreDir(biosDir);
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(IsBios);
    }

    /// <summary>
    /// Chép BIOS PS2 ở BIOS/ và BIOS/PS2/ sang BIOS/pcsx2/bios/, kèm file phụ cùng tên (.rom1, .rom2, .erom, .nvm, .mec).
    /// Không ghi đè file đã có, không xóa file gốc. Trả về số file đã chép.
    /// </summary>
    public static int Sync(string biosDir)
    {
        var target = CoreDir(biosDir);
        int copied = 0;
        foreach (var dir in UserDirs(biosDir).Where(Directory.Exists))
        {
            var files = Directory.GetFiles(dir);
            foreach (var bios in files.Where(IsBios))
            {
                var baseName = Path.GetFileNameWithoutExtension(bios);
                foreach (var f in files.Where(f => Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                {
                    var dest = Path.Combine(target, Path.GetFileName(f));
                    if (File.Exists(dest)) continue;
                    Directory.CreateDirectory(target);
                    File.Copy(f, dest);
                    copied++;
                }
            }
        }
        return copied;
    }

    /// <summary>
    /// Chép file BIOS người dùng để nhầm trong thư mục Games sang thư mục BIOS (kèm file phụ cùng tên).
    /// BIOS PS2 vào thẳng BIOS/pcsx2/bios/. Không ghi đè, không xóa file gốc. Trả về số file đã chép.
    /// </summary>
    public static int ImportFromGames(IEnumerable<string> biosFiles, string biosDir)
    {
        int copied = 0;
        foreach (var bios in biosFiles)
        {
            var dir = Path.GetDirectoryName(bios)!;
            var target = IsBios(bios) ? CoreDir(biosDir) : biosDir;
            var baseName = Path.GetFileNameWithoutExtension(bios);
            foreach (var f in Directory.EnumerateFiles(dir).Where(f => Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase)))
            {
                var dest = Path.Combine(target, Path.GetFileName(f));
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(target);
                File.Copy(f, dest);
                copied++;
            }
        }
        return copied;
    }
}
