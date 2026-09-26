using System.Text;
using GameCenter.Core.Config;

namespace GameCenter.Core.Emulation;

/// <summary>
/// Sinh retroarch.cfg trong thư mục dữ liệu (mục 10.2).
/// Chỉ ghi đè các khóa Game Center quản lý; giữ nguyên các khóa khác
/// (ví dụ cấu hình tay cầm người dùng đã làm trong menu RetroArch).
/// </summary>
public static class RetroArchConfig
{
    /// <summary>
    /// input_quit_gamepad_combo / menu_toggle_gamepad_combo của RetroArch:
    /// 0 none, 1 Down+Y+L1+R1, 2 L3+R3, 3 L1+R1+Start+Select, 4 Start+Select, 5 L3+R1, 6 L1+R1,
    /// 7 giữ Start, 8 giữ Select, 9 Down+Select, 10 L2+R2.
    /// Cần kiểm tra lại với phiên bản RetroArch đóng gói.
    /// </summary>
    public const string QuitComboStartSelect = "4";
    public const string MenuComboL3R3 = "2";

    public static Dictionary<string, string> ManagedKeys(AppPaths paths, AppSettings settings, bool vulkanAvailable)
    {
        bool useVulkan = settings.Ps1Renderer switch
        {
            "vulkan" => true,
            "software" => false,
            _ => vulkanAvailable,
        };

        return new Dictionary<string, string>
        {
            // Thư mục
            ["savefile_directory"] = paths.SavesDir,
            ["savestate_directory"] = paths.StatesDir,
            ["system_directory"] = paths.BiosDir,
            ["libretro_directory"] = paths.CoresDir,
            ["libretro_info_path"] = Path.Combine(paths.RetroArchDir, "info"),
            ["assets_directory"] = Path.Combine(paths.RetroArchDir, "assets"),
            ["joypad_autoconfig_dir"] = Path.Combine(paths.RetroArchDir, "autoconfig"),
            ["core_options_path"] = paths.CoreOptionsCfg,
            ["global_core_options"] = "true",
            ["log_dir"] = paths.LogsDir,

            // Màn hình
            ["video_fullscreen"] = settings.Fullscreen ? "true" : "false",
            ["video_driver"] = useVulkan ? "vulkan" : "gl",

            // Thoát game: giữ START + SELECT trên tay cầm, hoặc Esc trên bàn phím
            ["input_quit_gamepad_combo"] = QuitComboStartSelect,
            ["input_exit_emulator"] = "escape",
            ["quit_press_twice"] = "false",
            ["confirm_quit"] = "false",
            // Mở menu (đổi đĩa, cấu hình tay cầm): L3+R3 hoặc F1
            ["menu_toggle_gamepad_combo"] = MenuComboL3R3,
            ["input_menu_toggle"] = "f1",

            // Tay cầm Xbox/XInput mặc định, tự nhận cấu hình
            ["input_joypad_driver"] = "xinput",
            ["input_autodetect_enable"] = "true",

            // Tắt thông báo kỹ thuật
            ["notification_show_autoconfig"] = "false",
            ["notification_show_config_override_load"] = "false",
            ["notification_show_remap_load"] = "false",
            ["notification_show_set_initial_disk"] = "true",
            ["notification_show_fast_forward"] = "false",
            ["notification_show_refresh_rate"] = "false",
            ["notification_show_patch_applied"] = "false",
            ["notification_show_cheats_applied"] = "false",
            ["notification_show_netplay_extra"] = "false",
            ["menu_show_core_updater"] = "false",
            ["menu_show_online_updater"] = "false",
            ["pause_nonactive"] = "false",
            ["sort_savefiles_enable"] = "true",
            ["sort_savestates_enable"] = "true",
            ["savestate_auto_index"] = "false",
            ["config_save_on_exit"] = "true",
        };
    }

    /// <summary>Tùy chọn core (retroarch-core-options.cfg).</summary>
    public static Dictionary<string, string> ManagedCoreOptions(AppSettings settings, bool vulkanAvailable)
    {
        bool useVulkan = settings.Ps1Renderer switch
        {
            "vulkan" => true,
            "software" => false,
            _ => vulkanAvailable,
        };
        // Beetle PSX HW: Vulkan mặc định, không có Vulkan → software. Không dùng OpenGL renderer làm mặc định.
        return new Dictionary<string, string>
        {
            ["beetle_psx_hw_renderer"] = useVulkan ? "hardware_vk" : "software",
        };
    }

    public static void Write(AppPaths paths, AppSettings settings)
    {
        bool vulkan = IsVulkanAvailable();
        MergeWrite(paths.RetroArchCfg, ManagedKeys(paths, settings, vulkan), paths.RetroArchBaseCfg);
        MergeWrite(paths.CoreOptionsCfg, ManagedCoreOptions(settings, vulkan), null);
    }

    /// <summary>Ghép: base (Defaults/retroarch-base.cfg) → file hiện có → khóa quản lý.</summary>
    public static void MergeWrite(string target, IDictionary<string, string> managed, string? baseFile)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        void Load(string file)
        {
            if (!File.Exists(file)) return;
            foreach (var (k, v) in Parse(File.ReadAllLines(file, Encoding.UTF8)))
            {
                if (!values.ContainsKey(k)) order.Add(k);
                values[k] = v;
            }
        }
        if (baseFile != null) Load(baseFile);
        Load(target);
        foreach (var (k, v) in managed)
        {
            if (!values.ContainsKey(k)) order.Add(k);
            values[k] = v;
        }

        var sb = new StringBuilder();
        foreach (var k in order) sb.Append(k).Append(" = \"").Append(values[k]).Append("\"\n");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, sb.ToString(), new UTF8Encoding(false));
    }

    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') val = val[1..^1];
            yield return (key, val);
        }
    }

    /// <summary>Kiểm tra sơ bộ máy có driver Vulkan (vulkan-1.dll).</summary>
    public static bool IsVulkanAvailable()
    {
        try { return File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")); }
        catch { return false; }
    }
}
