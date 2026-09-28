using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GameCenter.Core.Config;
using GameCenter.Core.Util;

namespace GameCenter.Core.Scanning;

public static class ScanStatus
{
    public const string Ok = "ok";
    public const string Missing = "missing";
    public const string BrokenCue = "broken_cue";
    public const string Unknown = "unknown";
}

public sealed class ScannedGame
{
    public string Title { get; set; } = "";
    /// <summary>null khi chưa nhận ra platform.</summary>
    public string? Platform { get; set; }
    public string LaunchFile { get; set; } = "";
    /// <summary>File gốc của game trong thư mục người dùng (khóa để quét lại không tạo trùng).</summary>
    public string SourceFile { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public string Status { get; set; } = ScanStatus.Ok;
    public string? Message { get; set; }
    public List<string> Discs { get; } = new();
}

/// <summary>
/// Quét đệ quy thư mục Games và nhận diện game (mục 7 và 8).
/// Không bao giờ ghi vào thư mục game; file sinh ra (.m3u, .cue tạm) nằm trong Playlists/.
/// </summary>
public sealed class GameScanner
{
    private const long MaxHashSize = 64L * 1024 * 1024;
    private readonly PlatformCatalog _catalog;
    private readonly string _gamesDir;
    private readonly string _playlistsDir;

    public GameScanner(PlatformCatalog catalog, string gamesDir, string playlistsDir)
    {
        _catalog = catalog;
        _gamesDir = Path.GetFullPath(gamesDir);
        _playlistsDir = Path.GetFullPath(playlistsDir);
    }

    public List<ScannedGame> Scan()
    {
        var result = new List<ScannedGame>();
        if (!Directory.Exists(_gamesDir)) return result;

        foreach (var dir in EnumerateDirs(_gamesDir))
        {
            try { ScanDirectory(dir, result); }
            catch (Exception ex) { Log.Error($"Lỗi khi quét {dir}", ex); }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateDirs(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            yield return d;
            string[] subs;
            try { subs = Directory.GetDirectories(d); } catch { continue; }
            foreach (var s in subs.OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)) stack.Push(s);
        }
    }

    /// <summary>Platform theo thư mục người dùng đã đặt (ưu tiên 1).</summary>
    private PlatformDefinition? FolderHint(string dir)
    {
        var rel = Path.GetRelativePath(_gamesDir, dir);
        if (rel == ".") return null;
        foreach (var seg in rel.Split(Path.DirectorySeparatorChar))
        {
            var p = _catalog.FromFolderName(seg);
            if (p != null) return p;
        }
        return null;
    }

    private void ScanDirectory(string dir, List<ScannedGame> result)
    {
        var files = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) return;

        var hint = FolderHint(dir);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirGames = new List<ScannedGame>();
        string Ext(string f) => Path.GetExtension(f).ToLowerInvariant();

        // 1. .m3u người dùng tự tạo
        foreach (var m3u in files.Where(f => Ext(f) == ".m3u"))
        {
            consumed.Add(m3u);
            var g = NewGame(m3u, "PS1", Path.GetFileNameWithoutExtension(m3u));
            g.Title = NameCleaner.Clean(NameCleaner.StripDisc(g.Title));
            foreach (var line in File.ReadAllLines(m3u).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                var p = Path.IsPathRooted(line) ? line : Path.Combine(dir, line);
                if (!File.Exists(p)) { g.Status = ScanStatus.BrokenCue; g.Message = $"Playlist trỏ tới file không tồn tại: {line}"; continue; }
                g.Discs.Add(Path.GetFullPath(p));
                consumed.Add(Path.GetFullPath(p));
                if (Ext(p) == ".cue") ConsumeCue(CueParser.Parse(p), consumed, g);
            }
            dirGames.Add(g);
        }

        // 2. .cue (ưu tiên PS1), ghép nhiều đĩa
        var cues = files.Where(f => Ext(f) == ".cue" && !consumed.Contains(f)).Select(CueParser.Parse).ToList();
        foreach (var cue in cues) consumed.Add(cue.CuePath);

        var multiDisc = cues
            .Where(c => NameCleaner.DiscNumberOf(Path.GetFileNameWithoutExtension(c.CuePath)) != null)
            .GroupBy(c => NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(c.CuePath)), StringComparer.OrdinalIgnoreCase)
            .Where(grp => grp.Count() >= 2)
            .ToList();
        var inMulti = multiDisc.SelectMany(g => g).ToHashSet();

