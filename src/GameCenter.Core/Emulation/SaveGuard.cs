using System.Security.Cryptography;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Util;

namespace GameCenter.Core.Emulation;

/// <summary>Một bản sao lưu tự động save của một game.</summary>
public sealed record SaveBackup(string Dir, DateTime Time, IReadOnlyList<string> Files);

/// <summary>
/// Nơi save của một game: thư mục save / save state RetroArch dùng, tên save (tên file chạy bỏ đuôi)
/// và khóa của các bản sao lưu tự động.
/// </summary>
public sealed record SaveLocation(string SavesDir, string StatesDir, string ContentName, string BackupKey, bool PerGame);

/// <summary>
/// Giữ save trong game (thẻ nhớ, SRAM) không bao giờ mất (docs/bao-toan-save.md):
/// - RetroArch đặt tên save theo tên file chạy ("&lt;tên&gt;.srm" trong Saves/&lt;core&gt;/). Tên file chạy đổi
///   (đổi tên file, ghép đĩa 1 thành bộ nhiều đĩa...) thì save cũ vẫn còn nhưng không được tìm thấy
///   → trước khi chơi, nhận lại (chép, không di chuyển) save mới nhất từ các tên game này từng dùng.
/// - Hai game cùng core trùng tên file chạy (mọi game PS1 dạng EBOOT.PBP, "game.cue" ở nhiều thư mục, "rom.nes"...)
///   sẽ dùng chung và ghi đè save của nhau → game thêm sau lưu save ở thư mục riêng Saves/_PerGame/&lt;id&gt;/
///   (truyền cho RetroArch bằng --appendconfig). File game giữ nguyên, nên không ảnh hưởng việc nạp game.
/// - Sao lưu tự động trước mỗi lần chơi (chỉ khi save có thay đổi), giữ <see cref="KeepBackups"/> bản gần nhất.
/// Game Center không bao giờ xóa file save của người dùng.
/// </summary>
public sealed class SaveGuard
{
    public const int KeepBackups = 10;
    /// <summary>Thư mục con (trong Saves/ và States/) chứa save của các game trùng tên file chạy.</summary>
    public const string PerGameFolder = "_PerGame";
    /// <summary>Thư mục con trong bản sao lưu chứa thẻ nhớ PS2 dùng chung (BIOS/pcsx2/memcards).</summary>
    private const string Ps2CardsFolder = "_PS2-memcards";
    private readonly AppPaths _paths;

    public SaveGuard(AppPaths paths) => _paths = paths;

    public string AutoBackupsDir => Path.Combine(_paths.BackupsDir, "Auto");
    /// <summary>Thẻ nhớ dùng chung của LRPS2 (pcsx2_shared_memory_cards = enabled): &lt;system&gt;/pcsx2/memcards.</summary>
    public string Ps2CardsDir => Path.Combine(_paths.BiosDir, "pcsx2", "memcards");

