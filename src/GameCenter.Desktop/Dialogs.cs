using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GameCenter.Desktop;

/// <summary>
/// Hộp thoại chữ lớn, dùng được bằng chuột, bàn phím và tay cầm
/// (A = chọn, B = đóng, mũi tên = di chuyển).
/// </summary>
public static class Dialogs
{
    public static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
    public static FontFamily TechFont => (FontFamily)Application.Current!.Resources["TechFont"]!;

    public static Window CreateWindow(string title, double width = 720, bool cancellable = true)
    {
        var w = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 900,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AttachGamepad(w, cancellable);
        // Không cho đóng bằng Esc khi bắt buộc chọn (ví dụ ngay sau khi bấm Esc thoát game)
        w.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (cancellable) w.Close();
        };
        return w;
    }

    public static void AttachGamepad(Window w, bool cancellable = true)
    {
        void OnPad(PadButton b)
        {
            if (!w.IsActive) return;
            var focused = w.FocusManager?.GetFocusedElement();
            switch (b)
            {
                case PadButton.A:
                    if (focused is Avalonia.Controls.Button btn) btn.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                    else if (focused is ToggleButton tb) tb.IsChecked = !tb.IsChecked;
                    break;
                case PadButton.B:
                    if (cancellable) w.Close();
                    break;
                case PadButton.Up or PadButton.Left:
                    Move(focused, NavigationDirection.Previous);
                    break;
                case PadButton.Down or PadButton.Right:
                    Move(focused, NavigationDirection.Next);
                    break;
            }
        }
        Gamepad.Instance.Pressed += OnPad;
        w.Closed += (_, _) => Gamepad.Instance.Pressed -= OnPad;
    }

    private static void Move(IInputElement? from, NavigationDirection dir)
    {
        if (from == null) return;
        KeyboardNavigationHandler.GetNext(from, dir)?.Focus(NavigationMethod.Directional);
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 16),
        Foreground = Res("Accent"), TextWrapping = TextWrapping.Wrap, FontFamily = TechFont,
    };

    public static TextBlock Para(string text, double size = 22) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.4,
    };

    public static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 26, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 20, 0, 8),
        Foreground = Res("Accent"), FontFamily = TechFont,
    };

    public static Button Button(string text, EventHandler<RoutedEventArgs> onClick, bool primary = false)
    {
        var b = new Button { Content = text };
        b.Classes.Add("big");
        if (primary) b.Classes.Add("play");
        b.Click += onClick;
        return b;
    }

    public static WrapPanel Row(params Control[] items)
    {
        var p = new WrapPanel();
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    private static StackPanel Shell(Window w, string title, string? message, out WrapPanel buttons)
    {
        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Heading(title));
        if (message != null) root.Children.Add(Para(message));
        buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        w.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return root;
    }

    public static async Task Info(Window owner, string title, string message)
    {
        var w = CreateWindow(title);
        var root = Shell(w, title, message, out var buttons);
        var ok = Button("Đã hiểu", (_, _) => w.Close(), primary: true);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        w.Opened += (_, _) => ok.Focus(NavigationMethod.Directional);
        await w.ShowDialog(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string message,
        string yes = "Đồng ý", string no = "Không", bool cancellable = true)
    {
        var w = CreateWindow(title, cancellable: cancellable);
        var root = Shell(w, title, message, out var buttons);
        bool? result = null;
        var yesBtn = Button(yes, (_, _) => { result = true; w.Close(); }, primary: true);
        buttons.Children.Add(yesBtn);
        buttons.Children.Add(Button(no, (_, _) => { result = false; w.Close(); }));
        root.Children.Add(buttons);
        if (!cancellable) w.Closing += (_, e) => { if (result == null) e.Cancel = true; };
        w.Opened += (_, _) => yesBtn.Focus(NavigationMethod.Directional);
        await w.ShowDialog(owner);
        return result == true;
    }

    public static async Task<string?> AskText(Window owner, string title, string message, string initial)
    {
        var w = CreateWindow(title);
        var root = Shell(w, title, message, out var buttons);
        var box = new TextBox { Text = initial };
        root.Children.Add(box);
        bool ok = false;
        buttons.Children.Add(Button("Lưu", (_, _) => { ok = true; w.Close(); }, primary: true));
        buttons.Children.Add(Button("Hủy", (_, _) => w.Close()));
        root.Children.Add(buttons);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ok = true; w.Close(); } };
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await w.ShowDialog(owner);
        return ok ? box.Text : null;
    }

    /// <summary>Chọn một mục trong danh sách nút lớn. Trả về chỉ số, hoặc -1 nếu hủy.</summary>
    public static async Task<int> Choose(Window owner, string title, string? message, IReadOnlyList<string> options)
    {
        var w = CreateWindow(title, 600);
        var root = Shell(w, title, message, out var buttons);
        int result = -1;
        Button? first = null;
        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var b = Button(options[i], (_, _) => { result = idx; w.Close(); });
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            root.Children.Add(b);
            first ??= b;
        }
        buttons.Children.Add(Button("Hủy", (_, _) => w.Close()));
        root.Children.Add(buttons);
        w.Opened += (_, _) => first?.Focus(NavigationMethod.Directional);
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>Cửa sổ báo lỗi khởi động (khi chưa có cửa sổ chính).</summary>
    public static Window StartupError(string message)
    {
        var w = new Window { Title = "Game Center", Width = 720, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var root = Shell(w, "Game Center không khởi động được", message, out var buttons);
        buttons.Children.Add(Button("Đóng", (_, _) => w.Close(), primary: true));
        root.Children.Add(buttons);
        return w;
    }
}