        foreach (var grp in multiDisc)
        {
            var discs = grp.OrderBy(c => NameCleaner.DiscNumberOf(Path.GetFileNameWithoutExtension(c.CuePath))).ToList();
            var g = NewGame(discs[0].CuePath, "PS1", grp.Key);
            foreach (var c in discs)
            {
                g.Discs.Add(c.CuePath);
                ConsumeCue(c, consumed, g);
            }
            dirGames.Add(g);
        }
        foreach (var cue in cues.Where(c => !inMulti.Contains(c)))
        {
            var g = NewGame(cue.CuePath, "PS1", Path.GetFileNameWithoutExtension(cue.CuePath));
            ConsumeCue(cue, consumed, g);
            dirGames.Add(g);
        }

        foreach (var f in files.Where(f => !consumed.Contains(f)))
        {
            var ext = Ext(f);
            switch (ext)
            {
                // 4. .pbp, .chd → PS1
                case ".pbp":
                case ".chd":
                    dirGames.Add(NewGame(f, "PS1", Path.GetFileNameWithoutExtension(f), size: new FileInfo(f).Length));
                    break;

                // 5. .bin không có .cue
                case ".bin":
                    dirGames.Add(ClassifyLooseBin(f, hint));
                    break;

                // 2. .zip: xem nội dung bên trong
                case ".zip":
                    dirGames.AddRange(ClassifyZip(f, hint));
                    break;

                default:
                    var platform = hint != null && hint.Extensions.Contains(ext) ? hint : _catalog.UniqueForExtension(ext);
                    if (platform == null) break; // không phải ROM (ảnh, txt...) → bỏ qua
                    var g = NewGame(f, platform.Name, Path.GetFileNameWithoutExtension(f), size: new FileInfo(f).Length);
                    g.FileHash = HashFile(f);
                    dirGames.Add(g);
                    break;
            }
        }

        // PS1 một game trong thư mục riêng: lấy tên thư mục làm tên game (ví dụ "Final Fantasy VII/ff7.cue")
        var ps1 = dirGames.Where(g => g.Platform == "PS1").ToList();
        if (ps1.Count == 1 && dirGames.Count == 1
            &&!string.Equals(Path.GetFullPath(dir), _gamesDir, StringComparison.OrdinalIgnoreCase)
            && _catalog.FromFolderName(Path.GetFileName(dir)) == null)
        {
            ps1[0].Title = NameCleaner.Clean(Path.GetFileName(dir));
        }

