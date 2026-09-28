using System.IO.Compression;
using System.Text;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Scanning;

namespace GameCenter.Tests;

public sealed class TestEnv : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gc-test-" + Guid.NewGuid().ToString("N")[..8], "Dữ liệu game");
    public AppPaths Paths { get; }
    public PlatformCatalog Catalog { get; }

    public TestEnv()
    {
        var appDir = FindRepoRoot();
        Paths = new AppPaths(appDir, Root);
        Paths.EnsureDataFolders();
        Catalog = PlatformCatalog.Load(Path.Combine(appDir, "Defaults", "platforms.json"));
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(Path.Combine(d.FullName, "Defaults", "platforms.json"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Defaults/platforms.json");
    }

    public string File(string rel, byte[]? content = null)
    {
        var p = Path.Combine(Paths.GamesDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, content ?? new byte[] { 1, 2, 3, 4 });
        return p;
    }

    public string Text(string rel, string text) => File(rel, new UTF8Encoding(false).GetBytes(text));

    /// <summary>Ảnh đĩa CD thô (2352 byte/sector, Mode 2) với PVD "CD001" ở sector 16.</summary>
    public static byte[] Ps1Disc(string systemId = "PLAYSTATION")
    {
        const int sector = 2352;
        var b = new byte[18 * sector];
        for (int s = 0; s < 18; s++)
        {
            int o = s * sector;
            for (int i = 1; i <= 10; i++) b[o + i] = 0xFF;
            b[o + 15] = 2; // mode 2
        }
        int pvd = 16 * sector + 24;
        b[pvd] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(b, pvd + 1);
        Encoding.ASCII.GetBytes(systemId).CopyTo(b, pvd + 8);
        return b;
    }

    public static byte[] MegaDriveRom()
    {
        var b = new byte[0x400];
        Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ").CopyTo(b, 0x100);
        return b;
    }

    public List<ScannedGame> Scan() => new GameScanner(Catalog, Paths.GamesDir, Paths.PlaylistsDir).Scan();

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Root)!, true); } catch { }
    }
}

public class ScannerTests
{
    [Fact]
    public void PlatformFolder_And_Extensions()
    {
        using var env = new TestEnv();
        env.File("SNES/Super Mario World (USA) [!].sfc");
        env.File("anywhere/Pokemon Emerald.gba");
        env.File("NES/readme.txt");

        var games = env.Scan();
        Assert.Equal(2, games.Count);
        var smw = games.Single(g => g.Platform == "SNES");
        Assert.Equal("Super Mario World", smw.Title);
        Assert.NotNull(smw.FileHash);
        Assert.Contains(games, g => g.Platform == "GBA");
    }

    [Fact]
    public void Ps1_CueBin_ShownOnce_WithFolderName()
    {
        using var env = new TestEnv();
        env.File("PS1/Final Fantasy VII/ff7.bin");
        env.Text("PS1/Final Fantasy VII/ff7.cue", "FILE \"FF7.BIN\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");

        var g = Assert.Single(env.Scan());
        Assert.Equal("PS1", g.Platform);
        Assert.Equal("Final Fantasy VII", g.Title);
        Assert.EndsWith("ff7.cue", g.LaunchFile);
        Assert.Equal(ScanStatus.Ok, g.Status); // khác hoa/thường vẫn khớp
    }

    [Fact]
    public void Ps1_BrokenCue_Reported()
    {
        using var env = new TestEnv();
        env.Text("PS1/Game/game.cue", "FILE \"khong co.bin\" BINARY\n  TRACK 01 MODE2/2352\n");
        var g = Assert.Single(env.Scan());
        Assert.Equal(ScanStatus.BrokenCue, g.Status);
        Assert.Contains("khong co.bin", g.Message);
    }

