using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Scanning;

namespace GameCenter.Tests;

/// <summary>Dựng file ảnh đĩa / ROM giả để kiểm thử nhận diện theo nội dung.</summary>
public static class Fake
{
    public const string Ps2Cnf = "BOOT2 = cdrom0:\\SLUS_200.62;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n";
    public const string Ps1Cnf = "BOOT = cdrom:\\SLUS_007.08;1\r\nTCB = 4\r\nEVENT = 10\r\n";

    /// <summary>Ảnh đĩa ISO 9660: PVD ở sector 16, thư mục gốc ở 18, SYSTEM.CNF ở 19.</summary>
    public static byte[] Disc(string? systemCnf, bool raw, string systemId = "PLAYSTATION")
    {
        var sectors = new byte[20][];
        for (int i = 0; i < sectors.Length; i++) sectors[i] = new byte[2048];

        var pvd = sectors[16];
        pvd[0] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1);
        pvd[6] = 1;
        Encoding.ASCII.GetBytes(systemId.PadRight(32)).CopyTo(pvd, 8);
        DirRecord(pvd, 156, 18, 2048, dir: true, "\0");

        var root = sectors[18];
        int p = DirRecord(root, 0, 18, 2048, dir: true, "\0");
        p = DirRecord(root, p, 18, 2048, dir: true, "\u0001");
        if (systemCnf != null)
        {
            DirRecord(root, p, 19, (uint)systemCnf.Length, dir: false, "SYSTEM.CNF;1");
            Encoding.ASCII.GetBytes(systemCnf).CopyTo(sectors[19], 0);
        }

        if (!raw) return sectors.SelectMany(s => s).ToArray();
        // Sector thô 2352: sync 00 FF×10 00, header (mode 2 ở byte 15), subheader 8 byte, dữ liệu từ byte 24
        var img = new byte[sectors.Length * 2352];
        for (int i = 0; i < sectors.Length; i++)
        {
            int o = i * 2352;
            for (int k = 1; k <= 10; k++) img[o + k] = 0xFF;
            img[o + 15] = 2;
            sectors[i].CopyTo(img, o + 24);
        }
        return img;
    }

    private static int DirRecord(byte[] b, int at, uint lba, uint size, bool dir, string name)
    {
        int len = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
        b[at] = (byte)len;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 2), lba);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at + 6), lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 10), size);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at + 14), size);
        b[at + 25] = (byte)(dir ? 2 : 0);
        b[at + 32] = (byte)name.Length;
        Encoding.ASCII.GetBytes(name).CopyTo(b, at + 33);
        return at + len;
    }

    /// <summary>.cso (CISO v1): khối 2048 byte nén deflate thô.</summary>
    public static byte[] Cso(byte[] iso)
    {
        const int block = 2048;
        int blocks = iso.Length / block;
        var data = new MemoryStream();
        var index = new uint[blocks + 1];
        int headerSize = 24 + (blocks + 1) * 4;
        for (int i = 0; i < blocks; i++)
        {
            index[i] = (uint)(headerSize + data.Length);
            var packed = new MemoryStream();
            using (var d = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) d.Write(iso, i * block, block);
            data.Write(packed.ToArray());
        }
        index[blocks] = (uint)(headerSize + data.Length);

        var o = new MemoryStream();
        var h = new byte[24];
        Encoding.ASCII.GetBytes("CISO").CopyTo(h, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), 24);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(8), (ulong)iso.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), block);
        h[20] = 1;
        o.Write(h);
        foreach (var x in index) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, x); o.Write(b); }
        o.Write(data.ToArray());
        return o.ToArray();
    }

    /// <summary>Header .chd v5 với một mục metadata ("DVD " hoặc "CHT2").</summary>
    public static byte[] Chd(string metaTag)
    {
        var b = new byte[124 + 16 + 32];
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 124);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 5);
        BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(48), 124);
        Encoding.ASCII.GetBytes(metaTag).CopyTo(b, 124);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(128), 32);
        return b;
    }

    /// <summary>BIOS PS2 giả: 4 MB, mục ROMDIR "RESET" rồi "ROMDIR".</summary>
    public static byte[] Ps2Bios()
    {
        var b = new byte[4 * 1024 * 1024];
        Encoding.ASCII.GetBytes("RESET").CopyTo(b, 0x2700);
        Encoding.ASCII.GetBytes("ROMDIR").CopyTo(b, 0x2710);
        return b;
    }

    public static byte[] GbRom(byte cgbFlag)
    {
        var b = new byte[0x8000];
        new byte[] { 0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B }.CopyTo(b, 0x104);
        Encoding.ASCII.GetBytes("TETRIS").CopyTo(b, 0x134);
        b[0x143] = cgbFlag;
        byte x = 0;
        for (int i = 0x134; i <= 0x14C; i++) x = (byte)(x - b[i] - 1);
        b[0x14D] = x;
        return b;
    }

    public static byte[] GbaRom()
    {
        var b = new byte[0x200];
        Encoding.ASCII.GetBytes("POKEMON EMER").CopyTo(b, 0xA0);
        b[0xB2] = 0x96;
        byte chk = 0;
        for (int i = 0xA0; i <= 0xBC; i++) chk = (byte)(chk - b[i]);
        b[0xBD] = (byte)(chk - 0x19);
        return b;
    }

    public static byte[] NesRom() { var b = new byte[0x6010]; Encoding.ASCII.GetBytes("NES\u001A").CopyTo(b, 0); return b; }

    public static byte[] SegaMasterRom(byte regionNibble)
    {
        var b = new byte[0x8000];
        Encoding.ASCII.GetBytes("TMR SEGA").CopyTo(b, 0x7FF0);
        b[0x7FFF] = (byte)((regionNibble << 4) | 0x0C);
        return b;
    }

    /// <summary>EBOOT.PBP: "\0PBP", PSAR ở 0x28.</summary>
    public static byte[] Pbp(string psarMagic)
    {
        var b = new byte[0x40 + 0x100];
        Encoding.ASCII.GetBytes("\0PBP").CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x24), 0x40);
        Encoding.ASCII.GetBytes(psarMagic).CopyTo(b, 0x40);
        return b;
    }
}

