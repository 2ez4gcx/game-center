using System.Text.RegularExpressions;

namespace GameCenter.Core.Scanning;

/// <summary>Làm sạch tên hiển thị (mục 7.4). Không bao giờ đổi tên file thật.</summary>
public static partial class NameCleaner
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"[\s_\-\.]*(?<!\p{L})(disc|disk|cd)[\s_\-\.]*\d+(?!\d).*$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscSuffix();

    [GeneratedRegex(@"(?<!\p{L})(disc|disk|cd)[\s_\-\.]*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscNumber();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static string Clean(string fileNameOrTitle)
    {
        var s = Brackets().Replace(fileNameOrTitle, "");
        s = s.Replace('_', ' ');
        s = Spaces().Replace(s, " ").Trim(' ', '-', '.', ',');
        return string.IsNullOrWhiteSpace(s) ? fileNameOrTitle.Trim() : s;
    }

    /// <summary>Tên file không có extension, đã bỏ phần "Disc N" để ghép các đĩa cùng game.</summary>
    public static string StripDisc(string name)
    {
        var noBrackets = Regex.Replace(name, @"\s*[\(\[]\s*(disc|disk|cd)\s*\d+[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        return DiscSuffix().Replace(noBrackets, "").Trim(' ', '_', '-', '.');
    }

    /// <summary>Số đĩa trong tên file, hoặc null nếu không có mẫu disc1/disc 1/(Disc 1)/CD1.</summary>
    public static int? DiscNumberOf(string name)
    {
        var m = DiscNumber().Match(name);
        return m.Success ? int.Parse(m.Groups[2].Value) : null;
    }
}
