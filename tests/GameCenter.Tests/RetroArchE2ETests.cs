using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using Xunit.Abstractions;

namespace GameCenter.Tests;

/// <summary>
/// Kiểm thử đầu-cuối với RetroArch + Beetle PSX HW thật (docs/bao-toan-save.md, mục Kiểm thử).
/// Chỉ chạy khi có biến môi trường GAMECENTER_E2E_APPDIR trỏ tới thư mục cài Game Center (có Emulators/RetroArch):
///   $env:GAMECENTER_E2E_APPDIR = "C:\GameCenter"; dotnet test --filter E2E
/// Ảnh đĩa là đĩa PS1 giả tự tạo (không cần game thật). Mỗi lần chạy RetroArch hiện cửa sổ vài giây rồi tự thoát
/// (--max-frames, tắt vsync và âm thanh). Log RetroArch chép ra GAMECENTER_E2E_LOGDIR nếu đặt.
/// </summary>
public class RetroArchE2ETests
{
    private const string Beetle = "Beetle PSX HW";
    /// <summary>Cài đặt dùng cho mọi lần chạy (chơi trong cửa sổ để không che màn hình).</summary>
    private static readonly AppSettings Settings = new() { Fullscreen = false };
    private readonly ITestOutputHelper _out;

    public RetroArchE2ETests(ITestOutputHelper output) => _out = output;

