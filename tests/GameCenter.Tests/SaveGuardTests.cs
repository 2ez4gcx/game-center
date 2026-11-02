using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Scanning;

namespace GameCenter.Tests;

/// <summary>Các tình huống mất save trong docs/bao-toan-save.md.</summary>
public class SaveGuardTests
{
    private const string Beetle = "Beetle PSX HW";

    private static string Srm(string savesDir, string name, string content)
    {
        var p = Path.Combine(savesDir, Beetle, name + ".srm");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private static string Srm(TestEnv env, string name, string content) => Srm(env.Paths.SavesDir, name, content);
    private static string ReadSrm(string savesDir, string name) => File.ReadAllText(Path.Combine(savesDir, Beetle, name + ".srm"));
    private static string ReadSrm(TestEnv env, string name) => ReadSrm(env.Paths.SavesDir, name);
    private static SaveLocation Shared(TestEnv env, string name) => new(env.Paths.SavesDir, env.Paths.StatesDir, name, name, false);

    private static void Disc(TestEnv env, string folder, string name)
    {
        env.File($"{folder}/{name}.bin", TestEnv.Ps1Disc());
        env.Text($"{folder}/{name}.cue", $"FILE \"{name}.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
    }

    private static GameDatabase Db(TestEnv env)
    {
        var db = new GameDatabase(env.Paths.DatabaseFile);
        db.Initialize(env.Catalog);
        return db;
    }

    private static SaveLocation Prepare(TestEnv env, GameDatabase db, GameRecord g)
    {
        var guard = new SaveGuard(env.Paths);
        var loc = guard.Locate(g, db.GetGames(includeMissing: true), env.Catalog);
        guard.PrepareForLaunch(db, g, loc);
        return loc;
    }

    [Fact]
    public void Adopt_CopiesNewestOldSave_NeverOverwritesOrDeletes()
    {
        using var env = new TestEnv();
        var guard = new SaveGuard(env.Paths);
        var old = Srm(env, "Game (Disc 1)", "the le 1");
        File.WriteAllText(Path.Combine(env.Paths.SavesDir, Beetle, "Game (Disc 1).ldci"), "1");

        var copied = guard.AdoptPreviousSave(Shared(env, "Game"), new[] { "Game (Disc 1)", "khong co" });
        Assert.Equal(2, copied.Count);
        Assert.Equal("the le 1", ReadSrm(env, "Game"));
        Assert.True(File.Exists(old));                                  // bản cũ giữ nguyên

        // Đã có save theo tên mới: không đụng tới
        Srm(env, "Game (Disc 1)", "cu hon");
        Assert.Empty(guard.AdoptPreviousSave(Shared(env, "Game"), new[] { "Game (Disc 1)" }));
        Assert.Equal("the le 1", ReadSrm(env, "Game"));
    }

    /// <summary>Chơi đĩa 1 (bản 0.2.0, save "Game (Disc 1).srm"), sau đó chép thêm đĩa 2 → game thành bộ nhiều đĩa.</summary>
    [Fact]
    public void SingleDiscBecomesMultiDisc_SaveFollows()
    {
        using var env = new TestEnv();
        var db = Db(env);
        Disc(env, "PS1", "Legend of Dragoon (Disc 1)");
        db.ApplyScan(env.Scan());
        var g = db.GetGames().Single();
        Prepare(env, db, g);
        db.MarkPlayed(g.Id);
        Srm(env, "Legend of Dragoon (Disc 1)", "save dia 1");

        Disc(env, "PS1", "Legend of Dragoon (Disc 2)");
        db.ApplyScan(env.Scan());
        g = db.GetGames().Single();
        Assert.Equal(2, g.DiscCount);
        Assert.EndsWith("Legend of Dragoon.m3u", g.LaunchFile);

        var loc = Prepare(env, db, g);
        Assert.False(loc.PerGame);
        Assert.Equal("save dia 1", ReadSrm(env, "Legend of Dragoon"));
        Assert.Contains("Legend of Dragoon", db.GetSaveNames(g.Id));
    }

    /// <summary>Game có lịch sử chơi từ bản cũ (chưa có bảng save_names) vẫn tìm lại được save qua tên đĩa.</summary>
    [Fact]
    public void OldVersionGame_WithoutHistory_FindsDiscSave()
    {
        using var env = new TestEnv();
        var db = Db(env);
        Disc(env, "PS1/MGS", "Metal Gear Solid (Disc 1)");
        Disc(env, "PS1/MGS", "Metal Gear Solid (Disc 2)");
        db.ApplyScan(env.Scan());
        var g = db.GetGames().Single();
        db.MarkPlayed(g.Id);
        g = db.GetGames().Single();
        Srm(env, "Metal Gear Solid (Disc 2)", "dang o dia 2");

        Prepare(env, db, g);
        Assert.Equal("dang o dia 2", ReadSrm(env, "Metal Gear Solid"));
    }

    /// <summary>
    /// Mọi game PS1 dạng EBOOT.PBP đều tên "EBOOT" → trước đây dùng chung, ghi đè thẻ nhớ của nhau.
    /// Nay game thêm sau lưu ở thư mục riêng; file game giữ nguyên.
    /// </summary>
    [Fact]
    public void SameFileName_GetsOwnSaveFolder()
    {
        using var env = new TestEnv();
        var db = Db(env);
        var guard = new SaveGuard(env.Paths);
        env.File("PS1/Crash/EBOOT.PBP", Fake.Pbp("PSISOIMG0000"));
        db.ApplyScan(env.Scan());
        env.File("PS1/Spyro/EBOOT.PBP", Fake.Pbp("PSISOIMG0000"));
        db.ApplyScan(env.Scan());
        var all = db.GetGames(includeMissing: true);
        var crash = all.Single(x => x.Title == "Crash");
        var spyro = all.Single(x => x.Title == "Spyro");
        Assert.True(crash.Id < spyro.Id);

        // Game thêm trước giữ chỗ cũ (save cũ của nó không đổi); game thêm sau có thư mục riêng, ổn định
        var crashLoc = guard.Locate(crash, all, env.Catalog);
        var spyroLoc = guard.Locate(spyro, all, env.Catalog);
        Assert.False(crashLoc.PerGame);
        Assert.Null(SaveGuard.AppendConfig(crashLoc));
        Assert.True(spyroLoc.PerGame);
        // Thư mục riêng chưa có: không truyền cho RetroArch (nó sẽ bỏ qua và lưu vào thư mục cài đặt);
        // chuẩn bị trước khi chơi thì tạo thư mục
        Assert.Null(SaveGuard.AppendConfig(spyroLoc));
        guard.PrepareForLaunch(db, spyro, spyroLoc);
        Assert.True(Directory.Exists(spyroLoc.SavesDir) && Directory.Exists(spyroLoc.StatesDir));
        Assert.Equal(Path.Combine(env.Paths.SavesDir, SaveGuard.PerGameFolder, spyro.Id.ToString()), spyroLoc.SavesDir);
        Assert.Contains($"savefile_directory = \"{spyroLoc.SavesDir}\"", SaveGuard.AppendConfig(spyroLoc));
        Assert.Contains($"savestate_directory = \"{spyroLoc.StatesDir}\"", SaveGuard.AppendConfig(spyroLoc));
        Assert.Equal(spyroLoc, guard.Locate(spyro, all, env.Catalog));
        Assert.Equal("EBOOT", spyroLoc.ContentName);

        // Thẻ nhớ chung "EBOOT.srm" (của Crash): game mới không bao giờ nhận, kể cả các lần chơi sau
        Srm(env, "EBOOT", "the chung");
        guard.PrepareForLaunch(db, spyro, spyroLoc);
        Assert.Empty(guard.FindSaveFiles("EBOOT", spyroLoc.SavesDir));
        db.MarkPlayed(spyro.Id);
        spyro = db.GetGames().Single(x => x.Id == spyro.Id);
        guard.PrepareForLaunch(db, spyro, spyroLoc);
        Assert.Empty(guard.FindSaveFiles("EBOOT", spyroLoc.SavesDir));

        // Từ đây hai game tách riêng: save của Spyro không lẫn vào Crash (tìm, sao lưu)
        Srm(spyroLoc.SavesDir, "EBOOT", "spyro tien trinh moi");
        Assert.Single(guard.FindSaveFiles("EBOOT"));
        Assert.Equal("the chung", ReadSrm(env, "EBOOT"));
        guard.Backup(crashLoc, false);
        guard.Backup(spyroLoc, false);
        Assert.Equal("the chung", File.ReadAllText(Path.Combine(guard.ListBackups(crashLoc)[0].Dir, Beetle, "EBOOT.srm")));
        Assert.Equal("spyro tien trinh moi", File.ReadAllText(Path.Combine(guard.ListBackups(spyroLoc)[0].Dir, Beetle, "EBOOT.srm")));
    }

    /// <summary>
    /// Bản 0.2.0: Crash và Spyro (cùng EBOOT.PBP) đã chơi chung thẻ "EBOOT.srm". Lên bản mới, Spyro được tách riêng
    /// và nhận bản sao thẻ chung (không mất tiến trình); thẻ chung giữ nguyên cho Crash.
    /// </summary>
    [Fact]
    public void SameFileName_PlayedInOldVersion_KeepsProgressOnce()
    {
        using var env = new TestEnv();
        var db = Db(env);
        var guard = new SaveGuard(env.Paths);
        env.File("PS1/Crash/EBOOT.PBP", Fake.Pbp("PSISOIMG0000"));
        env.File("PS1/Spyro/EBOOT.PBP", Fake.Pbp("PSISOIMG0000"));
        db.ApplyScan(env.Scan());
        foreach (var g in db.GetGames()) db.MarkPlayed(g.Id);   // đã chơi ở bản cũ, chưa có lịch sử tên
        Srm(env, "EBOOT", "the chung ca hai");
        var all = db.GetGames(includeMissing: true);
        var second = all.OrderBy(g => g.Id).Last();
        var loc = guard.Locate(second, all, env.Catalog);
        Assert.True(loc.PerGame);

        guard.PrepareForLaunch(db, second, loc);
        Assert.Equal("the chung ca hai", ReadSrm(loc.SavesDir, "EBOOT"));
        Assert.Equal("the chung ca hai", ReadSrm(env, "EBOOT"));
        // Lần sau: đã có save riêng, thẻ chung có đổi cũng không chép đè
        Srm(env, "EBOOT", "crash choi tiep");
        Srm(loc.SavesDir, "EBOOT", "second choi tiep");
        guard.PrepareForLaunch(db, second, loc);
        Assert.Equal("second choi tiep", ReadSrm(loc.SavesDir, "EBOOT"));
    }

    /// <summary>"Chơi tiếp" của game này không được thấy chỗ đang chơi của game trùng tên.</summary>
    [Fact]
    public void SameFileName_StatesDoNotLeak()
    {
        using var env = new TestEnv();
        var perGame = Path.Combine(env.Paths.StatesDir, SaveGuard.PerGameFolder, "7", Beetle);
        Directory.CreateDirectory(perGame);
        File.WriteAllText(Path.Combine(perGame, "EBOOT.state.auto"), "cua game 7");
        Assert.Null(new SaveStateService(env.Paths.StatesDir).FindAutoState(@"C:\g\Crash\EBOOT.PBP"));
        Assert.NotNull(new SaveStateService(Path.Combine(env.Paths.StatesDir, SaveGuard.PerGameFolder, "7")).FindAutoState(@"C:\g\Spyro\EBOOT.PBP"));
    }

    [Fact]
    public void SameFileName_CartridgesAlsoSeparated()
    {
        using var env = new TestEnv();
        var db = Db(env);
        env.File("a/rom.nes", Fake.NesRom());
        env.File("b/rom.nes", new byte[] { 1, 2, 3 });
        db.ApplyScan(env.Scan());
        var all = db.GetGames();
        var guard = new SaveGuard(env.Paths);
        var locs = all.OrderBy(g => g.Id).Select(g => guard.Locate(g, all, env.Catalog)).ToList();
        Assert.False(locs[0].PerGame);
        Assert.True(locs[1].PerGame);
        // ROM khác core cùng tên thì không coi là trùng (save nằm ở thư mục core khác nhau)
        env.File("c/rom.gba", new byte[] { 1 });
        db.ApplyScan(env.Scan());
        all = db.GetGames();
        Assert.False(guard.Locate(all.Single(g => g.Platform == "GBA"), all, env.Catalog).PerGame);
    }

    [Fact]
    public void Backup_OnlyWhenChanged_KeepsLast10_And_Restore()
    {
        using var env = new TestEnv();
        var guard = new SaveGuard(env.Paths);
        var loc = Shared(env, "Game");
        Assert.Null(guard.Backup(loc, includePs2Cards: false)); // chưa có save

        Srm(env, "Game", "v1");
        Assert.NotNull(guard.Backup(loc, false));
        Assert.Null(guard.Backup(loc, false));                  // không đổi → không sao lưu thêm
        for (int i = 2; i <= 14; i++)
        {
            Srm(env, "Game", "v" + i);
            Assert.NotNull(guard.Backup(loc, false));
        }
        var list = guard.ListBackups(loc);
        Assert.Equal(SaveGuard.KeepBackups, list.Count);
        Assert.Equal("v14", File.ReadAllText(Path.Combine(list[0].Dir, Beetle, "Game.srm")));

        // Lỡ ghi đè save: khôi phục bản "v12"; bản hiện tại (hỏng) cũng được sao lưu trước
        Srm(env, "Game", "hong");
        var v12 = list.First(b => File.ReadAllText(Path.Combine(b.Dir, Beetle, "Game.srm")) == "v12");
        guard.Restore(loc, v12);
        Assert.Equal("v12", ReadSrm(env, "Game"));
        Assert.Contains(guard.ListBackups(loc), b => File.ReadAllText(Path.Combine(b.Dir, Beetle, "Game.srm")) == "hong");
    }

    [Fact]
    public void Ps2_SharedCards_BackedUpAndRestored()
    {
        using var env = new TestEnv();
        var guard = new SaveGuard(env.Paths);
        var loc = Shared(env, "God of War");
        Directory.CreateDirectory(guard.Ps2CardsDir);
        var card = Path.Combine(guard.Ps2CardsDir, "Mcd001.ps2");
        File.WriteAllText(card, "ps2 v1");
        Assert.NotNull(guard.Backup(loc, includePs2Cards: true));
        File.WriteAllText(card, "ps2 v2");
        guard.Restore(loc, guard.ListBackups(loc).Last());
        Assert.Equal("ps2 v1", File.ReadAllText(card));
    }

    [Fact]
    public void AutoState_FollowsNameChange()
    {
        using var env = new TestEnv();
        var dir = Path.Combine(env.Paths.StatesDir, Beetle);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Game (Disc 1).state.auto"), "cho dang choi");
        var guard = new SaveGuard(env.Paths);
        var copied = guard.AdoptPreviousState(Shared(env, "Game"), new[] { "Game (Disc 1)" });
        Assert.Equal(Path.Combine(dir, "Game.state.auto"), copied);
        Assert.Equal("cho dang choi", File.ReadAllText(copied!));
        Assert.Null(guard.AdoptPreviousState(Shared(env, "Game"), new[] { "Game (Disc 1)" }));
    }

    [Fact]
    public void ChangeDataFolder_CopiesSaves_WithoutOverwriting()
    {
        using var env = new TestEnv();
        Srm(env, "Game", "cu");
        File.WriteAllBytes(Path.Combine(env.Paths.BiosDir, "scph5501.bin"), new byte[16]);
        var other = new AppPaths(env.Paths.AppDir, Path.Combine(Path.GetDirectoryName(env.Root)!, "Thư mục mới"));
        other.EnsureDataFolders();
        Directory.CreateDirectory(Path.Combine(other.SavesDir, Beetle));
        File.WriteAllText(Path.Combine(other.SavesDir, Beetle, "Khac.srm"), "giu");

        Assert.Equal(2, BackupService.CopyUserData(env.Paths, other));
        Assert.Equal("cu", File.ReadAllText(Path.Combine(other.SavesDir, Beetle, "Game.srm")));
        Assert.Equal("giu", File.ReadAllText(Path.Combine(other.SavesDir, Beetle, "Khac.srm")));
        Assert.Equal(0, BackupService.CopyUserData(env.Paths, other));
    }

    [Fact]
    public void Config_ProtectsSaves()
    {
        using var env = new TestEnv();
        var keys = RetroArchConfig.ManagedKeys(env.Paths, new AppSettings(), false);
        Assert.Equal("true", keys["block_sram_overwrite"]);       // nạp state cũ không kéo SRAM về quá khứ
        Assert.Equal("10", keys["autosave_interval"]);
        Assert.Equal("true", keys["sort_savefiles_enable"]);
        Assert.Equal("false", keys["content_runtime_log"]);          // không ghi vào thư mục cài đặt
        var core = RetroArchConfig.ManagedCoreOptions(new AppSettings(), false);
        Assert.Equal("libretro", core["beetle_psx_hw_use_mednafen_memcard0_method"]);
        Assert.Equal("enabled", core["pcsx2_shared_memory_cards"]);
    }

    [Fact]
    public void MissingDisc_Warned_ButPlayable()
    {
        using var env = new TestEnv();
        Disc(env, "PS1/FF8", "Final Fantasy VIII (Disc 1)");
        Disc(env, "PS1/FF8", "Final Fantasy VIII (Disc 3)");
        Disc(env, "PS1/FF8", "Final Fantasy VIII (Disc 4)");
        var g = Assert.Single(env.Scan());
        Assert.Equal(ScanStatus.Ok, g.Status);
        Assert.Equal(3, g.Discs.Count);
        Assert.Contains("2", g.Message);
    }
}