    /// <summary>File nằm trong thư mục save riêng của game khác (Saves/_PerGame/...) tính từ thư mục gốc.</summary>
    public static bool IsInPerGameFolder(string root, string file) =>
        Path.GetRelativePath(root, file).StartsWith(PerGameFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Nơi lưu save

    /// <summary>
    /// Nơi lưu save của game. Bình thường là Saves/ và States/ như mọi game. Nếu một game cùng core thêm trước
    /// (id nhỏ hơn) đã dùng cùng tên save, game này lưu ở Saves/_PerGame/&lt;id&gt;/ và States/_PerGame/&lt;id&gt;/.
    /// Game thêm trước giữ nguyên chỗ cũ nên save hiện có của nó không bị ảnh hưởng. Kết quả ổn định qua các lần chơi.
    /// </summary>
    public SaveLocation Locate(GameRecord game, IEnumerable<GameRecord> allGames, PlatformCatalog catalog)
    {
        var name = SaveStateService.ContentName(game.LaunchFile);
        var core = catalog.Get(game.Platform)?.Core;
        bool taken = core != null && allGames.Any(o => o.Id < game.Id
            && catalog.Get(o.Platform)?.Core == core
            && string.Equals(SaveStateService.ContentName(o.LaunchFile), name, StringComparison.OrdinalIgnoreCase));
        if (!taken) return new SaveLocation(_paths.SavesDir, _paths.StatesDir, name, name, PerGame: false);
        var id = game.Id.ToString();
        return new SaveLocation(Path.Combine(_paths.SavesDir, PerGameFolder, id), Path.Combine(_paths.StatesDir, PerGameFolder, id),
            name, $"{name} [{id}]", PerGame: true);
    }

    /// <summary>
    /// Cấu hình phụ cho RetroArch khi game lưu save ở thư mục riêng (null nếu dùng chỗ chung).
    /// Thư mục phải có sẵn: RetroArch bỏ qua thư mục không tồn tại và lưu vào thư mục mặc định cạnh retroarch.exe
    /// (thư mục cài đặt) → <see cref="PrepareForLaunch"/> tạo trước; chưa tạo được thì trả về null (dùng chỗ chung).
    /// </summary>
    public static string? AppendConfig(SaveLocation loc) =>
        !loc.PerGame || !Directory.Exists(loc.SavesDir) || !Directory.Exists(loc.StatesDir) ? null :
        $"savefile_directory = \"{loc.SavesDir}\"\nsavestate_directory = \"{loc.StatesDir}\"\n";

    // ------------------------------------------------------------------ Nhận lại save khi tên đổi

    /// <summary>
    /// File save của một tên: "&lt;tên&gt;.srm", "&lt;tên&gt;.1.mcr", "&lt;tên&gt;.ldci"... ở mọi thư mục core dưới
    /// <paramref name="savesRoot"/> (mặc định Saves/, không tính thư mục riêng của game khác).
    /// </summary>
    public IReadOnlyList<string> FindSaveFiles(string contentName, string? savesRoot = null)
    {
        var root = savesRoot ?? _paths.SavesDir;
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var prefix = contentName + ".";
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !IsInPerGameFolder(root, f))
            .ToList();
    }

