using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace GameCenter.Core.Localization;

/// <summary>
/// Bảng chuỗi giao diện của một ngôn ngữ (Localization/Lang/&lt;mã&gt;.json, nhúng trong GameCenter.Core).
/// Thiếu chuỗi thì dùng bảng dự phòng (tiếng Việt), thiếu nữa thì hiện tên khóa.
/// Khóa bắt đầu bằng "_" là thông tin của bảng (tên ngôn ngữ, định dạng ngày), không phải chuỗi giao diện.
/// </summary>
public sealed class Strings
{
    private readonly IReadOnlyDictionary<string, string> _map;
    private readonly Strings? _fallback;

    public string Code { get; }
    /// <summary>Tên ngôn ngữ viết bằng chính ngôn ngữ đó ("Tiếng Việt", "English").</summary>
    public string Name => _map.GetValueOrDefault("_name") ?? Code;
    public CultureInfo Culture { get; }
    public IEnumerable<string> Keys => _map.Keys.Where(k => !k.StartsWith('_'));

    public Strings(string code, IReadOnlyDictionary<string, string> map, Strings? fallback = null)
    {
        Code = code;
        _map = map;
        _fallback = fallback;
        try { Culture = CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    public bool Has(string key) => _map.ContainsKey(key) || (_fallback?.Has(key) ?? false);

    public string this[string key] =>
        _map.TryGetValue(key, out var v) ? v : _fallback != null ? _fallback[key] : key;

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    public static Strings Parse(string code, string json, Strings? fallback = null)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? new();
        return new Strings(code, map, fallback);
    }
}

/// <summary>
/// Ngôn ngữ giao diện đang dùng. Thêm ngôn ngữ mới: thêm file Localization/Lang/&lt;mã&gt;.json
/// (cùng khóa với vi.json), không phải sửa mã.
/// </summary>
public static class Loc
{
    public const string Vietnamese = "vi";
    public const string English = "en";

    private const string ResourcePrefix = "GameCenter.Core.Localization.Lang.";
    private static readonly Lazy<Strings> VietnameseTable = new(() => LoadEmbedded(Vietnamese, null)!);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Strings?> Cache = new();

    /// <summary>Mặc định tiếng Việt (ngôn ngữ gốc của Game Center) cho tới khi gọi <see cref="Use"/>.</summary>
    public static Strings Current { get; private set; } = VietnameseTable.Value;

    public static string Language => Current.Code;

    /// <summary>Các ngôn ngữ có sẵn: (mã, tên).</summary>
    public static IReadOnlyList<(string Code, string Name)> Available =>
        typeof(Loc).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(".json"))
            .Select(n => n[ResourcePrefix.Length..^".json".Length])
            .OrderBy(c => c == Vietnamese ? 0 : 1).ThenBy(c => c)
            .Select(c => (c, Load(c)?.Name ?? c))
            .ToList();

    public static bool IsAvailable(string? code) =>
        code != null && Available.Any(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bảng chuỗi của một ngôn ngữ (dự phòng bằng tiếng Việt), null nếu không có.</summary>
    public static Strings? Load(string code) =>
        code == Vietnamese ? VietnameseTable.Value : Cache.GetOrAdd(code, c => LoadEmbedded(c, VietnameseTable.Value));

    /// <summary>Đổi ngôn ngữ giao diện. Mã không có thì dùng tiếng Việt.</summary>
    public static void Use(string? code) =>
        Current = (code == null ? null : Load(code.ToLowerInvariant())) ?? VietnameseTable.Value;

    public static string T(string key) => Current[key];
    public static string T(string key, params object?[] args) => Current.Format(key, args);

    /// <summary>Chuỗi có số đếm: dùng "&lt;khóa&gt;.one" khi n = 1 nếu ngôn ngữ có (ví dụ "1 game" / "2 games").</summary>
    public static string Plural(string key, long n) =>
        Current.Format(n == 1 && Current.Has(key + ".one") ? key + ".one" : key, n);

    /// <summary>Ngày theo định dạng của ngôn ngữ (khóa "_date_format").</summary>
    public static string Date(DateTime d) => d.ToString(Current["_date_format"], Current.Culture);

    /// <summary>
    /// Ngôn ngữ cho lần chạy đầu tiên (chưa chọn trong Cài đặt):
    /// 1. ngôn ngữ đã chọn trong bộ cài Windows (Defaults/installer.ini, "Language=en");
    /// 2. đã có dữ liệu từ bản cũ (chỉ có tiếng Việt) → tiếng Việt;
    /// 3. ngôn ngữ của hệ điều hành: tiếng Việt nếu là tiếng Việt, còn lại tiếng Anh.
    /// </summary>
    public static string DetectDefault(string installerIni, bool upgradedFromOldVersion, CultureInfo? osCulture = null)
    {
        var fromInstaller = ReadInstallerLanguage(installerIni);
        if (IsAvailable(fromInstaller)) return fromInstaller!.ToLowerInvariant();
        if (upgradedFromOldVersion) return Vietnamese;
        var os = (osCulture ?? CultureInfo.InstalledUICulture).TwoLetterISOLanguageName;
        return IsAvailable(os) ? os : English;
    }

    private static string? ReadInstallerLanguage(string iniFile)
    {
        try
        {
            if (!File.Exists(iniFile)) return null;
            foreach (var line in File.ReadAllLines(iniFile))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals("Language", StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim();
            }
        }
        catch { /* không đọc được → bỏ qua */ }
        return null;
    }

    private static Strings? LoadEmbedded(string code, Strings? fallback)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream(ResourcePrefix + code + ".json");
        if (s == null) return null;
        using var reader = new StreamReader(s);
        return Strings.Parse(code, reader.ReadToEnd(), fallback);
    }
}