public class DiscDetectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ps1_vs_Ps2_BySystemCnf(bool raw)
    {
        Assert.Equal(DiscKind.Ps2, DiscImage.Detect(new MemoryStream(Fake.Disc(Fake.Ps2Cnf, raw))));
        Assert.Equal(DiscKind.Ps1, DiscImage.Detect(new MemoryStream(Fake.Disc(Fake.Ps1Cnf, raw))));
        // Game PS1 đời đầu không có SYSTEM.CNF
        Assert.Equal(DiscKind.Ps1, DiscImage.Detect(new MemoryStream(Fake.Disc(null, raw))));
        Assert.Equal(DiscKind.Psp, DiscImage.Detect(new MemoryStream(Fake.Disc(null, raw, "PSP GAME"))));
        Assert.Equal(DiscKind.Other, DiscImage.Detect(new MemoryStream(Fake.Disc(null, raw, "SEGA SEGASATURN"))));
        Assert.Equal(DiscKind.Unknown, DiscImage.Detect(new MemoryStream(new byte[5000])));
    }

    [Fact]
    public void Cso_IsDecompressed()
    {
        Assert.Equal(DiscKind.Ps2, DiscImage.Detect(new MemoryStream(Fake.Cso(Fake.Disc(Fake.Ps2Cnf, raw: false)))));
        Assert.Equal(DiscKind.Psp, DiscImage.Detect(new MemoryStream(Fake.Cso(Fake.Disc(null, false, "PSP GAME")))));
    }

    [Fact]
    public void Chd_Metadata()
    {
        Assert.Equal(ChdMedia.Dvd, ChdFile.ReadMedia(new MemoryStream(Fake.Chd("DVD "))));
        Assert.Equal(ChdMedia.Cd, ChdFile.ReadMedia(new MemoryStream(Fake.Chd("CHT2"))));
        Assert.Equal(ChdMedia.Other, ChdFile.ReadMedia(new MemoryStream(Fake.Chd("CHGD"))));
        Assert.Equal(ChdMedia.Unknown, ChdFile.ReadMedia(new MemoryStream(new byte[200])));
    }

    [Fact]
    public void RomHeaders()
    {
        Assert.Equal("NES", RomHeader.Detect(Fake.NesRom()));
        Assert.Equal("GB", RomHeader.Detect(Fake.GbRom(0x00)));
        Assert.Equal("GBC", RomHeader.Detect(Fake.GbRom(0x80)));
        Assert.Equal("GBC", RomHeader.Detect(Fake.GbRom(0xC0)));
        Assert.Equal("GBA", RomHeader.Detect(Fake.GbaRom()));
        Assert.Equal("MegaDrive", RomHeader.Detect(TestEnv.MegaDriveRom()));
        Assert.Equal("MasterSystem", RomHeader.Detect(Fake.SegaMasterRom(4)));
        Assert.Equal("GameGear", RomHeader.Detect(Fake.SegaMasterRom(7)));
        Assert.Equal("Nintendo 64", RomHeader.Detect(new byte[] { 0x80, 0x37, 0x12, 0x40, 0, 0, 0, 0 }));
        Assert.Null(RomHeader.Detect(new byte[0x8000]));
    }
}

