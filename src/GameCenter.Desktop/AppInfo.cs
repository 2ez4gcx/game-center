using System.Reflection;

namespace GameCenter.Desktop;

/// <summary>Thông tin tác giả và phiên bản hiển thị trong app.</summary>
public static class AppInfo
{
    public const string Author = "Khuong Doan";
    public const string Contact = "khuongdoan.com";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0";
}
