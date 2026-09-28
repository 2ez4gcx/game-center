using System.Reflection;

namespace GameCenter.App;

/// <summary>Thông tin tác giả và phiên bản hiển thị trong app.</summary>
public static class AppInfo
{
    public const string Author = "2ez4gcx";
    public const string Contact = "github.com/2ez4gcx";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0";
}
