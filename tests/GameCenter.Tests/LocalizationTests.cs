using System.Globalization;
using System.Text.RegularExpressions;
using GameCenter.Core.Config;
using GameCenter.Core.Emulation;
using GameCenter.Core.Localization;

namespace GameCenter.Tests;

public class LocalizationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "GameCenter.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("GameCenter.sln");
    }

    [Fact]
    public void Languages_Available()
    {
        var codes = Loc.Available.Select(a => a.Code).ToList();
        Assert.Equal("vi", codes[0]);
        Assert.Contains("en", codes);
        Assert.Equal("English", Loc.Load("en")!.Name);
        Assert.Equal("Tiếng Việt", Loc.Load("vi")!.Name);
    }

    /// <summary>Mọi ngôn ngữ có đủ khóa của tiếng Việt, cùng các chỗ điền {0}, {1}...</summary>
    [Fact]
    public void AllLanguages_HaveSameKeys_AndPlaceholders()
    {
        var vi = Loc.Load("vi")!;
        foreach (var (code, _) in Loc.Available.Where(a => a.Code != "vi"))
        {
            var other = Loc.Load(code)!;
            // Chuỗi số nhiều ".one" chỉ có ở ngôn ngữ cần
            var extra = other.Keys.Where(k => !k.EndsWith(".one") && !vi.Keys.Contains(k)).ToList();
            var missing = vi.Keys.Where(k => !other.Keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{code} thiếu: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{code} thừa: {string.Join(", ", extra)}");
            foreach (var k in vi.Keys)
                Assert.True(Placeholders(vi[k]).SetEquals(Placeholders(other[k])), $"{code}: chỗ điền khác nhau ở \"{k}\"");
        }
    }

    private static HashSet<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();

    /// <summary>Mọi khóa dùng trong mã nguồn (C# và XAML) đều có trong file ngôn ngữ.</summary>
    [Fact]
    public void KeysUsedInCode_Exist()
    {
        var prefixes = "app|startup|dialog|main|tab|scan|play|launch|more|rename|platform|cover|game|settings|ra|backup|keys|help";
        var keyLiteral = new Regex($"\"((?:{prefixes})\\.[a-z0-9_.]+)\"");
        var xamlKey = new Regex(@"\{local:T\s+([a-z0-9_.]+)\}");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in keyLiteral.Matches(text))
                if (!Regex.IsMatch(m.Groups[1].Value, @"\.(json|cfg|db|log|ini|txt)$")) used.Add(m.Groups[1].Value); // tên file, không phải khóa
            foreach (Match m in xamlKey.Matches(text)) used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 100, $"Chỉ tìm thấy {used.Count} khóa");
        foreach (var (code, _) in Loc.Available)
        {
            var table = Loc.Load(code)!;
            var missing = used.Where(k => !table.Has(k) && !k.EndsWith('.')).ToList();
            Assert.True(missing.Count == 0, $"{code} thiếu khóa dùng trong mã: {string.Join(", ", missing)}");
        }
        // Khóa ghép động: nút tay cầm và phím
        foreach (var b in KeyboardLayout.Buttons)
            Assert.True(Loc.Load("en")!.Has("pad." + b.Id) && Loc.Load("en")!.Has("pad." + b.Id + ".hint"));
    }

    [Fact]
    public void Strings_FallBack_And_Plural()
    {
        var en = Loc.Load("en")!;
        Assert.Equal("Settings", en["settings.title"]);
        Assert.Equal("khong.co.khoa", en["khong.co.khoa"]);
        Assert.Equal("played once", en.Format(en.Has("game.played.one") ? "game.played.one" : "game.played", 1));
        Assert.Equal("đã chơi 3 lần", Loc.Load("vi")!.Format("game.played", 3));
    }

    [Fact]
    public void DefaultLanguage_InstallerThenUpgradeThenOs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gc-lang-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var ini = Path.Combine(dir, "installer.ini");
            var vi = CultureInfo.GetCultureInfo("vi-VN");
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            // Không có installer.ini: bản cũ → tiếng Việt; máy mới → theo hệ điều hành, không có thì tiếng Anh
            Assert.Equal("vi", Loc.DetectDefault(ini, upgradedFromOldVersion: true, fr));
            Assert.Equal("vi", Loc.DetectDefault(ini, false, vi));
            Assert.Equal("en", Loc.DetectDefault(ini, false, fr));
            // Ngôn ngữ chọn trong bộ cài được ưu tiên
            File.WriteAllText(ini, "[Setup]\r\nLanguage=en\r\n");
            Assert.Equal("en", Loc.DetectDefault(ini, true, vi));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RetroArch_FollowsLanguage()
    {
        Assert.Equal("15", RetroArchConfig.RetroArchLanguage(null));
        Assert.Equal("15", RetroArchConfig.RetroArchLanguage("vi"));
        Assert.Equal("0", RetroArchConfig.RetroArchLanguage("en"));
        using var env = new TestEnv();
        var keys = RetroArchConfig.ManagedKeys(env.Paths, new AppSettings { Language = "en" }, false);
        Assert.Equal("0", keys["user_language"]);
    }
}