public class Ps2ScanTests
{
    [Fact]
    public void Iso_Cso_Chd_Classified()
    {
        using var env = new TestEnv();
        env.File("a/God of War (USA).iso", Fake.Disc(Fake.Ps2Cnf, raw: false));
        env.File("a/Crash (USA).iso", Fake.Disc(Fake.Ps1Cnf, raw: false));
        env.File("a/Patapon.iso", Fake.Disc(null, false, "PSP GAME"));
        env.File("a/Okami.cso", Fake.Cso(Fake.Disc(Fake.Ps2Cnf, raw: false)));
        env.File("a/Shadow of the Colossus.chd", Fake.Chd("DVD "));
        env.File("a/Spyro.chd", Fake.Chd("CHT2"));
        env.File("PS2/Ape Escape 2.chd", Fake.Chd("CHT2"));   // game PS2 đĩa CD: theo thư mục PS2
        env.File("PS2/khong doc duoc.iso", new byte[100]);

        var g = env.Scan().ToDictionary(x => x.Title);
        Assert.Equal("PS2", g["God of War"].Platform);
        Assert.Equal(ScanStatus.Unknown, g["Crash"].Status);
        Assert.Contains(".iso", g["Crash"].Message);
        Assert.Equal(ScanStatus.Unknown, g["Patapon"].Status);
        Assert.Equal("PS2", g["Okami"].Platform);
        Assert.Equal("PS2", g["Shadow of the Colossus"].Platform);
        Assert.Equal("PS1", g["Spyro"].Platform);
        Assert.Equal("PS2", g["Ape Escape 2"].Platform);
        Assert.Equal("PS2", g["khong doc duoc"].Platform);
    }

