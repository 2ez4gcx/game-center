using System.Text.Json;

namespace GameCenter.Core.Config;

/// <summary>
/// Tách thư mục ứng dụng (chỉ đọc) và thư mục dữ liệu người dùng (tài liệu mục 5).
/// </summary>
public sealed class AppPaths
{
    public string AppDir { get; }
    public string DataDir { get; }

    public AppPaths(string appDir, string dataDir)
    {
        AppDir = Path.GetFullPath(appDir);
        DataDir = Path.GetFullPath(dataDir);
    }

    // --- Thư mục ứng dụng ---
    public string RetroArchDir => Path.Combine(AppDir, "Emulators", "RetroArch");
    public string RetroArchExe => Path.Combine(RetroArchDir, "retroarch.exe");
    public string CoresDir => Path.Combine(RetroArchDir, "cores");
    public string DefaultsDir => Path.Combine(AppDir, "Defaults");
    public string PlatformsJson => Path.Combine(DefaultsDir, "platforms.json");
    public string RetroArchBaseCfg => Path.Combine(DefaultsDir, "retroarch-base.cfg");
    public string LicensesDir => Path.Combine(AppDir, "Licenses");

    // --- Thư mục dữ liệu ---
    public string GamesDir => Path.Combine(DataDir, "Games");
    public string BiosDir => Path.Combine(DataDir, "BIOS");
    public string SavesDir => Path.Combine(DataDir, "Saves");
    public string StatesDir => Path.Combine(DataDir, "States");
    public string CoversDir => Path.Combine(DataDir, "Covers");
    public string PlaylistsDir => Path.Combine(DataDir, "Playlists");
    public string ConfigDir => Path.Combine(DataDir, "Config");
    public string SettingsJson => Path.Combine(ConfigDir, "settings.json");
    public string RetroArchCfg => Path.Combine(ConfigDir, "retroarch.cfg");
    public string CoreOptionsCfg => Path.Combine(ConfigDir, "retroarch-core-options.cfg");
    public string DatabaseDir => Path.Combine(DataDir, "Database");
    public string DatabaseFile => Path.Combine(DatabaseDir, "games.db");
    public string LogsDir => Path.Combine(DataDir, "Logs");
    public string LogFile => Path.Combine(LogsDir, "gamecenter.log");
    public string BackupsDir => Path.Combine(DataDir, "Backups");

    public static readonly string[] DefaultPlatformFolders = { "NES", "SNES", "GB", "GBA", "MegaDrive", "PS1" };

    public void EnsureDataFolders()
    {
        foreach (var d in new[] { GamesDir, BiosDir, SavesDir, StatesDir, CoversDir, PlaylistsDir, ConfigDir, DatabaseDir, LogsDir })
            Directory.CreateDirectory(d);
        foreach (var p in DefaultPlatformFolders)
            Directory.CreateDirectory(Path.Combine(GamesDir, p));
    }

    // --- Vị trí thư mục dữ liệu (người dùng có thể đổi trong Cài đặt) ---

    /// <summary>Dự phòng khi thư mục cài đặt không ghi được (ví dụ cài vào Program Files).</summary>
    public static string FallbackDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GameCenter");

    /// <summary>
    /// Mặc định: dữ liệu (Games, Saves, BIOS...) nằm ngay trong thư mục cài đặt,
    /// để người dùng chỉ cần nhớ một chỗ. Không ghi được thì dùng %USERPROFILE%\GameCenter.
    /// </summary>
    public static string DefaultDataDir(string appDir) =>
        IsWritable(appDir) ? Path.GetFullPath(appDir) : FallbackDataDir;

    public static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>File nhỏ ghi nhớ thư mục dữ liệu đã chọn.</summary>
    public static string LocationFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameCenter", "location.json");

    public static string ResolveDataDir(string appDir)
    {
        // Dùng cho kiểm thử / bản portable
        var env = Environment.GetEnvironmentVariable("GAMECENTER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        try
        {
            if (File.Exists(LocationFile))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(LocationFile));
                if (doc.RootElement.TryGetProperty("dataDir", out var v) && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString()!;
            }
        }
        catch { /* dùng mặc định */ }
        // Tương thích bản cũ: đã có dữ liệu ở %USERPROFILE%\GameCenter thì dùng tiếp, không bỏ rơi game và save
        if (File.Exists(Path.Combine(FallbackDataDir, "Database", "games.db"))) return FallbackDataDir;
        return DefaultDataDir(appDir);
    }

    public static void SaveDataDirLocation(string dataDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocationFile)!);
        File.WriteAllText(LocationFile, JsonSerializer.Serialize(new { dataDir }));
    }
}