        // Sinh playlist .m3u cho game nhiều đĩa (sau khi đã có tên cuối cùng)
        foreach (var g in dirGames.Where(g => g.Platform == "PS1" && g.Discs.Count >= 2 && Ext(g.SourceFile) == ".cue"))
            g.LaunchFile = WriteGenerated("PS1", NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(g.SourceFile)), ".m3u", string.Join("\r\n", g.Discs) + "\r\n", g.SourceFile);

        result.AddRange(dirGames);
    }

    private ScannedGame NewGame(string sourceFile, string? platform, string rawTitle, long size = 0) => new()
    {
        Title = NameCleaner.Clean(rawTitle),
        Platform = platform,
        LaunchFile = Path.GetFullPath(sourceFile),
        SourceFile = Path.GetFullPath(sourceFile),
        FolderPath = Path.GetDirectoryName(Path.GetFullPath(sourceFile))!,
        FileSize = size,
    };

    private static void ConsumeCue(CueInfo cue, HashSet<string> consumed, ScannedGame g)
    {
        foreach (var f in cue.Files.Where(f => f.ResolvedPath != null))
        {
            consumed.Add(f.ResolvedPath!);
            try { g.FileSize += new FileInfo(f.ResolvedPath!).Length; } catch { }
        }
        if (cue.Files.Count == 0)
        {
            g.Status = ScanStatus.BrokenCue;
            g.Message = $"File {Path.GetFileName(cue.CuePath)} không có dòng FILE nào.";
        }
        else if (cue.Missing.Any())
        {
            g.Status = ScanStatus.BrokenCue;
            g.Message = $"File {Path.GetFileName(cue.CuePath)} trỏ tới file không tồn tại: " +
                        string.Join(", ", cue.Missing.Select(m => m.NameInCue));
        }
    }

    // ------------------------------------------------------------------ Nhận diện theo nội dung

    private const int CdSector = 2352;
    /// <summary>Đủ để đọc tới hết sector 16 (Primary Volume Descriptor) của ảnh đĩa CD.</summary>
    private const int HeadSize = 17 * CdSector + 64;

    public static byte[] ReadHead(Stream s, int size = HeadSize)
    {
        var buf = new byte[size];
        int n = s.ReadAtLeast(buf, size, throwOnEndOfStream: false);
        Array.Resize(ref buf, n);
        return buf;
    }

    private static bool Ascii(byte[] b, int offset, string text) =>
        b.Length >= offset + text.Length && Encoding.ASCII.GetString(b, offset, text.Length) == text;

    public static bool HasMegaDriveHeader(Stream s) => HasMegaDriveHeader(ReadHead(s, 0x110));

    /// <summary>ROM Mega Drive có "SEGA" ở 0x100 (một số ROM lệch 1 byte: " SEGA").</summary>
    public static bool HasMegaDriveHeader(byte[] head) => Ascii(head, 0x100, "SEGA") || Ascii(head, 0x101, "SEGA");

    /// <summary>Mẫu đồng bộ ở đầu mỗi sector CD thô: 00 FF×10 00.</summary>
    private static bool HasCdSync(byte[] b)
    {
        if (b.Length < 16 || b[0] != 0 || b[11] != 0) return false;
        for (int i = 1; i <= 10; i++) if (b[i] != 0xFF) return false;
        return true;
    }

    public enum BinKind { Unknown, MegaDrive, Ps1Disc, OtherCd }

    /// <summary>
    /// Nhận diện file .bin theo nội dung:
    /// - Đĩa PS1: sector thô 2352 byte, sector 16 là "\x01CD001" với system id "PLAYSTATION".
    /// - Đĩa CD khác (Sega CD, Saturn...): có sync nhưng không phải PS1 → chưa hỗ trợ.
    /// - ROM Mega Drive: "SEGA" ở 0x100.
    /// </summary>
    public static BinKind DetectBin(byte[] head)
    {
        if (HasCdSync(head))
        {
            // Mode 2 (PS1): dữ liệu sau 16 byte header + 8 byte subheader; Mode 1: sau 16 byte
            foreach (var dataOffset in new[] { 24, 16 })
            {
                int pvd = 16 * CdSector + dataOffset;
                if (head.Length > pvd + 6 && head[pvd] == 1 && Ascii(head, pvd + 1, "CD001"))
                    return Ascii(head, pvd + 8, "PLAYSTATION") ? BinKind.Ps1Disc : BinKind.OtherCd;
            }
            return BinKind.OtherCd;
        }
        if (HasMegaDriveHeader(head)) return BinKind.MegaDrive;
        return BinKind.Unknown;
    }

    private ScannedGame ClassifyLooseBin(string bin, PlatformDefinition? hint)
    {
        var size = new FileInfo(bin).Length;
        BinKind kind;
        using (var fs = File.OpenRead(bin)) kind = DetectBin(ReadHead(fs));
        // Không nhận ra từ nội dung: tin theo thư mục hệ máy người dùng đã đặt
        if (kind == BinKind.Unknown)
        {
            if (hint?.Name == "MegaDrive") kind = BinKind.MegaDrive;
            else if (hint?.Name == "PS1") kind = BinKind.Ps1Disc;
        }

        switch (kind)
        {
            case BinKind.MegaDrive:
                var g = NewGame(bin, "MegaDrive", Path.GetFileNameWithoutExtension(bin), size);
                g.FileHash = HashFile(bin);
                return g;

            case BinKind.Ps1Disc:
                // PS1 .bin đơn → sinh .cue tạm trong Playlists/PS1 (mục 8.4)
                var ps1 = NewGame(bin, "PS1", Path.GetFileNameWithoutExtension(bin), size);
                // Đặt theo tên file gốc (không phải tên đã làm sạch): RetroArch đặt tên save theo file chạy,
                // giữ tên gốc thì save không đổi khi tên hiển thị thay đổi.
                ps1.LaunchFile = WriteGenerated("PS1", Path.GetFileNameWithoutExtension(bin), ".cue", CueParser.BuildTempCue(ps1.SourceFile), ps1.SourceFile);
                return ps1;

            default:
                var u = NewGame(bin, null, Path.GetFileNameWithoutExtension(bin), size);
                u.Status = ScanStatus.Unknown;
                u.Message = kind == BinKind.OtherCd
                    ? "Đây là đĩa CD của máy khác (không phải PS1), Game Center chưa hỗ trợ."
                    : "Không nhận ra file .bin này, hãy chọn hệ máy.";
                return u;
        }
    }

    /// <summary>Nhận diện một ROM bên trong file .zip.</summary>
    private string? PlatformOfEntry(ZipArchiveEntry entry, PlatformDefinition? hint)
    {
        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (ext == ".bin")
        {
            using var s = entry.Open();
            // Đĩa PS1 trong zip không chạy trực tiếp được → không nhận
            if (HasMegaDriveHeader(ReadHead(s, 0x110))) return "MegaDrive";
            return hint?.Name == "MegaDrive" ? hint.Name : null;
        }
        if (hint != null && hint.Extensions.Contains(ext) && hint.Extensions.Contains(".zip")) return hint.Name;
        var p = _catalog.UniqueForExtension(ext);
        return p != null && p.Extensions.Contains(".zip") ? p.Name : null;
    }

    /// <summary>
    /// .zip một ROM → một game. .zip nhiều ROM → mỗi ROM một game,
    /// chạy bằng cú pháp của RetroArch "file.zip#rom.ext".
    /// </summary>
    private List<ScannedGame> ClassifyZip(string zip, PlatformDefinition? hint)
    {
        var single = NewGame(zip, null, Path.GetFileNameWithoutExtension(zip), new FileInfo(zip).Length);
        bool hintTakesZip = hint != null && hint.Extensions.Contains(".zip");
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            var roms = archive.Entries
                .Where(e => e.Length > 0 && !string.IsNullOrEmpty(e.Name)
                            && _catalog.IsKnownExtension(Path.GetExtension(e.Name).ToLowerInvariant())
                            && Path.GetExtension(e.Name).ToLowerInvariant() is not (".zip" or ".cue" or ".m3u"))
                .ToList();

            if (roms.Count <= 1)
            {
                single.Platform = roms.Count == 1 ? PlatformOfEntry(roms[0], hint) : null;
                if (single.Platform == null && hintTakesZip) single.Platform = hint!.Name;
                if (roms.Count == 1) single.FileHash = roms[0].Crc32.ToString("X8");
                if (single.Platform == null)
                {
                    single.Status = ScanStatus.Unknown;
                    single.Message = roms.Count == 0
                        ? "File .zip không chứa ROM nào nhận ra được."
                        : "Không xác định được hệ máy của ROM trong file .zip.";
                }
                return new() { single };
            }

            var games = new List<ScannedGame>();
            foreach (var e in roms)
            {
                var key = $"{Path.GetFullPath(zip)}#{e.FullName}";
                var g = new ScannedGame
                {
                    Title = NameCleaner.Clean(Path.GetFileNameWithoutExtension(e.Name)),
                    Platform = PlatformOfEntry(e, hint),
                    LaunchFile = key,
                    SourceFile = key,
                    FolderPath = Path.GetDirectoryName(Path.GetFullPath(zip))!,
                    FileSize = e.Length,
                    FileHash = e.Crc32.ToString("X8"),
                };
                if (g.Platform == null)
                {
                    g.Status = ScanStatus.Unknown;
                    g.Message = $"Không nhận ra ROM \"{e.Name}\" trong {Path.GetFileName(zip)}.";
                }
                games.Add(g);
            }
            return games;
        }
        catch (InvalidDataException)
        {
            single.Status = ScanStatus.Unknown;
            single.Message = "File .zip bị hỏng.";
            return new() { single };
        }
    }

    /// <summary>File thật trên ổ đĩa (bỏ phần "#rom" của đường dẫn bên trong zip).</summary>
    public static string PhysicalPath(string path)
    {
        int i = path.IndexOf(".zip#", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? path : path[..(i + 4)];
    }

    private static string? HashFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > MaxHashSize) return null;
            using var fs = fi.OpenRead();
            return Crc32.ComputeHex(fs);
        }
        catch { return null; }
    }

    /// <summary>
    /// Ghi file sinh ra (.m3u / .cue tạm) vào Playlists/&lt;platform&gt;/ (UTF-8 không BOM).
    /// Trùng tên với game khác thì thêm mã ngắn từ đường dẫn gốc.
    /// </summary>
    private string WriteGenerated(string platform, string title, string ext, string content, string sourceKey)
    {
        var dir = Path.Combine(_playlistsDir, platform);
        Directory.CreateDirectory(dir);
        var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "game";

        var path = Path.Combine(dir, safe + ext);
        if (File.Exists(path) && File.ReadAllText(path) != content)
        {
            var h = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sourceKey.ToLowerInvariant())))[..8];
            path = Path.Combine(dir, $"{safe} [{h}]{ext}");
        }
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