    [Fact]
    public void CueBin_Ps2_And_MultiDisc_M3u()
    {
        using var env = new TestEnv();
        env.File("x/Ridge Racer V/rrv.bin", Fake.Disc(Fake.Ps2Cnf, raw: true));
        env.Text("x/Ridge Racer V/rrv.cue", "FILE \"rrv.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        foreach (var n in new[] { 1, 2 })
        {
            env.File($"PS2/Xenosaga III (Disc {n}).bin", Fake.Disc(Fake.Ps2Cnf, raw: true));
            env.Text($"PS2/Xenosaga III (Disc {n}).cue", $"FILE \"Xenosaga III (Disc {n}).bin\" BINARY\n");
        }

        var games = env.Scan();
        var rrv = games.Single(g => g.Title == "Ridge Racer V");
        Assert.Equal("PS2", rrv.Platform);
        Assert.EndsWith("rrv.cue", rrv.LaunchFile);

        var xeno = games.Single(g => g.Title == "Xenosaga III");
        Assert.Equal("PS2", xeno.Platform);
        Assert.Equal(2, xeno.Discs.Count);
        Assert.StartsWith(Path.Combine(env.Paths.PlaylistsDir, "PS2"), xeno.LaunchFile);
        Assert.EndsWith(".m3u", xeno.LaunchFile);
    }

    [Fact]
    public void LooseBin_Ps2_RunsDirectly()
    {
        using var env = new TestEnv();
        var bin = env.File("misc/Gran Turismo 3.bin", Fake.Disc(Fake.Ps2Cnf, raw: true));
        var g = Assert.Single(env.Scan());
        Assert.Equal("PS2", g.Platform);
        Assert.Equal(Path.GetFullPath(bin), g.LaunchFile);
    }

    [Fact]
    public void LooseDiscs_WithDiscNumbers_MergedIntoOneGame()
    {
        using var env = new TestEnv();
        env.File("PS1/Final Fantasy VIII (Disc 1).chd", Fake.Chd("CHT2"));
        env.File("PS1/Final Fantasy VIII (Disc 2).chd", Fake.Chd("CHT2"));
        env.File("PS1/Final Fantasy VIII (Disc 3).chd", Fake.Chd("CHT2"));
        env.File("PS1/Vagrant Story (Disc 1).chd", Fake.Chd("CHT2"));

        var games = env.Scan();
        Assert.Equal(2, games.Count);
        var ff8 = games.Single(g => g.Title == "Final Fantasy VIII");
        Assert.Equal(3, ff8.Discs.Count);
        Assert.EndsWith(".m3u", ff8.LaunchFile);
        Assert.Contains("Disc 1", ff8.SourceFile);
        Assert.Equal(3, File.ReadAllLines(ff8.LaunchFile).Length);
    }

    [Fact]
    public void TrackBins_WithoutCue_GetTempCue()
    {
        using var env = new TestEnv();
        env.File("PS1/Castlevania (Track 1).bin", TestEnv.Ps1Disc());
        env.File("PS1/Castlevania (Track 2).bin", new byte[4704]);
        env.File("PS1/Castlevania (Track 3).bin", new byte[4704]);

        var g = Assert.Single(env.Scan());
        Assert.Equal("PS1", g.Platform);
        Assert.EndsWith(".cue", g.LaunchFile);
        var cue = CueParser.Parse(g.LaunchFile);
        Assert.True(cue.IsValid);
        Assert.Equal(3, cue.Files.Count);
        Assert.Contains("TRACK 03 AUDIO", File.ReadAllText(g.LaunchFile));
    }

    [Fact]
    public void Ps2Bios_Sync_And_Check()
    {
        using var env = new TestEnv();
        var ps2 = env.Catalog.Get("PS2")!;
        Assert.Equal(BiosState.Missing, BiosChecker.Check(ps2, env.Paths.BiosDir));

        // Người dùng chép BIOS thẳng vào BIOS/ (tên tùy ý) kèm file phụ cùng tên
        File.WriteAllBytes(Path.Combine(env.Paths.BiosDir, "BIOS cua toi.bin"), Fake.Ps2Bios());
        File.WriteAllBytes(Path.Combine(env.Paths.BiosDir, "BIOS cua toi.rom1"), new byte[16]);
        File.WriteAllBytes(Path.Combine(env.Paths.BiosDir, "scph5501.bin"), new byte[512 * 1024]); // BIOS PS1 không bị chép
        BiosChecker.Prepare(ps2, env.Paths.BiosDir);

        var core = Ps2Bios.CoreDir(env.Paths.BiosDir);
        Assert.True(File.Exists(Path.Combine(core, "BIOS cua toi.bin")));
        Assert.True(File.Exists(Path.Combine(core, "BIOS cua toi.rom1")));
        Assert.False(File.Exists(Path.Combine(core, "scph5501.bin")));
        Assert.Equal(BiosState.Present, BiosChecker.Check(ps2, env.Paths.BiosDir));
        Assert.Equal(0, Ps2Bios.Sync(env.Paths.BiosDir)); // không chép lại lần nữa
    }

    [Fact]
    public void BiosInGamesFolder_NotAGame_ImportedToBios()
    {
        using var env = new TestEnv();
        env.File("PS2/SCPH-70012.bin", Fake.Ps2Bios());
        env.File("scph1001.bin", new byte[512 * 1024]);
        var scanner = new GameScanner(env.Catalog, env.Paths.GamesDir, env.Paths.PlaylistsDir);
        Assert.Empty(scanner.Scan());
        Assert.Equal(2, scanner.BiosFiles.Count);

        Assert.Equal(2, Ps2Bios.ImportFromGames(scanner.BiosFiles, env.Paths.BiosDir));
        Assert.True(File.Exists(Path.Combine(Ps2Bios.CoreDir(env.Paths.BiosDir), "SCPH-70012.bin")));
        Assert.True(File.Exists(Path.Combine(env.Paths.BiosDir, "scph1001.bin")));
    }

    [Fact]
    public void Launcher_Ps2WithoutBios_ExplainsWhereToPutIt()
    {
        using var env = new TestEnv();
        // Thư mục ứng dụng giả có RetroArch và core PS2
        var appDir = Path.Combine(Path.GetDirectoryName(env.Root)!, "app");
        var paths = new AppPaths(appDir, env.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.RetroArchExe)!);
        File.WriteAllText(paths.RetroArchExe, "");
        Directory.CreateDirectory(paths.CoresDir);
        File.WriteAllText(paths.CorePath("pcsx2_libretro.dll"), "");
        var iso = env.File("PS2/game.iso", Fake.Disc(Fake.Ps2Cnf, raw: false));

        var launcher = new EmulatorLauncher(paths, env.Catalog, new AppSettings());
        var game = new GameRecord { Platform = "PS2", LaunchFile = iso, SourceFile = iso };
        var plan = launcher.Plan(game);
        Assert.Equal(LaunchProblem.BiosMissing, plan.Problem);
        Assert.Contains("PS2", plan.Message);

        File.WriteAllBytes(Path.Combine(paths.BiosDir, "ps2.bin"), Fake.Ps2Bios());
        plan = launcher.Plan(game);
        Assert.Equal(LaunchProblem.None, plan.Problem);
        Assert.Equal(paths.CorePath("pcsx2_libretro.dll"), plan.StartInfo!.ArgumentList[3]);
    }
}

