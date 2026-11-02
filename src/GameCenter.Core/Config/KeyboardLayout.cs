using GameCenter.Core.Localization;

namespace GameCenter.Core.Config;

/// <summary>Một nút của tay cầm ảo RetroPad (RetroArch) và phím bàn phím được gán.</summary>
public sealed record PadButtonInfo(string Id, string DefaultKey)
{
    /// <summary>Tên nút theo ngôn ngữ giao diện (khóa "pad.&lt;id&gt;").</summary>
    public string Label => Loc.T("pad." + Id);
    /// <summary>Gợi ý vị trí nút (khóa "pad.&lt;id&gt;.hint").</summary>
    public string Hint => Loc.T("pad." + Id + ".hint");
}

/// <summary>
/// Sơ đồ bàn phím mặc định: tay trái WASD + Q/E (L1/L2), tay phải IJKL + U/O (R1/R2),
/// giống cách cầm tay cầm thật. Esc (thoát game) và F1 (menu) được giữ riêng, không gán được.
/// Tên phím theo quy ước retroarch.cfg.
/// </summary>
public static class KeyboardLayout
{
    public static readonly IReadOnlyList<PadButtonInfo> Buttons = new PadButtonInfo[]
    {
        new("up",     "w"),
        new("down",   "s"),
        new("left",   "a"),
        new("right",  "d"),
        new("x",      "i"),
        new("y",      "j"),
        new("b",      "k"),
        new("a",      "l"),
        new("l",      "q"),
        new("l2",     "e"),
        new("r",      "u"),
        new("r2",     "o"),
        new("start",  "enter"),
        new("select", "space"),
    };

    /// <summary>Phím hệ thống, không cho gán vào nút game: Esc thoát, F1 menu, F2/F4 lưu/tải nhanh.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "escape", "f1", "f2", "f4" };

    /// <summary>
    /// Phím tắt mặc định của RetroArch trùng với sơ đồ trên (ví dụ K = frame advance, L = tua nhanh,
    /// Space = bật tua nhanh, P = tạm dừng...). Tắt hết để bấm nhầm không làm game chạy sai.
    /// </summary>
    public static readonly string[] HotkeysToDisable =
    {
        "input_frame_advance", "input_hold_fast_forward", "input_toggle_fast_forward",
        "input_hold_slowmotion", "input_toggle_slowmotion", "input_pause_toggle", "input_rewind",
        "input_toggle_fullscreen", "input_reset", "input_shader_next", "input_shader_prev",
        "input_movie_record_toggle", "input_state_slot_increase", "input_state_slot_decrease",
        "input_audio_mute", "input_osk_toggle", "input_netplay_game_watch", "input_cheat_index_plus",
        "input_cheat_index_minus", "input_cheat_toggle", "input_screenshot", "input_volume_up",
        "input_volume_down", "input_game_focus_toggle", "input_grab_mouse_toggle", "input_ai_service",
        "input_fps_toggle", "input_send_debug_info", "input_netplay_host_toggle", "input_netplay_ping_toggle",
        "input_toggle_statistics", "input_disk_eject_toggle", "input_disk_next", "input_disk_prev",
        "input_close_content", "input_toggle_vrr_runloop",
        "input_record_replay", "input_play_replay", "input_replay_slot_increase", "input_replay_slot_decrease",
        "input_preempt_toggle", "input_runahead_toggle", "input_overlay_next", "input_turbo_fire_toggle",
    };

    public static Dictionary<string, string> Defaults() =>
        Buttons.ToDictionary(b => b.Id, b => b.DefaultKey);

    /// <summary>Gộp cấu hình người dùng với mặc định (nút thiếu thì dùng mặc định).</summary>
    public static Dictionary<string, string> Resolve(IDictionary<string, string>? user)
    {
        var map = Defaults();
        if (user == null) return map;
        foreach (var b in Buttons)
            if (user.TryGetValue(b.Id, out var k) && !string.IsNullOrWhiteSpace(k) && !Reserved.Contains(k))
                map[b.Id] = k;
        return map;
    }

    /// <summary>Tên hiển thị của phím theo tên RetroArch.</summary>
    public static string DisplayName(string raKey) => raKey switch
    {
        "nul" or "" => "—",
        "enter" => "Enter",
        "space" => "Space",
        "shift" or "rshift" or "ctrl" or "rctrl" or "alt" or "ralt" => Loc.T("key." + raKey),
        "tab" => "Tab",
        "backspace" => "Backspace",
        "up" => "↑",
        "down" => "↓",
        "left" => "←",
        "right" => "→",
        _ when raKey.StartsWith("num") && raKey.Length == 4 => raKey[3..],
        _ when raKey.StartsWith("keypad") => "Numpad " + raKey[6..],
        _ when raKey.Length == 1 => raKey.ToUpperInvariant(),
        _ => char.ToUpperInvariant(raKey[0]) + raKey[1..],
    };
}
