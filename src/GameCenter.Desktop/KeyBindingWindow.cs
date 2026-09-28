using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GameCenter.Core.Config;
using GameCenter.Core.Emulation;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

/// <summary>
/// Thiết lập phím bàn phím cho từng nút tay cầm ảo.
/// Bấm vào ô phím → nhấn phím mới. Phím đã dùng cho nút khác thì hai nút đổi phím cho nhau.
/// </summary>
public sealed class KeyBindingWindow : Window
{
    private readonly Dictionary<string, string> _map;
    private readonly Dictionary<string, Button> _caps = new();
    private string? _listening;
    private readonly TextBlock _status;

    private static readonly string[] LeftHand = { "up", "left", "down", "right", "l", "l2", "select" };
    private static readonly string[] RightHand = { "x", "y", "b", "a", "r", "r2", "start" };

    public KeyBindingWindow()
    {
        Title = "Thiết lập phím";
        Width = 1040;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 900;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _map = KeyboardLayout.Resolve(App.Settings.KeyBindings);

        var root = new StackPanel { Margin = new Thickness(32, 26, 32, 26) };
        root.Children.Add(Dialogs.Heading("⌨  Thiết lập phím"));
        root.Children.Add(Dialogs.Para("Bấm vào ô phím rồi nhấn phím bạn muốn dùng. Esc (thoát game), F1 (menu), F2 / F4 (lưu / tải nhanh) là phím hệ thống, không đổi được.", 19));

        var cols = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var left = Group("TAY TRÁI", LeftHand);
        var right = Group("TAY PHẢI", RightHand);
        Grid.SetColumn(right, 2);
        cols.Children.Add(left);
        cols.Children.Add(right);
        root.Children.Add(cols);

        _status = new TextBlock { FontSize = 19, Margin = new Thickness(4, 16, 0, 0), Foreground = Dialogs.Res("Accent"), Text = " " };
        root.Children.Add(_status);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var reset = Dialogs.Button("↺  Khôi phục mặc định", (_, _) => ResetDefaults());
        DockPanel.SetDock(reset, Dock.Left);
        buttons.Children.Add(reset);
        var right2 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right2.Children.Add(Dialogs.Button("Lưu", async (_, _) => await Save(), primary: true));
        right2.Children.Add(Dialogs.Button("Hủy", (_, _) => Close()));
        buttons.Children.Add(right2);
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        // Bắt phím trước khi nút / ô khác xử lý (Enter, Space...)
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Dialogs.AttachGamepad(this);
        Refresh();
    }