    /// <summary>
    /// Tên save hiện tại chưa có save chính (.srm) mà một tên cũ của game có → chép bản mới nhất sang
    /// (cùng thư mục core, ở nơi lưu của game). Tìm ở nơi lưu của game; <paramref name="fromShared"/>: với game lưu riêng,
    /// tìm cả chỗ chung (save của game từ bản cũ, trước khi được tách riêng). Không ghi đè, không xóa bản cũ.
    /// Trả về các file đã chép.
    /// </summary>
    public IReadOnlyList<string> AdoptPreviousSave(SaveLocation loc, IEnumerable<string> previousNames, bool fromShared = true)
    {
        if (FindSaveFiles(loc.ContentName, loc.SavesDir).Any(IsMainSave)) return Array.Empty<string>();
        var roots = loc.PerGame && fromShared ? new[] { loc.SavesDir, _paths.SavesDir } : new[] { loc.SavesDir };
        var best = previousNames.Append(loc.ContentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(n => roots.SelectMany(r => FindSaveFiles(n, r).Where(IsMainSave).Select(f => (Name: n, Root: r, File: f))))
            .Where(x => !(x.Root == loc.SavesDir && string.Equals(x.Name, loc.ContentName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => File.GetLastWriteTimeUtc(x.File))
            .FirstOrDefault();
        if (best.File == null) return Array.Empty<string>();

        var copied = new List<string>();
        var dir = Path.GetDirectoryName(best.File)!;
        var destDir = Path.Combine(loc.SavesDir, Path.GetRelativePath(best.Root, dir));
        // Chép cả bộ file cùng tên trong thư mục core đó (.srm, .1.mcr của thẻ nhớ 2, .ldci đĩa đang dùng...)
        foreach (var f in Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f).StartsWith(best.Name + ".", StringComparison.OrdinalIgnoreCase)))
        {
            var dest = Path.Combine(destDir, loc.ContentName + Path.GetFileName(f)[best.Name.Length..]);
            if (File.Exists(dest)) continue;
            Directory.CreateDirectory(destDir);
            File.Copy(f, dest);
            copied.Add(dest);
        }
        Log.Info($"Nhận lại save \"{best.Name}\" cho \"{loc.BackupKey}\": {string.Join(", ", copied.Select(Path.GetFileName))}");
        return copied;
    }

    /// <summary>Như <see cref="AdoptPreviousSave"/> cho "chỗ đang chơi" (&lt;tên&gt;.state.auto). Trả về file đã chép hoặc null.</summary>
    public string? AdoptPreviousState(SaveLocation loc, IEnumerable<string> previousNames, bool fromShared = true)
    {
        var own = new SaveStateService(loc.StatesDir);
        if (own.FindAutoStateByName(loc.ContentName) != null) return null;
        var sources = loc.PerGame && fromShared ? new[] { own, new SaveStateService(_paths.StatesDir) } : new[] { own };
        var best = previousNames.Append(loc.ContentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(n => sources.Select(s => (Root: s, File: s.FindAutoStateByName(n))))
            .Where(x => x.File != null && !(x.Root == own && Path.GetFileName(x.File) == loc.ContentName + SaveStateService.AutoSuffix))
            .OrderByDescending(x => File.GetLastWriteTimeUtc(x.File!))
            .FirstOrDefault();
        if (best.File == null) return null;
        var rootDir = best.Root == own ? loc.StatesDir : _paths.StatesDir;
        var destDir = Path.Combine(loc.StatesDir, Path.GetRelativePath(rootDir, Path.GetDirectoryName(best.File)!));
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, loc.ContentName + SaveStateService.AutoSuffix);
        File.Copy(best.File, dest);
        Log.Info($"Nhận lại chỗ đang chơi {best.File} → {dest}");
        return dest;
    }

    /// <summary>File save chính: SRAM / thẻ nhớ 1 (.srm) hoặc thẻ nhớ kiểu Mednafen (.mcr).</summary>
    private static bool IsMainSave(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".srm" or ".mcr" or ".sav";

    // ------------------------------------------------------------------ Sao lưu tự động / khôi phục

    private string BackupRoot(string key) =>
        Path.Combine(AutoBackupsDir, string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));

    /// <summary>File cần sao lưu của game: save theo tên (đường dẫn tương đối trong nơi lưu) và thẻ nhớ PS2 dùng chung.</summary>
    private List<(string Source, string Relative)> SaveSet(SaveLocation loc, bool includePs2Cards)
    {
        var set = FindSaveFiles(loc.ContentName, loc.SavesDir).Select(f => (f, Path.GetRelativePath(loc.SavesDir, f))).ToList();
        if (includePs2Cards && Directory.Exists(Ps2CardsDir))
            set.AddRange(Directory.EnumerateFiles(Ps2CardsDir).Select(f => (f, Path.Combine(Ps2CardsFolder, Path.GetFileName(f)))));
        return set;
    }

    /// <summary>
    /// Sao lưu save của game trước khi chơi vào Backups/Auto/&lt;khóa&gt;/&lt;thời gian&gt;/. Bỏ qua nếu giống hệt bản gần nhất;
    /// giữ <see cref="KeepBackups"/> bản. Trả về thư mục bản sao lưu mới (null nếu không cần).
    /// </summary>
    public string? Backup(SaveLocation loc, bool includePs2Cards, bool force = false)
    {
        var set = SaveSet(loc, includePs2Cards);
        if (set.Count == 0) return null;
        var latest = ListBackups(loc).FirstOrDefault();
        if (!force && latest != null && SameContent(set, latest.Dir)) return null;

        var dir = Path.Combine(BackupRoot(loc.BackupKey), DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
        foreach (var (source, rel) in set)
        {
            var dest = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }
        foreach (var old in ListBackups(loc).Skip(KeepBackups))
        {
            try { Directory.Delete(old.Dir, recursive: true); } catch (Exception ex) { Log.Warn($"Không xóa được bản sao lưu cũ {old.Dir}: {ex.Message}"); }
        }
        return dir;
    }

    private static bool SameContent(List<(string Source, string Relative)> set, string backupDir)
    {
        var backed = Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories).ToList();
        if (backed.Count != set.Count) return false;
        foreach (var (source, rel) in set)
        {
            var b = Path.Combine(backupDir, rel);
            if (!File.Exists(b) || new FileInfo(b).Length != new FileInfo(source).Length || !Hash(b).SequenceEqual(Hash(source))) return false;
        }
        return true;
    }

    private static byte[] Hash(string file)
    {
        using var fs = File.OpenRead(file);
        return SHA256.HashData(fs);
    }

    /// <summary>Các bản sao lưu tự động của game, mới nhất trước.</summary>
    public IReadOnlyList<SaveBackup> ListBackups(SaveLocation loc)
    {
        var root = BackupRoot(loc.BackupKey);
        if (!Directory.Exists(root)) return Array.Empty<SaveBackup>();
        return Directory.EnumerateDirectories(root)
            .Select(d => new SaveBackup(d, TryParseStamp(Path.GetFileName(d), out var t) ? t : Directory.GetCreationTime(d),
                Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(d, f)).ToList()))
            .Where(b => b.Files.Count > 0)
            .OrderByDescending(b => b.Time)
            .ToList();
    }

