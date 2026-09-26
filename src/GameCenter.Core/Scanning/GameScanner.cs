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
                    dirGames.Add(ClassifyZip(f, hint));
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
            g.LaunchFile = WriteGenerated("PS1", g.Title, ".m3u", string.Join("\r\n", g.Discs) + "\r\n", g.SourceFile);

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

    public static bool HasMegaDriveHeader(Stream s)
    {
        var buf = new byte[4];
        if (s.CanSeek)
        {
            if (s.Length < 0x104) return false;
            s.Seek(0x100, SeekOrigin.Begin);
        }
        else
        {
            var skip = new byte[0x100];
            if (s.ReadAtLeast(skip, 0x100, throwOnEndOfStream: false) < 0x100) return false;
        }
        return s.ReadAtLeast(buf, 4, throwOnEndOfStream: false) == 4 && Encoding.ASCII.GetString(buf) == "SEGA";
    }

    private ScannedGame ClassifyLooseBin(string bin, PlatformDefinition? hint)
    {
        var size = new FileInfo(bin).Length;
        bool isMd;
        if (hint?.Name == "MegaDrive") isMd = true;
        else if (hint?.Name == "PS1") isMd = false;
        else { using var fs = File.OpenRead(bin); isMd = HasMegaDriveHeader(fs); }

        if (isMd)
        {
            var g = NewGame(bin, "MegaDrive", Path.GetFileNameWithoutExtension(bin), size);
            g.FileHash = HashFile(bin);
            return g;
        }

        // PS1 .bin đơn → sinh .cue tạm trong Playlists/PS1 (mục 8.4)
        var ps1 = NewGame(bin, "PS1", Path.GetFileNameWithoutExtension(bin), size);
        ps1.LaunchFile = WriteGenerated("PS1", ps1.Title, ".cue", CueParser.BuildTempCue(ps1.SourceFile), ps1.SourceFile);
        if (hint?.Name != "PS1") ps1.Message = "File .bin không có .cue, đoán là PS1.";
        return ps1;
    }

    private ScannedGame ClassifyZip(string zip, PlatformDefinition? hint)
    {
        var g = NewGame(zip, null, Path.GetFileNameWithoutExtension(zip), new FileInfo(zip).Length);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            var roms = archive.Entries
                .Where(e => e.Length > 0 && _catalog.IsKnownExtension(Path.GetExtension(e.Name).ToLowerInvariant())
                            && Path.GetExtension(e.Name).ToLowerInvariant() != ".zip")
                .ToList();

            if (hint != null && hint.Extensions.Contains(".zip"))
            {
                g.Platform = hint.Name;
            }
            else if (roms.Count == 1)
            {
                var entry = roms[0];
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext == ".bin")
                {
                    using var s = entry.Open();
                    g.Platform = HasMegaDriveHeader(s) ? "MegaDrive" : null;
                }
                else
                {
                    var p = _catalog.UniqueForExtension(ext);
                    if (p != null && p.Extensions.Contains(".zip")) g.Platform = p.Name;
                }
            }

            if (roms.Count == 1) g.FileHash = roms[0].Crc32.ToString("X8");

            if (g.Platform == null)
            {
                g.Status = ScanStatus.Unknown;
                g.Message = roms.Count switch
                {
                    0 => "File .zip không chứa ROM nào nhận ra được.",
                    1 => "Không xác định được hệ máy của ROM trong file .zip.",
                    _ => $"File .zip chứa {roms.Count} ROM, hãy chọn hệ máy.",
                };
            }
        }
        catch (InvalidDataException)
        {
            g.Status = ScanStatus.Unknown;
            g.Message = "File .zip bị hỏng.";
        }
        return g;
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
