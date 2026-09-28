using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GameCenter.Core.Util;

namespace GameCenter.App;

/// <summary>Trợ giúp: thoát game, đổi đĩa, tay cầm, BIOS, giấy phép, giới thiệu.</summary>
public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "Trợ giúp";
        Width = 900;
        Height = Math.Min(900, SystemParameters.WorkArea.Height * 0.95);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        Dialogs.AttachGamepad(this);

        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading("Trợ giúp"));

        Add(root, "Thoát game",
            "• Tay cầm: giữ cùng lúc nút START và SELECT (trên tay cầm Xbox là nút ☰ và nút ⧉).\n" +
            "• Bàn phím: bấm phím Esc.\n" +
            "Game Center sẽ tự hiện lại sau khi thoát game. Game tự lưu khi bạn lưu trong game như máy thật.");

        Add(root, "Chơi game",
            "1. Bấm \"Mở thư mục game\" và chép game vào đó — để chung một chỗ cũng được, Game Center tự nhận ra hệ máy.\n" +
            "2. Game mới tự hiện ra sau vài giây (hoặc bấm \"Quét game mới\").\n" +
            "3. Chọn game và bấm \"Chơi\" (hoặc nút A trên tay cầm).");

        Add(root, "Điều khiển Game Center bằng tay cầm",
            "• Lên / Xuống: chọn game\n• A: chơi\n• LB / RB: đổi mục (Tất cả, PS1, SNES...)\n" +
            "• Y: thêm/bỏ yêu thích\n• X: thêm tùy chọn (đổi tên, mở thư mục...)\n• START: cài đặt   • SELECT: trợ giúp\n• B: đóng cửa sổ");

        Add(root, "Đổi đĩa (game PS1 nhiều đĩa)", DiscGuideText);

        Add(root, "BIOS",
            "Game PS1 chơi được ngay mà không cần BIOS.\n" +
            "Nếu muốn tương thích tốt hơn, bạn có thể thêm file BIOS mà bạn tự sao lưu hợp pháp từ máy của mình vào thư mục BIOS. " +
            "Game Center không cung cấp và không tải BIOS.");
        root.Children.Add(Dialogs.Button("🧩  Mở thư mục BIOS", (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir)));

        Add(root, "Khi gặp lỗi",
            "Game Center ghi lại lỗi trong thư mục Logs. Khi cần hỗ trợ, hãy gửi file gamecenter.log trong thư mục đó.");
        root.Children.Add(Dialogs.Button("📄  Mở thư mục Logs", (_, _) => MainWindow.OpenFolder(App.Paths.LogsDir)));

        // --- Giấy phép (mục 17.2: ghi rõ thành phần nguồn mở)
        Add(root, "Giấy phép",
            "Game Center là phần mềm miễn phí: không bán, không quảng cáo, không có tính năng trả phí.\n" +
            "Game Center dùng các thành phần nguồn mở sau, mỗi thành phần giữ giấy phép riêng:\n" +
            "• RetroArch — GPLv3\n• Beetle PSX HW (PS1) — GPLv2\n• Gambatte (GB/GBC) — GPLv2\n• mGBA (GBA) — MPL 2.0\n" +
            "• Snes9x (SNES) — giấy phép phi thương mại riêng\n• Genesis Plus GX (Mega Drive) — giấy phép phi thương mại riêng\n" +
            "• FCEUmm (NES) — GPLv2\n\n" +
            "Game Center không phải sản phẩm chính thức của Sony, Nintendo hay Sega. Bộ cài không kèm game hay BIOS thương mại.");
        var licenseList = new WrapPanel();
        if (Directory.Exists(App.Paths.LicensesDir))
        {
            foreach (var f in Directory.EnumerateFiles(App.Paths.LicensesDir, "*.txt", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var file = f;
                licenseList.Children.Add(Dialogs.Button(Path.GetFileNameWithoutExtension(file), (_, _) => OpenText(file)));
            }
        }
        root.Children.Add(licenseList);

        var close = Dialogs.Button("Đóng", (_, _) => Close(), primary: true);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 24, 0, 0);
        root.Children.Add(close);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => close.Focus();
    }

    private static void Add(Panel root, string title, string text)
    {
        root.Children.Add(new TextBlock
        {
            Text = title, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 8),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Accent"],
        });
        root.Children.Add(Dialogs.Para(text, 21));
    }

    private static void OpenText(string file)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { file }, UseShellExecute = false }); }
        catch (Exception ex) { Log.Error("Không mở được file giấy phép", ex); }
    }

    public const string DiscGuideText =
        "Khi game báo \"hãy cho đĩa 2 vào\":\n" +
        "1. Mở menu: giữ cùng lúc 2 cần analog (bấm lún L3 + R3), hoặc bấm phím F1.\n" +
        "2. Chọn \"Điều khiển đĩa\" (Disc Control).\n" +
        "3. Chọn \"Mở khay đĩa\" → chọn đĩa tiếp theo → \"Đóng khay đĩa\".\n" +
        "4. Bấm lại L3 + R3 (hoặc F1) để quay về game.\n" +
        "Tiến trình chơi được giữ nguyên.";

    /// <summary>Hướng dẫn đổi đĩa ngắn, hiện khi chạy game nhiều đĩa (mục 8.3).</summary>
    public static void ShowDiscGuide(Window owner) =>
        Dialogs.Info(owner, "Game này có nhiều đĩa", DiscGuideText);
}