    private static bool TryParseStamp(string name, out DateTime t) =>
        DateTime.TryParseExact(name, "yyyy-MM-dd_HH-mm-ss-fff", null, System.Globalization.DateTimeStyles.None, out t);

    /// <summary>
    /// Khôi phục một bản sao lưu: sao lưu save hiện tại trước (để có thể quay lại), rồi chép đè từng file.
    /// File save hiện có mà bản sao lưu không có thì giữ nguyên (không xóa gì).
    /// </summary>
    public void Restore(SaveLocation loc, SaveBackup backup)
    {
        Backup(loc, includePs2Cards: backup.Files.Any(f => f.StartsWith(Ps2CardsFolder)), force: true);
        foreach (var rel in backup.Files)
        {
            var target = rel.StartsWith(Ps2CardsFolder)
                ? Path.Combine(Ps2CardsDir, Path.GetFileName(rel))
                : Path.Combine(loc.SavesDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(backup.Dir, rel), target, overwrite: true);
        }
        Log.Info($"Khôi phục save \"{loc.BackupKey}\" từ {backup.Dir}");
    }

    // ------------------------------------------------------------------ Trước khi chơi

    /// <summary>
    /// Chuẩn bị save trước khi chạy game: nhận lại save từ tên cũ, sao lưu, ghi nhớ tên đang dùng.
    /// Lỗi ở đây chỉ ghi log, không chặn việc chơi.
    /// </summary>
    public void PrepareForLaunch(GameDatabase db, GameRecord game, SaveLocation loc)
    {
        try
        {
            Directory.CreateDirectory(loc.SavesDir);
            Directory.CreateDirectory(loc.StatesDir);
            // Chỉ nhận lại save cho game đã từng chơi: game mới thêm chưa có save nào để nhận,
            // tránh chép nhầm save của game khác trùng tên file.
            // Game lưu riêng chỉ nhận thẻ chung khi đã chơi ở bản cũ (chưa có lịch sử tên): khi đó thẻ chung đúng là
            // thẻ nó từng dùng. Đã có lịch sử nghĩa là save của nó đã ở thư mục riêng; thẻ chung là của game kia.
            var history = db.GetSaveNames(game.Id);
            if (game.PlayCount > 0 || history.Count > 0)
            {
                var old = history.Concat(PossibleOldNames(db, game)).ToList();
                bool fromShared = history.Count == 0;
                AdoptPreviousSave(loc, old, fromShared);
                AdoptPreviousState(loc, old, fromShared);
            }
            Backup(loc, includePs2Cards: game.Platform == "PS2");
            db.RecordSaveName(game.Id, loc.ContentName);
        }
        catch (Exception ex) { Log.Error($"Không chuẩn bị được save cho {game.Name}", ex); }
    }

    /// <summary>Tên save game này có thể đã dùng trước khi có lịch sử: file gốc, file chạy, từng đĩa.</summary>
    private static IEnumerable<string> PossibleOldNames(GameDatabase db, GameRecord game) =>
        new[] { game.SourceFile, game.LaunchFile }.Concat(db.GetDiscs(game.Id))
            .Select(SaveStateService.ContentName);
}
