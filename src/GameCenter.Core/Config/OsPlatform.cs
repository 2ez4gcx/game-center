using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameCenter.Core.Config;

public enum Os { Windows, MacOS, Linux }

/// <summary>
/// Khác biệt giữa Windows / macOS / Linux: vị trí RetroArch, đuôi file core,
/// driver hình / tay cầm mặc định, cách mở thư mục.
/// </summary>
public static class OsPlatform
{
    public static Os Current =>
        OperatingSystem.IsWindows() ? Os.Windows :
        OperatingSystem.IsMacOS() ? Os.MacOS : Os.Linux;

    /// <summary>File chạy RetroArch, tính từ thư mục Emulators.</summary>
    public static string RetroArchExe(string emulatorsDir, Os os) => os switch
    {
        Os.Windows => Path.Combine(emulatorsDir, "RetroArch", "retroarch.exe"),
        // Không sửa bên trong RetroArch.app (sẽ hỏng chữ ký); core để ngoài bundle
        Os.MacOS => Path.Combine(emulatorsDir, "RetroArch.app", "Contents", "MacOS", "RetroArch"),
        // Bản Linux: file chạy hoặc AppImage của RetroArch, đặt tên "retroarch"
        _ => Path.Combine(emulatorsDir, "RetroArch", "retroarch"),
    };

    /// <summary>Thư mục core: Windows/Linux trong RetroArch/cores, macOS trong Emulators/cores.</summary>
    public static string CoresDir(string emulatorsDir, Os os) =>
        os == Os.MacOS ? Path.Combine(emulatorsDir, "cores") : Path.Combine(emulatorsDir, "RetroArch", "cores");

    /// <summary>Thư mục phụ của RetroArch (info, assets, autoconfig): chỉ Windows/Linux có sẵn cạnh file chạy.</summary>
    public static string? RetroArchResourceDir(string emulatorsDir, Os os) =>
        os == Os.MacOS ? null : Path.Combine(emulatorsDir, "RetroArch");

    public static string CoreExtension(Os os) => os switch
    {
        Os.Windows => ".dll",
        Os.MacOS => ".dylib",
        _ => ".so",
    };

    /// <summary>Tên core trong platforms.json ghi dạng Windows ("x_libretro.dll"); đổi sang đuôi của hệ hiện tại.</summary>
    public static string CoreFileName(string core, Os os) =>
        Path.GetFileNameWithoutExtension(core) + CoreExtension(os);

    /// <summary>Driver tay cầm cho RetroArch (null: để RetroArch tự chọn).</summary>
    public static string? JoypadDriver(Os os) => os switch
    {
        Os.Windows => "xinput",
        Os.Linux => "udev",
        _ => null,
    };

    /// <summary>Máy có Vulkan không (macOS: không có Vulkan gốc → dùng renderer phần mềm).</summary>
    public static bool IsVulkanAvailable(Os os)
    {
        try
        {
            return os switch
            {
                Os.Windows => File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")),
                Os.Linux => new[] { "/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/lib/x86_64-linux-gnu" }
                    .Any(d => File.Exists(Path.Combine(d, "libvulkan.so.1"))),
                _ => false,
            };
        }
        catch { return false; }
    }

    /// <summary>Driver hình khi không dùng Vulkan.</summary>
    public static string FallbackVideoDriver(Os os) => os == Os.MacOS ? "glcore" : "gl";

    /// <summary>Mở thư mục bằng trình quản lý file của hệ điều hành.</summary>
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        var psi = Current switch
        {
            Os.Windows => new ProcessStartInfo("explorer.exe"),
            Os.MacOS => new ProcessStartInfo("open"),
            _ => new ProcessStartInfo("xdg-open"),
        };
        psi.UseShellExecute = false;
        psi.ArgumentList.Add(path);
        Process.Start(psi);
    }

    /// <summary>Mở file văn bản (giấy phép) bằng trình mặc định.</summary>
    public static void OpenFile(string path)
    {
        if (Current == Os.Windows)
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
        else
            Process.Start(new ProcessStartInfo(Current == Os.MacOS ? "open" : "xdg-open") { ArgumentList = { path }, UseShellExecute = false });
    }

    public static bool IsArm => RuntimeInformation.OSArchitecture == Architecture.Arm64;
}