    private Border Group(string title, string[] ids)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15, FontFamily = Dialogs.TechFont,
            Foreground = Dialogs.Res("FgDim"), Margin = new Thickness(4, 0, 0, 8),
        });
        foreach (var id in ids)
        {
            var info = KeyboardLayout.Buttons.First(b => b.Id == id);
            var row = new DockPanel { Margin = new Thickness(0, 4) };
            var cap = new Button { MinWidth = 150, Margin = new Thickness(0), FontFamily = Dialogs.TechFont, FontSize = 22, Tag = id };
            cap.Classes.Add("big");
            cap.Click += (_, _) => Listen(id);
            _caps[id] = cap;
            DockPanel.SetDock(cap, Dock.Right);
            row.Children.Add(cap);
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = info.Label, FontSize = 21, FontWeight = FontWeight.SemiBold });
            label.Children.Add(new TextBlock { Text = info.Hint, FontSize = 15, Foreground = Dialogs.Res("FgDim") });
            row.Children.Add(label);
            panel.Children.Add(row);
        }
        return new Border
        {
            Child = panel, Padding = new Thickness(18, 14), CornerRadius = new CornerRadius(16),
            Background = Dialogs.Res("Panel"), BorderBrush = Dialogs.Res("Line"), BorderThickness = new Thickness(1.5),
        };
    }

    private void Refresh()
    {
        foreach (var (id, cap) in _caps)
        {
            bool listening = id == _listening;
            cap.Content = listening ? "Bấm phím…" : KeyboardLayout.DisplayName(_map[id]);
            cap.Background = listening ? Dialogs.Res("Play") : new SolidColorBrush(Color.FromArgb(0x66, 0x1A, 0x2A, 0x48));
            cap.Foreground = listening ? Dialogs.Res("AccentFg") : Dialogs.Res("Fg");
        }
    }

    private void Listen(string id)
    {
        _listening = id;
        _status.Text = $"Nhấn phím cho \"{Label(id)}\"  (Esc để hủy)";
        Refresh();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_listening == null)
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            return;
        }
        e.Handled = true;
        var id = _listening;
        _listening = null;

        if (e.Key == Key.Escape) { _status.Text = "Đã hủy."; Refresh(); return; }
        var ra = ToRetroArch(e.Key);
        if (ra == null || KeyboardLayout.Reserved.Contains(ra))
        {
            _status.Text = $"Phím {e.Key} không dùng được, hãy chọn phím khác.";
            Refresh();
            return;
        }

        // Phím đang dùng cho nút khác → đổi chéo
        var other = _map.FirstOrDefault(kv => kv.Value == ra && kv.Key != id).Key;
        if (other != null)
        {
            _map[other] = _map[id];
            _status.Text = $"Đã đổi phím giữa \"{Label(id)}\" và \"{Label(other)}\".";
        }
        else _status.Text = $"\"{Label(id)}\" = {KeyboardLayout.DisplayName(ra)}";
        _map[id] = ra;
        Refresh();
    }

    private static string Label(string id) => KeyboardLayout.Buttons.First(b => b.Id == id).Label;

    private void ResetDefaults()
    {
        foreach (var (k, v) in KeyboardLayout.Defaults()) _map[k] = v;
        _listening = null;
        _status.Text = "Đã khôi phục phím mặc định (bấm Lưu để áp dụng).";
        Refresh();
    }

    private async Task Save()
    {
        try
        {
            App.Settings.KeyBindings = new Dictionary<string, string>(_map);
            App.Settings.Save(App.Paths.SettingsJson);
            RetroArchConfig.Write(App.Paths, App.Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Lưu phím lỗi", ex);
            await Dialogs.Info(this, "Không lưu được", ex.Message);
            return;
        }
        Close();
    }

    /// <summary>Đổi phím Avalonia sang tên phím trong retroarch.cfg.</summary>
    public static string? ToRetroArch(Key k) => k switch
    {
        >= Key.A and <= Key.Z => k.ToString().ToLowerInvariant(),
        >= Key.D0 and <= Key.D9 => "num" + (k - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => "keypad" + (k - Key.NumPad0),
        >= Key.F2 and <= Key.F12 => k.ToString().ToLowerInvariant(),
        Key.Enter => "enter",
        Key.Space => "space",
        Key.Tab => "tab",
        Key.Back => "backspace",
        Key.LeftShift => "shift",
        Key.RightShift => "rshift",
        Key.LeftCtrl => "ctrl",
        Key.RightCtrl => "rctrl",
        Key.LeftAlt => "alt",
        Key.RightAlt => "ralt",
        Key.Up => "up",
        Key.Down => "down",
        Key.Left => "left",
        Key.Right => "right",
        Key.Home => "home",
        Key.End => "end",
        Key.PageUp => "pageup",
        Key.PageDown => "pagedown",
        Key.Insert => "insert",
        Key.Delete => "del",
        Key.OemComma => "comma",
        Key.OemPeriod => "period",
        Key.OemMinus => "minus",
        Key.OemPlus => "equals",
        Key.OemQuestion => "slash",
        Key.OemSemicolon => "semicolon",
        Key.OemQuotes => "quote",
        Key.OemOpenBrackets => "leftbracket",
        Key.OemCloseBrackets => "rightbracket",
        Key.OemPipe => "backslash",
        Key.OemTilde => "tilde",
        Key.Add => "add",
        Key.Subtract => "subtract",
        Key.Multiply => "multiply",
        Key.Divide => "divide",
        _ => null,
    };
}