/// <summary>Game nhiều đĩa (ví dụ The Legend of Dragoon, 4 đĩa PS1) theo các cách sắp xếp hay gặp.</summary>
public class MultiDiscLayoutTests
{
    private static void Disc(TestEnv env, string folder, string name)
    {
        env.File($"{folder}/{name}.bin", TestEnv.Ps1Disc());
        env.Text($"{folder}/{name}.cue", $"FILE \"{name}.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
    }

    [Fact]
    public void Redump_AllDiscsInOneFolder()
    {
        using var env = new TestEnv();
        for (int n = 1; n <= 4; n++) Disc(env, "PS1/Legend of Dragoon", $"Legend of Dragoon, The (USA) (Disc {n})");
        var db = new GameDatabase(env.Paths.DatabaseFile);
        db.Initialize(env.Catalog);
        db.ApplyScan(env.Scan());

        var g = Assert.Single(db.GetGames());
        Assert.Equal("Legend of Dragoon", g.Title);
        Assert.Equal(4, g.DiscCount);
        Assert.Equal("Legend of Dragoon, The (USA).m3u", Path.GetFileName(g.LaunchFile));
    }

    [Fact]
    public void OneFolderPerDisc_FilesWithoutDiscNumber()
    {
        using var env = new TestEnv();
        // Bản Việt hóa hay chia mỗi đĩa một thư mục, file bên trong cùng tên
        for (int n = 1; n <= 4; n++) Disc(env, $"PS1/The Legend of Dragoon (Disc {n}) (VN)", "LOD");
        env.File("PS1/The Legend of Dragoon (Disc 1) (VN)/huong dan.txt");

        var g = Assert.Single(env.Scan());
        Assert.Equal("PS1", g.Platform);
        Assert.Equal("The Legend of Dragoon", g.Title);
        Assert.Equal(4, g.Discs.Count);
        Assert.Contains("(Disc 1)", g.SourceFile);
        // Mọi đĩa chạy qua một playlist → đổi đĩa được, dùng chung thẻ nhớ "LOD.srm" (giữ save của đĩa 1 nếu đã chơi trước đó)
        Assert.Equal("LOD.m3u", Path.GetFileName(g.LaunchFile));
        var lines = File.ReadAllLines(g.LaunchFile);
        Assert.Equal(4, lines.Length);
        Assert.Contains("(Disc 4) (VN)", lines[3]);
    }

    [Fact]
    public void GameFolder_WithDiscSubfolders()
    {
        using var env = new TestEnv();
        Disc(env, "Legend of Dragoon/CD1", "lod1");
        Disc(env, "Legend of Dragoon/CD2", "lod2");
        // Chỉ có đĩa 1 của game khác: không ghép nhầm
        Disc(env, "Vagrant Story (Disc 1)", "vs");

        var games = env.Scan();
        Assert.Equal(2, games.Count);
        var lod = games.Single(g => g.Title == "Legend of Dragoon");
        Assert.Equal(2, lod.Discs.Count);
        Assert.Equal(0, games.Single(g => g.Title == "Vagrant Story").Discs.Count);
    }
}

public class ArchiveAndFormatTests
{
    private static void Zip(string path, params (string Name, byte[] Data)[] entries)
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
    public void NotPlayableFiles_AreExplained()
    {
        using var env = new TestEnv();
        env.File("x/Chrono Trigger.7z");
        env.File("x/FF7.part1.rar");
        env.File("x/FF7.part2.rar");                      // phần sau: không báo
        env.File("x/Tekken 3.7z.001");
        env.File("x/Tekken 3.7z.002");
        env.File("x/Silent Hill.ecm");
        env.File("x/Mario 64.z64");
        env.File("x/._Mario.sfc");                        // rác macOS: bỏ qua
        env.File("x/cover.jpg");                          // không phải game: bỏ qua
        Directory.CreateDirectory(Path.Combine(env.Paths.GamesDir, "__MACOSX"));
        env.File("__MACOSX/Zelda.sfc");

        var g = env.Scan().ToDictionary(x => Path.GetFileName(x.SourceFile));
        Assert.Equal(5, g.Count);
        Assert.All(g.Values, x => Assert.Equal(ScanStatus.Unknown, x.Status));
        Assert.Contains("Chrono Trigger.7z", g["Chrono Trigger.7z"].Message);
        Assert.Equal("FF7", g["FF7.part1.rar"].Title);
        Assert.Equal("Tekken 3", g["Tekken 3.7z.001"].Title);
        Assert.Contains("unecm", g["Silent Hill.ecm"].Message);
        Assert.Contains("Nintendo 64", g["Mario 64.z64"].Message);
    }

