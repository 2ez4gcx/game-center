using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GameCenter.Core.Config;
using GameCenter.Core.Localization;
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
/// Các trường hợp file nén / định dạng lạ: xem docs/nhan-dien-file-nen.md.
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

    /// <summary>File BIOS người dùng chép nhầm vào thư mục Games: không hiện như game, Game Center chép sang thư mục BIOS.</summary>
    public List<string> BiosFiles { get; } = new();

    public List<ScannedGame> Scan()
    {
        var result = new List<ScannedGame>();
        BiosFiles.Clear();
        if (!Directory.Exists(_gamesDir)) return result;

        foreach (var dir in EnumerateDirs(_gamesDir))
        {
            try { ScanDirectory(dir, result); }
            catch (Exception ex) { Log.Error($"Lỗi khi quét {dir}", ex); }
        }
        MergeDiscFolders(result);

        // Sinh playlist .m3u cho game nhiều đĩa (sau khi đã có tên cuối cùng); .m3u người dùng tự tạo thì giữ nguyên.
        // Tên .m3u theo file đĩa 1 bỏ phần "(Disc N)": RetroArch đặt tên thẻ nhớ theo file chạy → mọi đĩa dùng chung một thẻ nhớ.
        foreach (var g in result.Where(g => g.Platform != null && g.Discs.Count >= 2 && Ext(g.LaunchFile) != ".m3u"))
        {
            try { g.LaunchFile = WriteGenerated(g.Platform!, NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(g.SourceFile)), ".m3u", string.Join("\r\n", g.Discs) + "\r\n", g.SourceFile); }
            catch (Exception ex) { Log.Error($"Không ghi được playlist cho {g.SourceFile}", ex); }
        }
        return result;
    }

    /// <summary>
    /// Game nhiều đĩa để mỗi đĩa một thư mục, file bên trong có thể không ghi số đĩa:
    /// "Tên (Disc 1) (VN)/LOD.cue", "Tên (Disc 2) (VN)/LOD.cue"... hoặc "Tên/Disc 1/", "Tên/Disc 2/".
    /// Các thư mục anh em cùng tên (bỏ phần số đĩa) → ghép thành một game: đổi đĩa được và dùng chung thẻ nhớ.
    /// </summary>
    private void MergeDiscFolders(List<ScannedGame> games)
    {
        // Tên mang số đĩa: tên file nếu có, không thì tên thư mục chứa
        static string? DiscName(ScannedGame g)
        {
            var file = Path.GetFileNameWithoutExtension(g.SourceFile);
            if (NameCleaner.DiscNumberOf(file) != null) return file;
            var folder = Path.GetFileName(g.FolderPath);
            return NameCleaner.DiscNumberOf(folder) != null ? folder : null;
        }
        var groups = games
            .Where(g => g.Status == ScanStatus.Ok && g.Discs.Count == 0 && _catalog.IsDiscPlatform(g.Platform)
                        && !g.SourceFile.Contains(".zip#", StringComparison.OrdinalIgnoreCase) && DiscName(g) != null)
            .GroupBy(g => g.Platform + "|" + Path.GetDirectoryName(g.FolderPath) + "|" + NameCleaner.StripDisc(DiscName(g)!), StringComparer.OrdinalIgnoreCase)
            // Chỉ ghép khi các đĩa nằm ở thư mục khác nhau (cùng thư mục đã ghép ở MergeLooseDiscs) và không trùng số đĩa
            .Where(grp => grp.Count() >= 2
                          && grp.Select(g => g.FolderPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == grp.Count()
                          && grp.Select(g => NameCleaner.DiscNumberOf(DiscName(g)!)).Distinct().Count() == grp.Count())
            .ToList();
        foreach (var grp in groups)
        {
            var discs = grp.OrderBy(g => NameCleaner.DiscNumberOf(DiscName(g)!)).ToList();
            var name = NameCleaner.StripDisc(DiscName(discs[0])!);
            // "Tên/Disc 1/": tên game là thư mục cha
            var parent = Path.GetDirectoryName(discs[0].FolderPath)!;
            if (name.Length == 0 && !string.Equals(Path.GetFullPath(parent), _gamesDir, StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileName(parent);
            if (name.Length == 0) name = NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(discs[0].SourceFile));

            var merged = NewGame(discs[0].SourceFile, discs[0].Platform, name, discs.Sum(d => d.FileSize));
            merged.LaunchFile = discs[0].LaunchFile;
            merged.Discs.AddRange(discs.Select(d => d.LaunchFile));
            games.RemoveAll(discs.Contains);
            games.Add(merged);
        }
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
            foreach (var s in subs.Where(s => !IsJunkDir(Path.GetFileName(s))).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
                stack.Push(s);
        }
    }

    /// <summary>Thư mục rác của macOS / Windows / USB: __MACOSX, .Trashes, $RECYCLE.BIN, System Volume Information.</summary>
    private static bool IsJunkDir(string name) =>
        name.StartsWith('.') || name.StartsWith('$') ||
        name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);

    /// <summary>File rác: "._tên" (AppleDouble của macOS, cùng đuôi với ROM thật), .DS_Store, Thumbs.db.</summary>
    private static bool IsJunkFile(string name) =>
        name.StartsWith("._") || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    /// <summary>Platform theo thư mục người dùng đã đặt (ưu tiên 1).</summary>
    private PlatformDefinition? FolderHint(string dir)
    {
        var rel = Path.GetRelativePath(_gamesDir, dir);
        if (rel == ".") return null;
        return FolderHintOf(rel.Split(Path.DirectorySeparatorChar));
    }

    private PlatformDefinition? FolderHintOf(IEnumerable<string> segments)
    {
        foreach (var seg in segments)
        {
            var p = _catalog.FromFolderName(seg);
            if (p != null) return p;
        }
        return null;
    }

    private static string Ext(string f) => Path.GetExtension(f).ToLowerInvariant();

    private void ScanDirectory(string dir, List<ScannedGame> result)
    {
        var files = Directory.GetFiles(dir).Where(f => !IsJunkFile(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) return;

        var hint = FolderHint(dir);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirGames = new List<ScannedGame>();

        // 1. .m3u người dùng tự tạo: hệ máy theo đĩa đầu tiên
        foreach (var m3u in files.Where(f => Ext(f) == ".m3u"))
        {
            consumed.Add(m3u);
            var g = NewGame(m3u, null, Path.GetFileNameWithoutExtension(m3u));
            g.Title = NameCleaner.Clean(NameCleaner.StripDisc(g.Title));
            string? firstData = null;
            foreach (var line in File.ReadAllLines(m3u).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                var p = Path.IsPathRooted(line) ? line : Path.Combine(dir, line);
                if (!File.Exists(p)) { g.Status = ScanStatus.BrokenCue; g.Message = Loc.T("scan.playlist_missing", line); continue; }
                g.Discs.Add(Path.GetFullPath(p));
                consumed.Add(Path.GetFullPath(p));
                var data = Path.GetFullPath(p);
                if (Ext(p) == ".cue")
                {
                    var cue = CueParser.Parse(p);
                    ConsumeCue(cue, consumed, g);
                    data = DataTrack(cue);
                }
                if (g.Discs.Count == 1) firstData = data;
            }
            g.Platform = DiscPlatform(firstData, hint);
            dirGames.Add(g);
        }

        // 2. .cue: PS1 hoặc PS2 theo nội dung track dữ liệu; ghép nhiều đĩa cùng game
        var cues = files.Where(f => Ext(f) == ".cue" && !consumed.Contains(f)).Select(CueParser.Parse).ToList();
        foreach (var cue in cues) consumed.Add(cue.CuePath);
        var cuePlatform = cues.ToDictionary(c => c, c => DiscPlatform(DataTrack(c), hint));

        var multiDisc = cues
            .Where(c => NameCleaner.DiscNumberOf(Path.GetFileNameWithoutExtension(c.CuePath)) != null)
            .GroupBy(c => cuePlatform[c] + "|" + NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(c.CuePath)), StringComparer.OrdinalIgnoreCase)
            .Where(grp => grp.Count() >= 2)
            .ToList();
        var inMulti = multiDisc.SelectMany(g => g).ToHashSet();

        foreach (var grp in multiDisc)
        {
            var discs = grp.OrderBy(c => NameCleaner.DiscNumberOf(Path.GetFileNameWithoutExtension(c.CuePath))).ToList();
            var g = NewGame(discs[0].CuePath, cuePlatform[discs[0]], NameCleaner.StripDisc(Path.GetFileNameWithoutExtension(discs[0].CuePath)));
            foreach (var c in discs)
            {
                g.Discs.Add(c.CuePath);
                ConsumeCue(c, consumed, g);
            }
            dirGames.Add(g);
        }
        foreach (var cue in cues.Where(c => !inMulti.Contains(c)))
        {
            var g = NewGame(cue.CuePath, cuePlatform[cue], Path.GetFileNameWithoutExtension(cue.CuePath));
            ConsumeCue(cue, consumed, g);
            dirGames.Add(g);
        }

        // 3. .ccd (CloneCD): ảnh đĩa .img + .sub cùng tên. Beetle PSX mở .ccd; LRPS2 mở .img.
        foreach (var ccd in files.Where(f => Ext(f) == ".ccd" && !consumed.Contains(f)))
            dirGames.Add(ClassifyCcd(ccd, files, consumed, hint));

        // 4. Đĩa nhiều track (Redump) bị mất .cue: "Tên (Track 1).bin", "Tên (Track 2).bin"... → sinh .cue tạm
        dirGames.AddRange(ClassifyTrackBins(files, consumed, hint));

        // 5. Từng file còn lại
        foreach (var f in files.Where(f => !consumed.Contains(f)))
        {
            var name = Path.GetFileName(f);
            if (KnownFormats.IsLaterPart(name)) continue;
            switch (Ext(f))
            {
                case ".pbp":
                    dirGames.Add(ClassifyPbp(f));
                    break;

                case ".chd":
                    dirGames.Add(ClassifyChd(f, hint));
                    break;

                // Ảnh đĩa LRPS2 mở được, Beetle PSX thì không
                case ".iso":
                case ".cso":
                case ".mdf":
                case ".nrg":
                    if (Ext(f) == ".mdf" && Sibling(files, f, ".mds") is { } mds) consumed.Add(mds);
                    dirGames.Add(ClassifyDiscImage(f, hint));
                    break;

                // .bin / .img không có .cue, .rom: nhận diện theo nội dung
                case ".bin":
                case ".img":
                case ".rom":
                    if (ClassifyLooseFile(f, hint) is { } loose) dirGames.Add(loose);
                    break;

                // .zip: xem nội dung bên trong
                case ".zip":
                    dirGames.AddRange(ClassifyZip(f, hint));
                    break;

                default:
                    var ext = Ext(f);
                    var platform = hint != null && hint.Extensions.Contains(ext) ? hint : _catalog.UniqueForExtension(ext);
                    if (platform != null)
                    {
                        var g = NewGame(f, platform.Name, Path.GetFileNameWithoutExtension(f), size: new FileInfo(f).Length);
                        g.FileHash = HashFile(f);
                        dirGames.Add(g);
                    }
                    // File nén khác zip, file nén nhiều phần, .ecm, game của máy chưa hỗ trợ: báo rõ thay vì bỏ qua.
                    // Còn lại (ảnh, txt, save...) không phải game → bỏ qua.
                    else if (NotPlayable(f) is { } unknown) dirGames.Add(unknown);
                    break;
            }
        }

        // 6. Ghép đĩa rời của cùng game ("Tên (Disc 1).chd", "Tên (Disc 2).chd"...) thành một game nhiều đĩa
        MergeLooseDiscs(dirGames);

        // Game đĩa (PS1, PS2) nằm một mình trong thư mục riêng: lấy tên thư mục làm tên game (ví dụ "Final Fantasy VII/ff7.cue")
        if (dirGames.Count == 1 && _catalog.IsDiscPlatform(dirGames[0].Platform)
            && !string.Equals(Path.GetFullPath(dir), _gamesDir, StringComparison.OrdinalIgnoreCase)
            && _catalog.FromFolderName(Path.GetFileName(dir)) == null)
        {
            dirGames[0].Title = NameCleaner.Clean(Path.GetFileName(dir));
        }

        // Playlist .m3u cho game nhiều đĩa được sinh sau khi quét hết (Scan), vì đĩa có thể nằm ở các thư mục khác nhau
        result.AddRange(dirGames);
    }

    /// <summary>File cùng tên, khác đuôi, trong cùng thư mục (không phân biệt hoa thường).</summary>
    private static string? Sibling(List<string> files, string file, string ext)
    {
        var baseName = Path.GetFileNameWithoutExtension(file);
        return files.FirstOrDefault(f => Ext(f) == ext &&
            Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File dữ liệu của đĩa trong .cue: file của track đầu tiên (track dữ liệu).</summary>
    private static string? DataTrack(CueInfo cue) => cue.Files.FirstOrDefault()?.ResolvedPath;

    /// <summary>
    /// Hệ máy của game đĩa (PS1 / PS2) theo nội dung file dữ liệu. Không đọc được nội dung
    /// thì tin theo thư mục người dùng đã đặt (Games/PS2/...), còn lại là PS1 như trước.
    /// </summary>
    private string DiscPlatform(string? dataFile, PlatformDefinition? hint)
    {
        var kind = dataFile == null ? DiscKind.Unknown : Ext(dataFile) switch
        {
            ".chd" => ChdFile.ReadMedia(dataFile) == ChdMedia.Dvd ? DiscKind.Ps2 : DiscKind.Unknown,
            ".pbp" => DiscKind.Ps1,
            _ => DiscImage.Detect(dataFile),
        };
        return kind switch
        {
            DiscKind.Ps2 => "PS2",
            DiscKind.Ps1 => "PS1",
            _ => hint?.Name == "PS2" ? "PS2" : "PS1",
        };
    }

    private static void MarkUnknown(ScannedGame g, string message)
    {
        g.Platform = null;
        g.Status = ScanStatus.Unknown;
        g.Message = message;
    }

    /// <summary>
    /// .chd: metadata cho biết đĩa DVD (chỉ có thể là PS2) hay CD. Đĩa CD đa số là PS1;
    /// game PS2 dạng CD thì nhận theo thư mục PS2 (không giải nén .chd để đọc SYSTEM.CNF).
    /// </summary>
    private ScannedGame ClassifyChd(string chd, PlatformDefinition? hint)
    {
        var media = ChdFile.ReadMedia(chd);
        string? platform = media switch
        {
            ChdMedia.Dvd => "PS2",
            ChdMedia.Other => _catalog.IsDiscPlatform(hint?.Name) ? hint!.Name : null,
            _ => hint?.Name == "PS2" ? "PS2" : "PS1",
        };
        var g = NewGame(chd, platform, Path.GetFileNameWithoutExtension(chd), new FileInfo(chd).Length);
        if (platform == null) MarkUnknown(g, Loc.T("scan.other_cd"));
        return g;
    }

    /// <summary>.iso / .cso / .mdf / .nrg: đọc SYSTEM.CNF để biết đĩa PS2. Không đọc được thì tin theo thư mục PS2.</summary>
    private ScannedGame ClassifyDiscImage(string file, PlatformDefinition? hint)
    {
        var kind = DiscImage.Detect(file);
        string? platform = kind == DiscKind.Ps2 || (kind is DiscKind.Unknown or DiscKind.Other && hint?.Name == "PS2") ? "PS2" : null;
        var g = NewGame(file, platform, Path.GetFileNameWithoutExtension(file), new FileInfo(file).Length);
        if (platform == null)
        {
            MarkUnknown(g, kind switch
            {
                // Beetle PSX chỉ mở .cue/.chd/.pbp/.ccd; .iso của đĩa PS1 còn mất cả dữ liệu Mode 2 (âm thanh XA)
                DiscKind.Ps1 => Loc.T("scan.ps1_format", Ext(file)),
                DiscKind.Psp => Loc.T("scan.psp"),
                DiscKind.Other => Loc.T("scan.other_cd"),
                _ => Loc.T("scan.unknown_image"),
            });
        }
        return g;
    }

    /// <summary>
    /// EBOOT.PBP: game PS1 chuyển đổi cho PSP (Beetle PSX chạy được) hoặc game PSP (chưa hỗ trợ).
    /// Header "\0PBP", vị trí phần PSAR ở 0x24; PS1 có "PSISOIMG" / "PSTITLEIMG" ở đầu PSAR.
    /// </summary>
    private ScannedGame ClassifyPbp(string pbp)
    {
        var g = NewGame(pbp, "PS1", Path.GetFileNameWithoutExtension(pbp), new FileInfo(pbp).Length);
        try
        {
            using var fs = File.OpenRead(pbp);
            var h = ReadHead(fs, 0x28);
            if (h.Length < 0x28 || h[0] != 0 || !Ascii(h, 1, "PBP")) return g; // không đọc được header → coi như PS1 như trước
            long psar = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(0x24));
            string marker = "";
            if (psar + 16 <= fs.Length)
            {
                fs.Position = psar;
                marker = Encoding.ASCII.GetString(ReadHead(fs, 16));
            }
            if (!marker.StartsWith("PSISOIMG") && !marker.StartsWith("PSTITLEIMG")) MarkUnknown(g, Loc.T("scan.psp"));
        }
        catch (IOException) { }
        return g;
    }

    /// <summary>.ccd (CloneCD) + .img + .sub cùng tên.</summary>
    private ScannedGame ClassifyCcd(string ccd, List<string> files, HashSet<string> consumed, PlatformDefinition? hint)
    {
        consumed.Add(ccd);
        var img = Sibling(files, ccd, ".img");
        if (img != null) consumed.Add(img);
        if (Sibling(files, ccd, ".sub") is { } sub) consumed.Add(sub);

        var platform = DiscPlatform(img, hint);
        var g = NewGame(ccd, platform, Path.GetFileNameWithoutExtension(ccd), img != null ? new FileInfo(img).Length : 0);
        if (img == null)
        {
            g.Status = ScanStatus.BrokenCue;
            g.Message = Loc.T("scan.cue_missing", Path.GetFileName(ccd), Path.GetFileNameWithoutExtension(ccd) + ".img");
        }
        else if (platform == "PS2") g.LaunchFile = Path.GetFullPath(img);
        return g;
    }

    /// <summary>
    /// Đĩa nhiều track bị mất .cue (đặt tên kiểu Redump "Tên (Track 1).bin"...): track 1 là đĩa PS1 / PS2
    /// thì sinh .cue tạm (track 1 dữ liệu, các track sau là nhạc) để chạy được kèm nhạc nền.
    /// </summary>
    private List<ScannedGame> ClassifyTrackBins(List<string> files, HashSet<string> consumed, PlatformDefinition? hint)
    {
        var games = new List<ScannedGame>();
        var groups = files
            .Where(f => Ext(f) == ".bin" && !consumed.Contains(f) && NameCleaner.TrackNumberOf(Path.GetFileNameWithoutExtension(f)) != null)
            .GroupBy(f => NameCleaner.StripTrack(Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
        foreach (var grp in groups)
        {
            var tracks = grp.OrderBy(f => NameCleaner.TrackNumberOf(Path.GetFileNameWithoutExtension(f))).ToList();
            if (NameCleaner.TrackNumberOf(Path.GetFileNameWithoutExtension(tracks[0])) != 1) continue;
            BinKind kind;
            using (var fs = File.OpenRead(tracks[0])) kind = DetectBin(fs, out _);
            if (kind == BinKind.Unknown && hint?.Name is "PS1" or "PS2") kind = hint!.Name == "PS2" ? BinKind.Ps2Disc : BinKind.Ps1Disc;
            if (kind is not (BinKind.Ps1Disc or BinKind.Ps2Disc)) continue;

            var platform = kind == BinKind.Ps2Disc ? "PS2" : "PS1";
            var g = NewGame(tracks[0], platform, grp.Key, tracks.Sum(t => new FileInfo(t).Length));
            g.LaunchFile = WriteGenerated(platform, grp.Key, ".cue", CueParser.BuildMultiTrackCue(tracks), g.SourceFile);
            foreach (var t in tracks) consumed.Add(t);
            games.Add(g);
        }
        return games;
    }

    /// <summary>
    /// Ghép các game đĩa nằm rời nhưng cùng tên, khác số đĩa ("(Disc 1)", "CD2"...) thành một game nhiều đĩa:
    /// đổi đĩa được khi đang chơi (playlist .m3u). Áp dụng cho .chd, .pbp, .iso, .bin không có .cue...
    /// </summary>
    private void MergeLooseDiscs(List<ScannedGame> dirGames)
    {
        string BaseName(ScannedGame g) => Path.GetFileNameWithoutExtension(g.SourceFile);
        var groups = dirGames
            .Where(g => g.Status == ScanStatus.Ok && g.Discs.Count == 0 && _catalog.IsDiscPlatform(g.Platform)
                        && !g.SourceFile.Contains(".zip#", StringComparison.OrdinalIgnoreCase)
                        && NameCleaner.DiscNumberOf(BaseName(g)) != null)
            .GroupBy(g => g.Platform + "|" + NameCleaner.StripDisc(BaseName(g)), StringComparer.OrdinalIgnoreCase)
            .Where(grp => grp.Count() >= 2)
            .ToList();
        foreach (var grp in groups)
        {
            var discs = grp.OrderBy(g => NameCleaner.DiscNumberOf(BaseName(g))).ToList();
            var merged = NewGame(discs[0].SourceFile, discs[0].Platform, NameCleaner.StripDisc(BaseName(discs[0])), discs.Sum(d => d.FileSize));
            merged.LaunchFile = discs[0].LaunchFile;
            merged.Discs.AddRange(discs.Select(d => d.LaunchFile));
            dirGames.RemoveAll(discs.Contains);
            dirGames.Add(merged);
        }
    }

    /// <summary>File không chạy được nhưng nhận ra: báo cách xử lý trong mục "Chưa nhận ra".</summary>
    private ScannedGame? NotPlayable(string file)
    {
        var name = Path.GetFileName(file);
        var kind = KnownFormats.Classify(name, out var console);
        if (kind == KnownFormats.Kind.None) return null;
        var g = NewGame(file, null, Path.GetFileNameWithoutExtension(file), new FileInfo(file).Length);
        MarkUnknown(g, kind switch
        {
            KnownFormats.Kind.Archive => Loc.T("scan.extract_first", name),
            KnownFormats.Kind.MultiPart => Loc.T("scan.multipart"),
            KnownFormats.Kind.Ecm => Loc.T("scan.ecm"),
            _ => Loc.T("scan.unsupported_console", console),
        });
        // "Tên.7z" / "Tên.part1.rar" → tên game không có phần đuôi nén
        g.Title = NameCleaner.Clean(StripArchiveSuffix(Path.GetFileNameWithoutExtension(file)));
        return g;
    }

    private static string StripArchiveSuffix(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"(\.(zip|7z|rar|part0*1|tar))+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
            g.Message = Loc.T("scan.cue_empty", Path.GetFileName(cue.CuePath));
        }
        else if (cue.Missing.Any())
        {
            g.Status = ScanStatus.BrokenCue;
            g.Message = Loc.T("scan.cue_missing", Path.GetFileName(cue.CuePath), string.Join(", ", cue.Missing.Select(m => m.NameInCue)));
        }
    }

    // ------------------------------------------------------------------ Nhận diện theo nội dung

    public static byte[] ReadHead(Stream s, int size)
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

    public enum BinKind { Unknown, Rom, MegaDrive, Ps1Disc, Ps2Disc, OtherCd }

    /// <summary>
    /// Nhận diện file .bin / .img / .rom theo nội dung:
    /// - Ảnh đĩa CD (sector thô 2352 byte): PS1 hoặc PS2 theo SYSTEM.CNF (xem <see cref="DiscImage"/>);
    ///   đĩa CD của máy khác (Sega CD, Saturn...) → chưa hỗ trợ.
    /// - ROM băng theo header (<see cref="RomHeader"/>): Mega Drive, NES, GB/GBC, GBA, Master System, Game Gear...
    ///   <paramref name="romPlatform"/> là hệ máy nhận ra được.
    /// - Ảnh đĩa DVD PS2 (sector 2048 byte) bị đặt đuôi .bin.
    /// </summary>
    public static BinKind DetectBin(Stream s, out string? romPlatform)
    {
        romPlatform = null;
        s.Position = 0;
        var head = ReadHead(s, RomHeader.HeadSize);
        if (DiscImage.HasCdSync(head))
        {
            return DiscImage.Detect(s) switch
            {
                DiscKind.Ps1 => BinKind.Ps1Disc,
                DiscKind.Ps2 => BinKind.Ps2Disc,
                _ => BinKind.OtherCd,
            };
        }
        romPlatform = RomHeader.Detect(head);
        if (romPlatform == RomHeader.MegaDrive) return BinKind.MegaDrive;
        if (romPlatform != null) return BinKind.Rom;
        return DiscImage.Detect(s) == DiscKind.Ps2 ? BinKind.Ps2Disc : BinKind.Unknown;
    }

    /// <summary>
    /// File BIOS chép nhầm vào Games: BIOS PS2 (nhận theo nội dung) hoặc file có tên trong danh sách BIOS
    /// của platforms.json (ví dụ scph5501.bin).
    /// </summary>
    private bool IsBiosFile(string file, long size)
    {
        if (size is >= Emulation.Ps2Bios.MinSize and <= Emulation.Ps2Bios.MaxSize && Emulation.Ps2Bios.IsBios(file)) return true;
        var name = Path.GetFileName(file);
        return size <= 1024 * 1024 && _catalog.Platforms.Any(p => p.Bios?.Files.Contains(name, StringComparer.OrdinalIgnoreCase) == true);
    }

    /// <summary>.bin / .img (không có .cue) và .rom. Trả về null nếu là file BIOS (ghi vào <see cref="BiosFiles"/>).</summary>
    private ScannedGame? ClassifyLooseFile(string file, PlatformDefinition? hint)
    {
        var size = new FileInfo(file).Length;
        if (IsBiosFile(file, size))
        {
            BiosFiles.Add(file);
            return null;
        }

        BinKind kind;
        string? rom;
        using (var fs = File.OpenRead(file)) kind = DetectBin(fs, out rom);
        // Không nhận ra từ nội dung: tin theo thư mục hệ máy người dùng đã đặt
        if (kind == BinKind.Unknown && hint != null)
        {
            kind = hint.Name switch
            {
                "MegaDrive" => BinKind.MegaDrive,
                "PS1" => BinKind.Ps1Disc,
                "PS2" => BinKind.Ps2Disc,
                _ => BinKind.Rom,
            };
            if (kind == BinKind.Rom) rom = hint.Name;
        }

        var title = Path.GetFileNameWithoutExtension(file);
        switch (kind)
        {
            case BinKind.MegaDrive:
            case BinKind.Rom when rom != null && _catalog.Get(rom) != null:
                var g = NewGame(file, kind == BinKind.MegaDrive ? "MegaDrive" : rom, title, size);
                g.FileHash = HashFile(file);
                return g;

            case BinKind.Ps1Disc:
                // PS1 .bin đơn → sinh .cue tạm trong Playlists/PS1 (mục 8.4)
                var ps1 = NewGame(file, "PS1", title, size);
                // Đặt theo tên file gốc (không phải tên đã làm sạch): RetroArch đặt tên save theo file chạy,
                // giữ tên gốc thì save không đổi khi tên hiển thị thay đổi.
                ps1.LaunchFile = WriteGenerated("PS1", title, ".cue", CueParser.BuildTempCue(ps1.SourceFile), ps1.SourceFile);
                return ps1;

            case BinKind.Ps2Disc:
                // LRPS2 (PCSX2) tự nhận cỡ sector của .bin / .img, chạy thẳng không cần .cue tạm
                return NewGame(file, "PS2", title, size);

            default:
                var u = NewGame(file, null, title, size);
                MarkUnknown(u, kind switch
                {
                    BinKind.OtherCd => Loc.T("scan.other_cd"),
                    BinKind.Rom => Loc.T("scan.unsupported_console", rom),
                    _ => Loc.T(Ext(file) == ".bin" ? "scan.unknown_bin" : "scan.unknown_image"),
                });
                return u;
        }
    }

    /// <summary>Đuôi file của game đĩa: nằm trong .zip thì không chạy được, phải giải nén.</summary>
    private static readonly HashSet<string> DiscExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cue", ".iso", ".cso", ".zso", ".chd", ".pbp", ".m3u", ".ccd", ".img", ".mdf", ".nrg" };

    /// <summary>Đuôi file tài liệu / ảnh hay đi kèm game trong file nén.</summary>
    private static readonly HashSet<string> ExtraExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".nfo", ".diz", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".pdf", ".htm", ".html", ".url", ".doc", ".md", ".xml", ".sfv", ".md5" };

    /// <summary>Mục rác trong file nén: thư mục __MACOSX và file "._tên" của macOS.</summary>
    private static bool IsJunkEntry(ZipArchiveEntry e) =>
        e.FullName.Contains("__MACOSX/", StringComparison.OrdinalIgnoreCase) || IsJunkFile(e.Name);

    /// <summary>Nhận diện một ROM bên trong file .zip (cả thư mục bên trong zip, ví dụ "SNES/game.sfc", cũng là gợi ý).</summary>
    private string? PlatformOfEntry(ZipArchiveEntry entry, PlatformDefinition? hint)
    {
        hint = FolderHintOf(entry.FullName.Split('/').SkipLast(1)) ?? hint;
        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (ext is ".bin" or ".rom" or "")
        {
            // Đĩa PS1 / PS2 trong zip không chạy trực tiếp được → không nhận
            using var s = entry.Open();
            var rom = RomHeader.Detect(ReadHead(s, RomHeader.HeadSize));
            if (rom != null && _catalog.Get(rom) is { } p && p.Extensions.Contains(".zip")) return p.Name;
            return hint?.Name == "MegaDrive" ? hint.Name : null;
        }
        if (hint != null && hint.Extensions.Contains(ext) && hint.Extensions.Contains(".zip")) return hint.Name;
        var unique = _catalog.UniqueForExtension(ext);
        return unique != null && unique.Extensions.Contains(".zip") ? unique.Name : null;
    }

    /// <summary>Mục trong zip là đĩa CD thô (PS1 / PS2 .bin không kèm .cue).</summary>
    private static bool IsCdImageEntry(ZipArchiveEntry e)
    {
        if (Path.GetExtension(e.Name).ToLowerInvariant() is not (".bin" or ".img")) return false;
        using var s = e.Open();
        return DiscImage.HasCdSync(ReadHead(s, 16));
    }

    /// <summary>
    /// .zip một ROM → một game. .zip nhiều ROM → mỗi ROM một game,
    /// chạy bằng cú pháp của RetroArch "file.zip#rom.ext".
    /// Các trường hợp không chạy được (zip có mật khẩu, chứa game đĩa, chứa file nén khác, game arcade...) → báo rõ.
    /// </summary>
    private List<ScannedGame> ClassifyZip(string zip, PlatformDefinition? hint)
    {
        var name = Path.GetFileName(zip);
        var single = NewGame(zip, null, Path.GetFileNameWithoutExtension(zip), new FileInfo(zip).Length);
        bool hintTakesZip = hint != null && hint.Extensions.Contains(".zip");

        // Đuôi .zip nhưng thật ra là 7z / rar (đổi đuôi nhầm)
        using (var fs = File.OpenRead(zip))
        {
            if (KnownFormats.IsOtherArchive(ReadHead(fs, 8)))
            {
                MarkUnknown(single, Loc.T("scan.extract_first", name));
                return new() { single };
            }
        }

        bool opened = false;
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            opened = true;
            var entries = archive.Entries.Where(e => e.Length > 0 && !string.IsNullOrEmpty(e.Name) && !IsJunkEntry(e)).ToList();
            if (entries.Any(e => e.IsEncrypted))
            {
                MarkUnknown(single, Loc.T("scan.zip_encrypted"));
                return new() { single };
            }

            var roms = entries
                .Where(e => (_catalog.IsKnownExtension(Path.GetExtension(e.Name).ToLowerInvariant()) || Ext(e.Name) == ".rom")
                            && !DiscExtensions.Contains(Path.GetExtension(e.Name)) && Ext(e.Name) != ".zip")
                .ToList();
            bool discInside = entries.Any(e => DiscExtensions.Contains(Path.GetExtension(e.Name)));

            if (roms.Count <= 1)
            {
                single.Platform = roms.Count == 1 ? PlatformOfEntry(roms[0], hint) : null;
                if (single.Platform == null && hintTakesZip && !discInside) single.Platform = hint!.Name;
                if (roms.Count == 1) single.FileHash = roms[0].Crc32.ToString("X8");
                if (single.Platform == null)
                {
                    MarkUnknown(single,
                        discInside || (roms.Count == 1 && IsCdImageEntry(roms[0])) ? Loc.T("scan.zip_disc") :
                        entries.Any(e => KnownFormats.Classify(e.Name, out _) is KnownFormats.Kind.Archive or KnownFormats.Kind.MultiPart || Ext(e.Name) == ".zip")
                            ? Loc.T("scan.extract_first", name) :
                        roms.Count == 0 && LooksLikeArcade(entries) ? Loc.T("scan.arcade") :
                        roms.Count == 0 ? Loc.T("scan.zip_empty") : Loc.T("scan.zip_unknown"));
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
                    MarkUnknown(g, IsCdImageEntry(e) ? Loc.T("scan.zip_disc") : Loc.T("scan.zip_entry_unknown", e.Name, name));
                games.Add(g);
            }
            return games;
        }
        catch (InvalidDataException)
        {
            // - Mở được zip nhưng không đọc được ROM bên trong: nén bằng cách RetroArch không hỗ trợ
            //   (LZMA, BZip2, Zstandard... do 7-Zip / WinRAR tạo) → giải nén ra
            // - Zip chia nhiều phần (game.z01, game.z02, game.zip): phần .zip không mở riêng được
            bool split = File.Exists(Path.ChangeExtension(zip, ".z01"));
            MarkUnknown(single, split ? Loc.T("scan.multipart") : opened ? Loc.T("scan.extract_first", name) : Loc.T("scan.zip_broken"));
            return new() { single };
        }
    }

    /// <summary>
    /// Bộ ROM arcade (MAME, FBNeo, Neo Geo): nhiều file dump chip, đuôi lạ (.u1, .ic12, .p1, .m1...) hoặc không đuôi,
    /// tên zip là mã game ("kof98.zip"). Chỉ dùng để báo cho rõ, không chạy.
    /// </summary>
    private bool LooksLikeArcade(List<ZipArchiveEntry> entries)
    {
        if (entries.Count < 3 || entries.Any(e => Ext(e.Name) is ".exe" or ".dll")) return false; // game PC
        int chips = entries.Count(e => !ExtraExtensions.Contains(Path.GetExtension(e.Name)) && !_catalog.IsKnownExtension(Ext(e.Name)));
        return chips * 10 >= entries.Count * 6;
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