    [Fact]
    public void Ps1_MultiDisc_GeneratesM3u_InPlaylists()
    {
        using var env = new TestEnv();
        foreach (var n in new[] { 1, 2 })
        {
            env.File($"PS1/Metal Gear Solid/Metal Gear Solid (Disc {n}).bin");
            env.Text($"PS1/Metal Gear Solid/Metal Gear Solid (Disc {n}).cue", $"FILE \"Metal Gear Solid (Disc {n}).bin\" BINARY\n");
        }

        var g = Assert.Single(env.Scan());
        Assert.Equal("Metal Gear Solid", g.Title);
        Assert.Equal(2, g.Discs.Count);
        Assert.StartsWith(env.Paths.PlaylistsDir, g.LaunchFile);
        Assert.EndsWith(".m3u", g.LaunchFile);
        var lines = File.ReadAllLines(g.LaunchFile);
        Assert.Equal(2, lines.Length);
        Assert.True(Path.IsPathRooted(lines[0]));
        Assert.Contains("Disc 1", lines[0]);
        // không ghi gì vào thư mục game
        Assert.Equal(4, Directory.GetFiles(Path.Combine(env.Paths.GamesDir, "PS1", "Metal Gear Solid")).Length);
    }

    [Fact]
    public void LooseBin_MegaDriveHeader_vs_Ps1TempCue()
    {
        using var env = new TestEnv();
        env.File("misc/Sonic.bin", TestEnv.MegaDriveRom());
        env.File("misc/Tiếng Việt game.bin", TestEnv.Ps1Disc());
        env.File("misc/Sega CD game.bin", TestEnv.Ps1Disc(systemId: "SEGA SEGACD"));
        env.File("misc/rác.bin", new byte[0x1000]);

        var games = env.Scan();
        Assert.Equal("MegaDrive", games.Single(g => g.Title == "Sonic").Platform);
        Assert.Equal(ScanStatus.Unknown, games.Single(g => g.Title == "Sega CD game").Status);
        Assert.Equal(ScanStatus.Unknown, games.Single(g => g.Title == "rác").Status);
        var ps1 = games.Single(g => g.Platform == "PS1");
        Assert.EndsWith(".cue", ps1.LaunchFile);
        Assert.StartsWith(env.Paths.PlaylistsDir, ps1.LaunchFile);
        var cue = CueParser.Parse(ps1.LaunchFile);
        Assert.True(cue.IsValid);
    }

    [Fact]
    public void GeneratedCue_KeepsOriginalFileName_SoSaveNameIsStable()
    {
        using var env = new TestEnv();
        env.File("Yu-Gi-Oh! Forbidden Memories (USA).bin", TestEnv.Ps1Disc());
        var g = Assert.Single(env.Scan());
        Assert.Equal("Yu-Gi-Oh! Forbidden Memories", g.Title);                       // tên hiển thị đã làm sạch
        Assert.Equal("Yu-Gi-Oh! Forbidden Memories (USA).cue", Path.GetFileName(g.LaunchFile)); // tên save = tên gốc
    }

    [Fact]
    public void Zip_ClassifiedByContent()
    {
        using var env = new TestEnv();
        MakeZip(env.File("x/one.zip"), ("Contra (USA).nes", new byte[16]));
        MakeZip(env.File("x/md.zip"), ("sonic.bin", TestEnv.MegaDriveRom()));
        MakeZip(env.File("x/many.zip"), ("a.nes", new byte[16]), ("b.gba", new byte[16]));
        MakeZip(env.File("x/none.zip"), ("readme.txt", new byte[16]));
        MakeZip(env.File("SNES/in-folder.zip"), ("whatever.dat", new byte[16]));

        var g = env.Scan().ToDictionary(x => Path.GetFileName(x.SourceFile));
        Assert.Equal("NES", g["one.zip"].Platform);
        Assert.Equal("MegaDrive", g["md.zip"].Platform);
        // zip nhiều ROM → tách mỗi ROM một game, chạy bằng "file.zip#rom"
        Assert.Equal("NES", g["many.zip#a.nes"].Platform);
        Assert.Equal("GBA", g["many.zip#b.gba"].Platform);
        Assert.EndsWith("many.zip#b.gba", g["many.zip#b.gba"].LaunchFile);
        Assert.Equal(ScanStatus.Unknown, g["none.zip"].Status);
        Assert.Equal("SNES", g["in-folder.zip"].Platform);
    }