    private static string? AppDir()
    {
        var dir = Environment.GetEnvironmentVariable("GAMECENTER_E2E_APPDIR");
        return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, "Emulators")) ? dir : null;
    }

    /// <summary>Thẻ nhớ PS1 128 KB có dấu để nhận ra.</summary>
    private static byte[] Card(string marker)
    {
        var b = new byte[128 * 1024];
        b[0] = (byte)'M';
        b[1] = (byte)'C';
        Encoding.ASCII.GetBytes(marker.PadRight(32)).CopyTo(b, 0x2000);
        return b;
    }

    private static string MarkerOf(string srm) => Encoding.ASCII.GetString(File.ReadAllBytes(srm), 0x2000, 32).Trim();

    /// <summary>Chạy RetroArch đúng lệnh Game Center tạo, thêm cấu hình test; chờ tự thoát. Trả về log.</summary>
    private string Launch(AppPaths paths, ProcessStartInfo psi, string extraCfg = "")
    {
        // Như Game Center: ghi lại cấu hình chuẩn trước mỗi lần chạy (RetroArch lưu cả giá trị --appendconfig khi thoát)
        RetroArchConfig.Write(paths, Settings);
        var testCfg = Path.Combine(paths.ConfigDir, "e2e.cfg");
        File.WriteAllText(testCfg,
            "audio_enable = \"false\"\n" +
            "video_vsync = \"false\"\n" +          // chạy không chờ màn hình: đủ số khung hình nhanh kể cả khi cửa sổ bị che
            "video_window_save_positions = \"false\"\n" + extraCfg);
        var log = Path.Combine(paths.LogsDir, $"retroarch-{Guid.NewGuid():N}.log");
        // Ghép cấu hình test bằng đúng hàm app dùng (gộp với --appendconfig "Chơi tiếp" / thư mục save riêng nếu có)
        EmulatorLauncher.AddAppendConfigs(psi, new[] { testCfg });
        psi.ArgumentList.Insert(0, "--verbose");
        psi.ArgumentList.Insert(1, "--log-file");
        psi.ArgumentList.Insert(2, log);
        psi.ArgumentList.Insert(3, "--max-frames=600");
        _out.WriteLine("RUN " + string.Join(" ", psi.ArgumentList.Select(a => $"\"{a}\"")));

        using var p = Process.Start(psi)!;
        bool exited = p.WaitForExit(120_000);
        if (!exited) p.Kill();
        p.WaitForExit();
        var text = File.Exists(log) ? File.ReadAllText(log) : "";
        if (Environment.GetEnvironmentVariable("GAMECENTER_E2E_LOGDIR") is { Length: > 0 } keep)
        {
            Directory.CreateDirectory(keep);
            File.WriteAllText(Path.Combine(keep, $"{DateTime.Now:HHmmss-fff}.log"), text);
        }
        foreach (var line in text.Split('\n').Where(l => Regex.IsMatch(l, @"\[(Disc|SRAM|State|Disk index file|Runtime)\]|libretro INFO\] Loading|ERROR")))
            _out.WriteLine("  | " + line.TrimEnd());
        Assert.True(exited, "RetroArch không tự thoát. Log:\n" + string.Join("\n", text.Split('\n').TakeLast(40)));
        Assert.DoesNotContain("Failed to load content", text);
        return text;
    }

    private (AppPaths Paths, GameDatabase Db, GameRecord Game, EmulatorLauncher Launcher) Setup(TestEnv env, string appDir)
    {
        var paths = new AppPaths(appDir, env.Root);
        paths.EnsureDataFolders();
        foreach (var n in new[] { 1, 2 })
        {
            env.File($"PS1/E2E Game (Disc {n}).bin", TestEnv.Ps1Disc());
            env.Text($"PS1/E2E Game (Disc {n}).cue", $"FILE \"E2E Game (Disc {n}).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        }
        var db = new GameDatabase(paths.DatabaseFile);
        db.Initialize(env.Catalog);
        db.ApplyScan(env.Scan());
        var game = db.GetGames().Single();
        var settings = Settings;
        RetroArchConfig.Write(paths, settings);
        return (paths, db, game, new EmulatorLauncher(paths, env.Catalog, settings));
    }

    /// <summary>Đĩa đang trong khay theo log RetroArch: "[Disc] ...: 2/2 - E2E Game (Disc 2)".</summary>
    private static string? DiscInTray(string log) =>
        Regex.Matches(log, @"\[Disc\][^\n]*?(\d+/\d+)").Select(m => m.Groups[1].Value).LastOrDefault();

    [Fact]
    public void E2E_MultiDisc_LoadsM3u_SharesMemoryCard_ResumesOnLastDisc()
    {
        if (AppDir() is not { } appDir) { _out.WriteLine("Bỏ qua: chưa đặt GAMECENTER_E2E_APPDIR"); return; }
        using var env = new TestEnv();
        var (paths, db, game, launcher) = Setup(env, appDir);
        Assert.Equal(2, game.DiscCount);
        Assert.EndsWith("E2E Game.m3u", game.LaunchFile);

        // Bản cũ: đã chơi đĩa 1 riêng lẻ, thẻ nhớ "E2E Game (Disc 1).srm"
        db.MarkPlayed(game.Id);
        game = db.GetGames().Single();
        var saves = Path.Combine(paths.SavesDir, Beetle);
        var oldSrm = Path.Combine(saves, "E2E Game (Disc 1).srm");
        Directory.CreateDirectory(saves);
        File.WriteAllBytes(oldSrm, Card("SAVE-TU-DIA-1"));

        var plan = launcher.Plan(game);
        Assert.Equal(LaunchProblem.None, plan.Problem);
        var guard = new SaveGuard(paths);
        var loc = guard.Locate(game, db.GetGames(includeMissing: true), env.Catalog);
        Assert.False(loc.PerGame);
        guard.PrepareForLaunch(db, game, loc);
        var launchFile = game.LaunchFile;
        var srm = Path.Combine(saves, "E2E Game.srm");
        Assert.Equal("SAVE-TU-DIA-1", MarkerOf(srm));

        // Lần 1: RetroArch nạp playlist 2 đĩa, bắt đầu ở đĩa 1
        var log = Launch(paths, plan.StartInfo!);
        Assert.Contains("E2E Game.m3u", log);
        Assert.Contains("SET_DISK_CONTROL_EXT_INTERFACE", log);            // core hỗ trợ đổi đĩa
        Assert.Equal("1/2", DiscInTray(log));
        Assert.Contains("[SRAM]", log);
        // Thẻ nhớ dùng chung theo tên playlist, dữ liệu giữ nguyên; thẻ nhớ cũ không bị đụng, không có thẻ riêng cho đĩa 2
        Assert.Equal("SAVE-TU-DIA-1", MarkerOf(srm));
        Assert.Equal("SAVE-TU-DIA-1", MarkerOf(oldSrm));
        Assert.Empty(Directory.GetFiles(saves, "E2E Game (Disc 2)*"));
        Assert.NotEmpty(guard.ListBackups(loc));
        Assert.NotNull(new SaveStateService(paths.StatesDir).FindAutoState(launchFile));

        // Người chơi đổi sang đĩa 2 rồi thoát: RetroArch ghi đĩa đang dùng vào "E2E Game.ldci".
        // Sửa đúng file RetroArch vừa ghi sang đĩa 2 → lần sau phải mở lại ở đĩa 2, vẫn cùng thẻ nhớ.
        var ldci = Path.Combine(saves, "E2E Game.ldci");
        Assert.True(File.Exists(ldci));
        var index = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ldci))!;
        Assert.Equal(0, (int)index["image_index"]!);
        index["image_index"] = 1;
        index["image_path"] = File.ReadAllLines(game.LaunchFile)[1];   // RetroArch kiểm tra cả số đĩa lẫn đường dẫn
        File.WriteAllText(ldci, index.ToJsonString(new System.Text.Json.JsonSerializerOptions
            { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        log = Launch(paths, launcher.Plan(game).StartInfo!);
        Assert.Equal("2/2", DiscInTray(log));
        Assert.Equal("SAVE-TU-DIA-1", MarkerOf(srm));
        Assert.Empty(Directory.GetFiles(saves, "E2E Game (Disc 2)*"));
    }

    /// <summary>
    /// "Chơi tiếp" nạp save state cũ: state của Beetle chứa cả thẻ nhớ. Có block_sram_overwrite thì thẻ nhớ trên ổ
    /// (bản mới nhất) được giữ; không có thì thẻ nhớ có thể bị kéo về lúc chụp state.
    /// </summary>
    [Fact]
    public void E2E_ResumeOldState_DoesNotRollBackMemoryCard()
    {
        if (AppDir() is not { } appDir) { _out.WriteLine("Bỏ qua: chưa đặt GAMECENTER_E2E_APPDIR"); return; }
        using var env = new TestEnv();
        var (paths, _, game, launcher) = Setup(env, appDir);
        var srm = Path.Combine(paths.SavesDir, Beetle, "E2E Game.srm");
        Directory.CreateDirectory(Path.GetDirectoryName(srm)!);

        // Lần 1: thẻ nhớ "A", thoát → RetroArch chụp chỗ đang chơi (state chứa thẻ nhớ "A")
        File.WriteAllBytes(srm, Card("A-LUC-CHUP-STATE"));
        Launch(paths, launcher.Plan(game).StartInfo!);
        Assert.NotNull(new SaveStateService(paths.StatesDir).FindAutoState(game.LaunchFile));
        Assert.Equal("A-LUC-CHUP-STATE", MarkerOf(srm));

        string ResumeWith(bool block)
        {
            // Sau đó người chơi save trong game (thẻ nhớ "B") nhưng chọn không lưu chỗ đang chơi
            File.WriteAllBytes(srm, Card("B-SAVE-MOI-NHAT"));
            var psi = launcher.Plan(game).StartInfo!;
            var resume = Path.Combine(paths.ConfigDir, "resume.cfg");
            File.WriteAllText(resume, "savestate_auto_load = \"true\"\n");
            EmulatorLauncher.AddAppendConfigs(psi, new[] { resume });
            var log = Launch(paths, psi, $"block_sram_overwrite = \"{(block ? "true" : "false")}\"\nsavestate_auto_save = \"false\"\n");
            Assert.Matches(@"\[State\][^\n]*(state\.auto)", log);   // đã thật sự nạp chỗ đang chơi
            return MarkerOf(srm);
        }

        // Cấu hình cũ (0.2.0) để đối chứng: ghi lại kết quả
        _out.WriteLine("block_sram_overwrite=false → thẻ nhớ sau khi chơi tiếp: " + ResumeWith(block: false));
        // Cấu hình Game Center hiện tại (chặn ghi đè): thẻ nhớ giữ bản mới nhất "B"
        Assert.Equal("B-SAVE-MOI-NHAT", ResumeWith(block: true));
    }

    /// <summary>ROM Game Boy giả có pin lưu (MBC1 + RAM 8 KB + pin) để core tạo SRAM (.srm).</summary>
    private static byte[] GbRomWithBattery()
    {
        var b = new byte[0x8000];
        new byte[] { 0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B }.CopyTo(b, 0x104);
        Encoding.ASCII.GetBytes("E2E SRAM").CopyTo(b, 0x134);
        b[0x147] = 0x03; // MBC1 + RAM + BATTERY
        b[0x149] = 0x02; // RAM 8 KB
        byte x = 0;
        for (int i = 0x134; i <= 0x14C; i++) x = (byte)(x - b[i] - 1);
        b[0x14D] = x;
        return b;
    }

    private static byte[] Sram(string marker)
    {
        var b = new byte[8 * 1024];
        Encoding.ASCII.GetBytes(marker.PadRight(32)).CopyTo(b, 0x100);
        return b;
    }

    /// <summary>
    /// Máy băng (Game Boy, Gambatte): save state chứa cả SRAM. Người chơi save trong game sau lần lưu chỗ đang chơi,
    /// rồi bấm "Chơi tiếp": không chặn thì SRAM bị kéo về lúc chụp state và ghi đè mất save trong game mới nhất.
    /// </summary>
    [Fact]
    public void E2E_Cartridge_ResumeOldState_KeepsNewestInGameSave()
    {
        if (AppDir() is not { } appDir) { _out.WriteLine("Bỏ qua: chưa đặt GAMECENTER_E2E_APPDIR"); return; }
        using var env = new TestEnv();
        var paths = new AppPaths(appDir, env.Root);
        paths.EnsureDataFolders();
        var rom = env.File("GB/E2E Cart.gb", GbRomWithBattery());
        var settings = Settings;
        RetroArchConfig.Write(paths, settings);
        var launcher = new EmulatorLauncher(paths, env.Catalog, settings);
        var game = new GameRecord { Id = 1, Platform = "GB", LaunchFile = rom, SourceFile = rom, Title = "E2E Cart" };
        Assert.Equal(LaunchProblem.None, launcher.Plan(game).Problem);

        // Lần 1: core tạo SRAM; tìm đúng thư mục core RetroArch dùng
        Launch(paths, launcher.Plan(game).StartInfo!);
        var srm = Directory.GetFiles(paths.SavesDir, "E2E Cart.srm", SearchOption.AllDirectories).Single();
        _out.WriteLine("SRAM: " + srm);

        // Lần 2: SRAM "A", thoát → chỗ đang chơi chụp lúc SRAM là "A"
        File.WriteAllBytes(srm, Sram("A-LUC-CHUP-STATE"));
        Launch(paths, launcher.Plan(game).StartInfo!);
        Assert.NotNull(new SaveStateService(paths.StatesDir).FindAutoState(rom));
        string Marker() => Encoding.ASCII.GetString(File.ReadAllBytes(srm), 0x100, 32).Trim();
        Assert.Equal("A-LUC-CHUP-STATE", Marker());

        string ResumeWith(bool block)
        {
            File.WriteAllBytes(srm, Sram("B-SAVE-MOI-NHAT"));      // save trong game mới nhất
            var psi = launcher.Plan(game).StartInfo!;
            var resume = Path.Combine(paths.ConfigDir, "resume.cfg");
            File.WriteAllText(resume, "savestate_auto_load = \"true\"\n");
            EmulatorLauncher.AddAppendConfigs(psi, new[] { resume });
            var log = Launch(paths, psi, $"block_sram_overwrite = \"{(block ? "true" : "false")}\"\nsavestate_auto_save = \"false\"\n");
            Assert.Matches(@"\[State\][^\n]*(state\.auto)", log);
            return Marker();
        }

        var unprotected = ResumeWith(block: false);
        _out.WriteLine("block_sram_overwrite=false (0.2.0) → SRAM sau khi chơi tiếp: " + unprotected);
        Assert.Equal("B-SAVE-MOI-NHAT", ResumeWith(block: true));
        // Không có file thống kê thời gian chơi trong thư mục cài đặt
        Assert.Empty(Directory.Exists(Path.Combine(appDir, "Emulators", "RetroArch", "playlists", "logs"))
            ? Directory.GetFiles(Path.Combine(appDir, "Emulators", "RetroArch", "playlists", "logs"), "E2E*", SearchOption.AllDirectories)
            : Array.Empty<string>());
    }

    /// <summary>
    /// Hai game trùng tên file chạy (bản Việt hóa và bản gốc đều là "LOD.cue"; hai ROM "game.gb"): trước đây dùng chung
    /// và ghi đè save của nhau. Nay game thêm sau lưu ở Saves/_PerGame/&lt;id&gt;/ qua --appendconfig, file game giữ nguyên.
    /// </summary>
    [Fact]
    public void E2E_SameFileName_EachGameKeepsItsOwnSave()
    {
        if (AppDir() is not { } appDir) { _out.WriteLine("Bỏ qua: chưa đặt GAMECENTER_E2E_APPDIR"); return; }
        using var env = new TestEnv();
        var paths = new AppPaths(appDir, env.Root);
        paths.EnsureDataFolders();
        foreach (var v in new[] { "LOD (USA)", "LOD (VN)" })
        {
            env.File($"PS1/{v}/LOD.bin", TestEnv.Ps1Disc());
            env.Text($"PS1/{v}/LOD.cue", "FILE \"LOD.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
            env.File($"GB/{v}/game.gb", GbRomWithBattery());
        }
        var db = new GameDatabase(paths.DatabaseFile);
        db.Initialize(env.Catalog);
        db.ApplyScan(env.Scan());
        var settings = Settings;
        RetroArchConfig.Write(paths, settings);
        var launcher = new EmulatorLauncher(paths, env.Catalog, settings);
        var guard = new SaveGuard(paths);
        var all = db.GetGames(includeMissing: true);
        Assert.Equal(4, all.Count);

        foreach (var (platform, coreDir, marker) in new[] { ("PS1", Beetle, 0x2000), ("GB", "Gambatte", 0x100) })
        {
            var games = all.Where(g => g.Platform == platform).OrderBy(g => g.Id).ToList();
            var locs = games.Select(g => guard.Locate(g, all, env.Catalog)).ToList();
            Assert.False(locs[0].PerGame);
            Assert.True(locs[1].PerGame);

            // Mỗi game có save riêng với nội dung riêng
            for (int i = 0; i < 2; i++)
            {
                var srm = Path.Combine(locs[i].SavesDir, coreDir, locs[i].ContentName + ".srm");
                Directory.CreateDirectory(Path.GetDirectoryName(srm)!);
                var data = platform == "PS1" ? Card($"SAVE-CUA-{games[i].Title}") : Sram($"SAVE-CUA-{games[i].Title}");
                File.WriteAllBytes(srm, data);
            }
            for (int i = 0; i < 2; i++)
            {
                guard.PrepareForLaunch(db, games[i], locs[i]);
                var psi = launcher.Plan(games[i]).StartInfo!;
                Assert.Equal(games[i].LaunchFile, psi.ArgumentList[^1]);           // file game giữ nguyên
                if (SaveGuard.AppendConfig(locs[i]) is { } perGame)
                {
                    var cfg = Path.Combine(paths.ConfigDir, "save-dir.cfg");
                    File.WriteAllText(cfg, perGame);
                    EmulatorLauncher.AddAppendConfigs(psi, new[] { cfg });
                }
                var log = Launch(paths, psi);
                Assert.Contains(Path.Combine(locs[i].SavesDir, coreDir, locs[i].ContentName + ".srm"), log);   // RetroArch ghi đúng chỗ
            }
            for (int i = 0; i < 2; i++)
            {
                var srm = Path.Combine(locs[i].SavesDir, coreDir, locs[i].ContentName + ".srm");
                Assert.Equal($"SAVE-CUA-{games[i].Title}", Encoding.ASCII.GetString(File.ReadAllBytes(srm), marker, 32).Trim());
                Assert.NotNull(new SaveStateService(locs[i].StatesDir).FindAutoState(games[i].LaunchFile));
            }
        }
    }
}
