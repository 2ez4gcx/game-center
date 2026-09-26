using GameCenter.Core.Config;
using GameCenter.Core.Scanning;
using Microsoft.Data.Sqlite;

namespace GameCenter.Core.Data;

public sealed class GameRecord
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string? DisplayTitle { get; set; }
    public string Platform { get; set; } = "";
    public string LaunchFile { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public string? CoverPath { get; set; }
    public string? AddedAt { get; set; }
    public string? LastPlayedAt { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public string ScanStatus { get; set; } = Scanning.ScanStatus.Ok;
    public string? ScanMessage { get; set; }
    public int DiscCount { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayTitle) ? Title : DisplayTitle!;
}

public sealed record ScanSummary(int Added, int Updated, int Moved, int Missing, int Unknown, int Broken);

/// <summary>Database SQLite (mục 9).</summary>
public sealed class GameDatabase
{
    /// <summary>Platform tạm cho game chưa nhận ra.</summary>
    public const string UnknownPlatform = "Unknown";

    private readonly string _connectionString;

    public GameDatabase(string dbFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbFile)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbFile, ForeignKeys = true }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Initialize(PlatformCatalog catalog)
    {
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS games (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                display_title TEXT,
                platform TEXT NOT NULL,
                launch_file TEXT NOT NULL,
                source_file TEXT NOT NULL,
                folder_path TEXT NOT NULL,
                file_size INTEGER,
                file_hash TEXT,
                cover_path TEXT,
                added_at TEXT,
                last_played_at TEXT,
                play_count INTEGER DEFAULT 0,
                is_favorite INTEGER DEFAULT 0,
                scan_status TEXT DEFAULT 'ok',   -- ok | missing | broken_cue | unknown
                scan_message TEXT,
                platform_locked INTEGER DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_games_source ON games(source_file COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS discs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
                disc_number INTEGER NOT NULL,
                file_path TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS platforms (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                core_file TEXT NOT NULL,
                extensions_json TEXT NOT NULL,
                enabled INTEGER DEFAULT 1
            );
            """);

        foreach (var p in catalog.Platforms)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO platforms(name, core_file, extensions_json, enabled) VALUES($n,$c,$e,$en)
                ON CONFLICT(name) DO UPDATE SET core_file=$c, extensions_json=$e, enabled=$en
                """;
            cmd.Parameters.AddWithValue("$n", p.Name);
            cmd.Parameters.AddWithValue("$c", p.Core);
            cmd.Parameters.AddWithValue("$e", System.Text.Json.JsonSerializer.Serialize(p.Extensions));
            cmd.Parameters.AddWithValue("$en", p.Enabled ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ Quét lại

    /// <summary>
    /// Đồng bộ kết quả quét vào database:
    /// không tạo bản ghi trùng; game bị xóa → 'missing' (giữ lịch sử);
    /// game bị di chuyển → khớp theo hash hoặc (tên + kích thước).
    /// </summary>
    public ScanSummary ApplyScan(IReadOnlyList<ScannedGame> scanned)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        int added = 0, updated = 0, moved = 0;

        var existing = LoadAll(c, tx, includeMissing: true);
        var bySource = existing.ToDictionary(g => g.SourceFile, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<long>();

        foreach (var s in scanned)
        {
            GameRecord? match = bySource.GetValueOrDefault(s.SourceFile);
            bool isMove = false;
            if (match == null)
            {
                // Có thể là game bị di chuyển: chỉ khớp với bản ghi không còn tìm thấy.
                var candidates = existing.Where(e => !seen.Contains(e.Id) && !File.Exists(e.SourceFile));
                match = s.FileHash != null
                    ? candidates.FirstOrDefault(e => e.FileHash == s.FileHash)
                    : candidates.FirstOrDefault(e => e.Title == s.Title && e.FileSize == s.FileSize && e.FileSize > 0);
                isMove = match != null;
            }

            if (match == null)
            {
                var id = Insert(c, tx, s);
                ReplaceDiscs(c, tx, id, s.Discs);
                seen.Add(id);
                added++;
                continue;
            }

            seen.Add(match.Id);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE games SET title=$t,
                    platform = CASE WHEN platform_locked=1 THEN platform ELSE $p END,
                    launch_file=$l, source_file=$s, folder_path=$f, file_size=$sz, file_hash=$h,
                    scan_status = CASE WHEN platform_locked=1 AND $st='unknown' THEN 'ok' ELSE $st END,
                    scan_message=$m
                WHERE id=$id
                """;
            BindScanned(cmd, s);
            cmd.Parameters.AddWithValue("$id", match.Id);
            cmd.ExecuteNonQuery();
            ReplaceDiscs(c, tx, match.Id, s.Discs);
            if (isMove) moved++; else updated++;
        }

        int missing = 0;
        foreach (var e in existing.Where(e => !seen.Contains(e.Id) && e.ScanStatus != ScanStatus.Missing))
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE games SET scan_status='missing' WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", e.Id);
            cmd.ExecuteNonQuery();
            missing++;
        }
        tx.Commit();

        return new ScanSummary(added, updated, moved, missing,
            scanned.Count(s => s.Status == ScanStatus.Unknown),
            scanned.Count(s => s.Status == ScanStatus.BrokenCue));
    }

    private static long Insert(SqliteConnection c, SqliteTransaction tx, ScannedGame s)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO games(title, platform, launch_file, source_file, folder_path, file_size, file_hash, added_at, scan_status, scan_message)
            VALUES($t,$p,$l,$s,$f,$sz,$h,$now,$st,$m);
            SELECT last_insert_rowid();
            """;
        BindScanned(cmd, s);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("s"));
        return (long)cmd.ExecuteScalar()!;
    }

    private static void BindScanned(SqliteCommand cmd, ScannedGame s)
    {
        cmd.Parameters.AddWithValue("$t", s.Title);
        cmd.Parameters.AddWithValue("$p", s.Platform ?? UnknownPlatform);
        cmd.Parameters.AddWithValue("$l", s.LaunchFile);
        cmd.Parameters.AddWithValue("$s", s.SourceFile);
        cmd.Parameters.AddWithValue("$f", s.FolderPath);
        cmd.Parameters.AddWithValue("$sz", s.FileSize);
        cmd.Parameters.AddWithValue("$h", (object?)s.FileHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$st", s.Status);
        cmd.Parameters.AddWithValue("$m", (object?)s.Message ?? DBNull.Value);
    }

    private static void ReplaceDiscs(SqliteConnection c, SqliteTransaction tx, long gameId, List<string> discs)
    {
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM discs WHERE game_id=$g";
            del.Parameters.AddWithValue("$g", gameId);
            del.ExecuteNonQuery();
        }
        for (int i = 0; i < discs.Count; i++)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO discs(game_id, disc_number, file_path) VALUES($g,$n,$f)";
            ins.Parameters.AddWithValue("$g", gameId);
            ins.Parameters.AddWithValue("$n", i + 1);
            ins.Parameters.AddWithValue("$f", discs[i]);
            ins.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------ Đọc

    public List<GameRecord> GetGames(bool includeMissing = false)
    {
        using var c = Open();
        return LoadAll(c, null, includeMissing);
    }

    private static List<GameRecord> LoadAll(SqliteConnection c, SqliteTransaction? tx, bool includeMissing)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT g.id, g.title, g.display_title, g.platform, g.launch_file, g.source_file, g.folder_path,
                   g.file_size, g.file_hash, g.cover_path, g.added_at, g.last_played_at, g.play_count,
                   g.is_favorite, g.scan_status, g.scan_message,
                   (SELECT COUNT(*) FROM discs d WHERE d.game_id = g.id)
            FROM games g
            """ + (includeMissing ? "" : " WHERE g.scan_status <> 'missing'") +
            " ORDER BY COALESCE(g.display_title, g.title) COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<GameRecord>();
        while (r.Read())
        {
            list.Add(new GameRecord
            {
                Id = r.GetInt64(0),
                Title = r.GetString(1),
                DisplayTitle = r.IsDBNull(2) ? null : r.GetString(2),
                Platform = r.GetString(3),
                LaunchFile = r.GetString(4),
                SourceFile = r.GetString(5),
                FolderPath = r.GetString(6),
                FileSize = r.IsDBNull(7) ? 0 : r.GetInt64(7),
                FileHash = r.IsDBNull(8) ? null : r.GetString(8),
                CoverPath = r.IsDBNull(9) ? null : r.GetString(9),
                AddedAt = r.IsDBNull(10) ? null : r.GetString(10),
                LastPlayedAt = r.IsDBNull(11) ? null : r.GetString(11),
                PlayCount = r.IsDBNull(12) ? 0 : r.GetInt32(12),
                IsFavorite = !r.IsDBNull(13) && r.GetInt32(13) != 0,
                ScanStatus = r.IsDBNull(14) ? ScanStatus.Ok : r.GetString(14),
                ScanMessage = r.IsDBNull(15) ? null : r.GetString(15),
                DiscCount = r.GetInt32(16),
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ Ghi

    public void MarkPlayed(long id) =>
        Update(id, "last_played_at=$v, play_count=play_count+1", DateTime.Now.ToString("s"));

    public void SetFavorite(long id, bool fav) => Update(id, "is_favorite=$v", fav ? 1 : 0);

    /// <summary>Đổi tên hiển thị trong app; không đổi tên file thật.</summary>
    public void SetDisplayTitle(long id, string? title) =>
        Update(id, "display_title=$v", string.IsNullOrWhiteSpace(title) ? DBNull.Value : title.Trim());

    public void SetCover(long id, string? path) => Update(id, "cover_path=$v", (object?)path ?? DBNull.Value);

    /// <summary>Người dùng tự chọn platform cho game "Chưa nhận ra". Giữ nguyên khi quét lại.</summary>
    public void SetPlatform(long id, string platform) =>
        Update(id, "platform=$v, platform_locked=1, scan_status=CASE WHEN scan_status='unknown' THEN 'ok' ELSE scan_status END, scan_message=NULL", platform);

    private void Update(long id, string setClause, object value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE games SET {setClause} WHERE id=$id";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