    private static void MakeZip(string path, params (string Name, byte[] Data)[] entries)
    {
        File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (n, d) in entries)
        {
            using var s = z.CreateEntry(n).Open();
            s.Write(d);
        }
    }

    [Fact]
    public void Rescan_NoDuplicates_Missing_And_Moved()
    {
        using var env = new TestEnv();
        var db = new GameDatabase(env.Paths.DatabaseFile);
        db.Initialize(env.Catalog);

        var rom = env.File("SNES/Chrono Trigger.sfc", new byte[] { 9, 9, 9 });
        env.File("SNES/Zelda.sfc", new byte[] { 7, 7 });
        db.ApplyScan(env.Scan());
        db.ApplyScan(env.Scan());
        Assert.Equal(2, db.GetGames().Count);

        var ct = db.GetGames().Single(x => x.Title == "Chrono Trigger");
        db.SetFavorite(ct.Id, true);
        db.MarkPlayed(ct.Id);

        // di chuyển
        var moved = Path.Combine(env.Paths.GamesDir, "SNES", "RPG", "Chrono Trigger.sfc");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(rom, moved);
        // xóa
        File.Delete(Path.Combine(env.Paths.GamesDir, "SNES", "Zelda.sfc"));

        var summary = db.ApplyScan(env.Scan());
        Assert.Equal(1, summary.Moved);
        Assert.Equal(1, summary.Missing);
        var after = Assert.Single(db.GetGames());
        Assert.Equal(ct.Id, after.Id);
        Assert.True(after.IsFavorite);
        Assert.Equal(1, after.PlayCount);
        Assert.Equal(moved, after.SourceFile);
        Assert.Equal(2, db.GetGames(includeMissing: true).Count);
    }

    [Fact]
    public void UserChosenPlatform_KeptOnRescan()
    {
        using var env = new TestEnv();
        var db = new GameDatabase(env.Paths.DatabaseFile);
        db.Initialize(env.Catalog);
        env.File("x/lạ.bin", new byte[0x1000]);
        db.ApplyScan(env.Scan());
        var g = db.GetGames().Single();
        Assert.Equal(GameDatabase.UnknownPlatform, g.Platform);

        db.SetPlatform(g.Id, "NES");
        db.ApplyScan(env.Scan());
        g = db.GetGames().Single();
        Assert.Equal("NES", g.Platform);
        Assert.Equal(ScanStatus.Ok, g.ScanStatus);
    }
}

public class NameAndConfigTests
{
    [Theory]
    [InlineData("Super Mario World (USA) [!]", "Super Mario World")]
    [InlineData("Final_Fantasy_VII (Rev 1) (Disc 1)", "Final Fantasy VII")]
    [InlineData("Tiếng Việt - Huyền thoại (Europe)", "Tiếng Việt - Huyền thoại")]
    public void Clean(string input, string expected) => Assert.Equal(expected, NameCleaner.Clean(input));

    [Theory]
    [InlineData("mgs_disc1", "mgs", 1)]
    [InlineData("Game (Disc 2)", "Game", 2)]
    [InlineData("Game CD3", "Game", 3)]
    [InlineData("Game disc 1", "Game", 1)]
    public void Disc(string name, string baseName, int n)
    {
        Assert.Equal(baseName, NameCleaner.StripDisc(name));
        Assert.Equal(n, NameCleaner.DiscNumberOf(name));
    }

    [Fact]
    public void RetroArchConfig_PreservesUserKeys()
    {
        using var env = new TestEnv();
        File.WriteAllText(env.Paths.RetroArchCfg, "video_shader_enable = \"x\"\nvideo_fullscreen = \"false\"\n");
        RetroArchConfig.Write(env.Paths, new AppSettings { Fullscreen = true });
        var kv = RetroArchConfig.Parse(File.ReadAllLines(env.Paths.RetroArchCfg)).ToDictionary(x => x.Key, x => x.Value);
        Assert.Equal("x", kv["video_shader_enable"]);
        // Phím mặc định WASD/IJKL, phím tắt trùng bị tắt, tự lưu khi thoát
        Assert.Equal("w", kv["input_player1_up"]);
        Assert.Equal("l", kv["input_player1_a"]);
        Assert.Equal("nul", kv["input_hold_fast_forward"]);
        Assert.Equal("true", kv["savestate_auto_save"]);
        Assert.Equal("true", kv["video_fullscreen"]);
        Assert.Equal("4", kv["input_quit_gamepad_combo"]);
        Assert.Equal(env.Paths.SavesDir, kv["savefile_directory"]);
    }

