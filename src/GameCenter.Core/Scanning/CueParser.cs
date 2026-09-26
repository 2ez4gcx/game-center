using System.Text;
using System.Text.RegularExpressions;

namespace GameCenter.Core.Scanning;

public sealed record CueFileRef(string NameInCue, string? ResolvedPath);

public sealed class CueInfo
{
    public string CuePath { get; init; } = "";
    public List<CueFileRef> Files { get; } = new();
    public IEnumerable<CueFileRef> Missing => Files.Where(f => f.ResolvedPath == null);
    public bool IsValid => Files.Count > 0 && !Missing.Any();
}

/// <summary>Đọc và kiểm tra file .cue (mục 8.5). Chỉ đọc, không tự sửa.</summary>
public static partial class CueParser
{
    [GeneratedRegex("^\\s*FILE\\s+(?:\"(?<q>[^\"]+)\"|(?<u>\\S+))\\s+\\S+\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FileLine();

    public static CueInfo Parse(string cuePath)
    {
        var info = new CueInfo { CuePath = cuePath };
        var dir = Path.GetDirectoryName(cuePath)!;
        foreach (var line in ReadLines(cuePath))
        {
            var m = FileLine().Match(line);
            if (!m.Success) continue;
            var name = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["u"].Value;
            info.Files.Add(new CueFileRef(name, Resolve(dir, name)));
        }
        return info;
    }

    /// <summary>Tìm file được tham chiếu, không phân biệt hoa thường.</summary>
    private static string? Resolve(string dir, string name)
    {
        var candidate = Path.IsPathRooted(name) ? name : Path.Combine(dir, name);
        if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        var targetDir = Path.GetDirectoryName(candidate);
        var fileName = Path.GetFileName(candidate);
        if (targetDir == null || !Directory.Exists(targetDir)) return null;
        return Directory.EnumerateFiles(targetDir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Đọc .cue: thử UTF-8 (có/không BOM), nếu lỗi thì dùng code page hệ thống.</summary>
    private static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return text.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
    }

    /// <summary>Nội dung .cue tạm cho file .bin PS1 đơn (mục 8.4).</summary>
    public static string BuildTempCue(string binPath) =>
        $"FILE \"{binPath}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n";
}
