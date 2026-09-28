using System.Text.Json;

namespace GameCenter.Core.Config;

public static class ControlModes
{
    public const string Gamepad = "gamepad";
    public const string Keyboard = "keyboard";
}

public sealed class AppSettings
{
    /// <summary>Game chạy toàn màn hình.</summary>
    public bool Fullscreen { get; set; } = true;
    /// <summary>Launcher tự mở toàn màn hình.</summary>
    public bool LauncherFullscreen { get; set; } = false;
    /// <summary>Hiện hướng dẫn đổi đĩa khi chạy game nhiều đĩa.</summary>
    public bool ShowDiscChangeGuide { get; set; } = true;
    /// <summary>
    /// Khi thoát game, hỏi có lưu lại chỗ đang chơi (save state của giả lập) không;
    /// lần mở sau hỏi chơi tiếp hay chơi từ đầu. Save trong game (.srm) luôn được giữ, không phụ thuộc tùy chọn này.
    /// </summary>
    public bool AskSaveStateOnExit { get; set; } = true;

    /// <summary>Cài đặt nâng cao: "Dùng DuckStation mà tôi đã tự cài" (mục 10.3). Không đóng gói DuckStation.</summary>
    public bool UseUserDuckStation { get; set; } = false;
    public string? DuckStationPath { get; set; }

    /// <summary>Renderer Beetle PSX HW: "auto" (Vulkan nếu có, không thì software), "vulkan", "software".</summary>
    public string Ps1Renderer { get; set; } = "auto";

    /// <summary>Cách điều khiển chính: "gamepad" (tay cầm) hoặc "keyboard" (bàn phím).</summary>
    public string ControlMode { get; set; } = ControlModes.Gamepad;

    /// <summary>Phím người dùng tự gán: id nút RetroPad → tên phím RetroArch. Thiếu thì dùng mặc định.</summary>
    public Dictionary<string, string> KeyBindings { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch { /* file hỏng → dùng mặc định */ }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