    [Fact]
    public void Zip_SpecialCases()
    {
        using var env = new TestEnv();
        Zip(env.File("x/ps1.zip"), ("game.cue", new byte[8]), ("game.bin", TestEnv.Ps1Disc()));
        Zip(env.File("x/ps1bin.zip"), ("game.bin", TestEnv.Ps1Disc()));
        Zip(env.File("x/kof98.zip"), ("242-p1.p1", new byte[8]), ("242-m1.m1", new byte[8]), ("242-v1.v1", new byte[8]), ("242-c1.c1", new byte[8]));
        Zip(env.File("x/nested.zip"), ("inner.zip", new byte[8]));
        Zip(env.File("x/gba-as-bin.zip"), ("pokemon.bin", Fake.GbaRom()));
        Zip(env.File("x/mac.zip"), ("__MACOSX/._a.nes", new byte[8]), ("a.nes", Fake.NesRom()));
        Zip(env.File("x/folders.zip"), ("SNES/Mario.smc", new byte[16]), ("NES/Zelda.nes", Fake.NesRom()));
        var sevenZip = new byte[32];
        new byte[] { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(sevenZip, 0);
        env.File("x/really7z.zip", sevenZip);
        env.File("x/split.zip", new byte[100]);
        env.File("x/split.z01", new byte[100]);

        var g = env.Scan().ToDictionary(x => Path.GetFileName(x.SourceFile));
        Assert.Contains("giải nén", g["ps1.zip"].Message);
        Assert.Equal(g["ps1.zip"].Message, g["ps1bin.zip"].Message);
        Assert.Contains("arcade", g["kof98.zip"].Message);
        Assert.Contains("nested.zip", g["nested.zip"].Message);
        Assert.Equal("GBA", g["gba-as-bin.zip"].Platform);
        Assert.Equal("NES", g["mac.zip"].Platform);
        Assert.Equal("SNES", g["Mario.smc"].Platform);      // "folders.zip#SNES/Mario.smc"
        Assert.Equal("NES", g["Zelda.nes"].Platform);
        Assert.Contains("really7z.zip", g["really7z.zip"].Message);
        Assert.Contains("nhiều phần", g["split.zip"].Message);
        Assert.DoesNotContain("split.z01", g.Keys);
    }

    [Fact]
    public void LooseFiles_ByHeader_And_Pbp()
    {
        using var env = new TestEnv();
        env.File("x/Zelda.rom", Fake.NesRom());
        env.File("x/Pokemon Gold.bin", Fake.GbRom(0x80));
        env.File("x/Sonic SMS.sms", Fake.SegaMasterRom(4));
        env.File("x/Columns.gg", Fake.SegaMasterRom(7));
        env.File("x/Mario 64.bin", new byte[] { 0x80, 0x37, 0x12, 0x40, 0, 0, 0, 0 });
        env.File("x/Crash/EBOOT.PBP", Fake.Pbp("PSISOIMG0000"));
        env.File("x/Patapon/EBOOT.PBP", Fake.Pbp("NPUMDIMG"));

        var games = env.Scan();
        Assert.Equal("NES", games.Single(g => g.Title == "Zelda").Platform);
        Assert.Equal("GBC", games.Single(g => g.Title == "Pokemon Gold").Platform);
        Assert.Equal("MasterSystem", games.Single(g => g.Title == "Sonic SMS").Platform);
        Assert.Equal("GameGear", games.Single(g => g.Title == "Columns").Platform);
        Assert.Contains("Nintendo 64", games.Single(g => g.Title == "Mario 64").Message);
        Assert.Equal("PS1", games.Single(g => g.Title == "Crash").Platform);
        Assert.Equal(ScanStatus.Unknown, games.Single(g => g.SourceFile.Contains("Patapon")).Status);
    }

    [Fact]
    public void CloneCd_Ps1_And_Ps2()
    {
        using var env = new TestEnv();
        env.Text("x/Tomba/tomba.ccd", "[CloneCD]\nVersion=3\n");
        env.File("x/Tomba/tomba.img", TestEnv.Ps1Disc());
        env.File("x/Tomba/tomba.sub", new byte[96]);
        env.Text("x/Ico/ico.ccd", "[CloneCD]\n");
        var img = env.File("x/Ico/ico.img", Fake.Disc(Fake.Ps2Cnf, raw: true));

        var g = env.Scan().ToDictionary(x => x.Title);
        Assert.Equal("PS1", g["Tomba"].Platform);
        Assert.EndsWith("tomba.ccd", g["Tomba"].LaunchFile);
        Assert.Equal("PS2", g["Ico"].Platform);
        Assert.Equal(Path.GetFullPath(img), g["Ico"].LaunchFile);
    }
}