    [Fact]
    public void Launcher_UsesArgumentList_WithSpacesAndUnicode()
    {
        using var env = new TestEnv();
        var l = new EmulatorLauncher(env.Paths, env.Catalog, new AppSettings());
        var psi = l.BuildRetroArchStartInfo(@"C:\x\core.dll", @"D:\Trò chơi\Game hay\a b.cue");
        Assert.Equal(new[] { "--config", env.Paths.RetroArchCfg, "-L", @"C:\x\core.dll", @"D:\Trò chơi\Game hay\a b.cue" }, psi.ArgumentList);
    }

    [Fact]
    public void Ps1_NoBios_UsesFallback()
    {
        using var env = new TestEnv();
        Assert.Equal(BiosState.UsingFallback, BiosChecker.Check(env.Catalog.Get("PS1")!, env.Paths.BiosDir));
        File.WriteAllBytes(Path.Combine(env.Paths.BiosDir, "SCPH5501.BIN"), new byte[1]);
        Assert.Equal(BiosState.Present, BiosChecker.Check(env.Catalog.Get("PS1")!, env.Paths.BiosDir));
    }
}

public class SaveStateTests
{
    [Fact]
    public void ContentName_MatchesRetroArch()
    {
        Assert.Equal("Yu-Gi-Oh! (USA)", SaveStateService.ContentName(@"C:\g\Yu-Gi-Oh! (USA).cue"));
        Assert.Equal("rom", SaveStateService.ContentName(@"C:\g\pack.zip#rom.nes"));
    }

    [Fact]
    public void Discard_RestoresPrevious_Keep_KeepsNew()
    {
        using var env = new TestEnv();
        var core = Path.Combine(env.Paths.StatesDir, "Beetle PSX HW");
        Directory.CreateDirectory(core);
        var state = Path.Combine(core, "Game (USA).state.auto");
        var launch = @"C:\x\Game (USA).cue";
        var svc = new SaveStateService(env.Paths.StatesDir);

        // Lần 1: chưa có bản cũ, chọn "Không lưu" → xóa hẳn
        var start = DateTime.UtcNow;
        Assert.Null(svc.BackupBeforePlay(launch));
        File.WriteAllText(state, "lan1");
        SaveStateService.Discard(svc.NewStateSince(launch, start)!);
        Assert.False(File.Exists(state));

        // Lần 2: chọn "Lưu lại" → giữ
        start = DateTime.UtcNow;
        File.WriteAllText(state, "lan2");
        SaveStateService.Keep(svc.NewStateSince(launch, start)!);
        Assert.Equal("lan2", File.ReadAllText(state));

        // Lần 3: chơi tiếp rồi chọn "Không lưu" → khôi phục "lan2"
        Assert.NotNull(svc.BackupBeforePlay(launch));
        start = DateTime.UtcNow;
        File.WriteAllText(state, "lan3");
        SaveStateService.Discard(svc.NewStateSince(launch, start)!);
        Assert.Equal("lan2", File.ReadAllText(state));
        Assert.Empty(Directory.GetFiles(core, "*.gcbak"));
    }
}

public class KeyboardTests
{
    [Fact]
    public void SystemKeys_CannotBeAssigned_FallBackToDefault()
    {
        var map = KeyboardLayout.Resolve(new Dictionary<string, string> { ["a"] = "f2", ["b"] = "escape", ["x"] = "num1" });
        Assert.Equal("l", map["a"]);
        Assert.Equal("k", map["b"]);
        Assert.Equal("num1", map["x"]);
    }
}
