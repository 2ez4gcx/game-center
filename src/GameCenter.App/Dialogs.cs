using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GameCenter.App;

/// <summary>
/// Hộp thoại chữ lớn, dùng được bằng chuột, bàn phím và tay cầm
/// (A = chọn, B = đóng, mũi tên = di chuyển).
/// </summary>
public static class Dialogs
{
    public static Window CreateWindow(Window? owner, string title, double width = 720)
    {
        var w = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height * 0.95,
            WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = (Brush)Application.Current.Resources["Bg"],
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = owner == null || !owner.IsVisible,
            FontSize = 20,
        };
        if (owner != null && owner.IsVisible) w.Owner = owner;
        AttachGamepad(w);
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { w.DialogResult ??= false; w.Close(); } };
        return w;
    }

    public static void AttachGamepad(Window w)
    {
        var pad = new Gamepad { IsEnabled = () => w.IsActive };
        pad.Pressed += b =>
        {
            var focused = Keyboard.FocusedElement as UIElement;
            switch (b)
            {
                case PadButton.A:
                    if (focused is ButtonBase btn) btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    else if (focused is ToggleButton tb) tb.IsChecked = !tb.IsChecked;
                    break;
                case PadButton.B:
                    w.Close();
                    break;
                case PadButton.Up:
                case PadButton.Left:
                    focused?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                    break;
                case PadButton.Down:
                case PadButton.Right:
                    focused?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;
            }
        };
        w.Closed += (_, _) => pad.Dispose();
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16),
        Foreground = (Brush)Application.Current.Resources["Accent"], TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Para(string text, double size = 22) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.4,
    };

    public static Button Button(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources[primary ? "PlayButton" : "BigButton"],
        };
        b.Click += onClick;
        return b;
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

    public static void Info(Window? owner, string title, string message)
    {
        var w = CreateWindow(owner, title);
        var root = Shell(w, title, message, out var buttons);
        var ok = Button("Đã hiểu", (_, _) => w.Close(), primary: true);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        w.Loaded += (_, _) => ok.Focus();
        w.ShowDialog();
    }

    public static bool Confirm(Window? owner, string title, string message, string yes = "Đồng ý", string no = "Không")
    {
        var w = CreateWindow(owner, title);
        var root = Shell(w, title, message, out var buttons);
        var yesBtn = Button(yes, (_, _) => { w.DialogResult = true; }, primary: true);
        buttons.Children.Add(yesBtn);
        buttons.Children.Add(Button(no, (_, _) => { w.DialogResult = false; }));
        root.Children.Add(buttons);
        w.Loaded += (_, _) => yesBtn.Focus();
        return w.ShowDialog() == true;
    }

    public static string? AskText(Window? owner, string title, string message, string initial)
    {
        var w = CreateWindow(owner, title);
        var root = Shell(w, title, message, out var buttons);
        var box = new TextBox { Text = initial };
        root.Children.Add(box);
        buttons.Children.Add(Button("Lưu", (_, _) => { w.DialogResult = true; }, primary: true));
        buttons.Children.Add(Button("Hủy", (_, _) => { w.DialogResult = false; }));
        root.Children.Add(buttons);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) w.DialogResult = true; };
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text : null;
    }

    /// <summary>Chọn một mục trong danh sách nút lớn. Trả về chỉ số, hoặc -1 nếu hủy.</summary>
    public static int Choose(Window? owner, string title, string? message, IReadOnlyList<string> options)
    {
        var w = CreateWindow(owner, title, 600);
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
        w.Loaded += (_, _) => first?.Focus();
        w.ShowDialog();
        return result;
    }
}
