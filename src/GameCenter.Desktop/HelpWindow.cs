using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using GameCenter.Core.Config;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

/// <summary>Trợ giúp: thoát game, đổi đĩa, tay cầm, BIOS, giấy phép, giới thiệu.</summary>
public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "Trợ giúp";
        Width = 900;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dialogs.AttachGamepad(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading("Trợ giúp"));

        Add(root, "Thoát game",
            "Để tránh lỡ tay, phải bấm 2 lần mới thoát:\n" +
            "• Tay cầm: bấm cùng lúc START và SELECT (trên tay cầm Xbox là nút ☰ và nút ⧉), màn hình hiện \"Nhấn lại để thoát...\", bấm thêm một lần nữa.\n" +
            "• Bàn phím: bấm Esc 2 lần liên tiếp.\n" +
            "Lỡ bấm một lần thì cứ chơi tiếp, vài giây sau lời nhắc tự tắt.\n" +
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
        root.Children.Add(Dialogs.Row(Dialogs.Button("🧩  Mở thư mục BIOS", (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir))));

        Add(root, "Khi gặp lỗi",
            "Game Center ghi lại lỗi trong thư mục Logs. Khi cần hỗ trợ, hãy gửi file gamecenter.log trong thư mục đó.");
        root.Children.Add(Dialogs.Row(Dialogs.Button("📄  Mở thư mục Logs", (_, _) => MainWindow.OpenFolder(App.Paths.LogsDir))));

        Add(root, "Giới thiệu",
            $"Game Center phiên bản {AppInfo.Version}\nTác giả: {AppInfo.Author}  ·  {AppInfo.Contact}");

        // --- Giấy phép (mục 17.2: ghi rõ thành phần nguồn mở)
        Add(root, "Giấy phép",
            "Game Center là phần mềm miễn phí: không bán, không quảng cáo, không có tính năng trả phí.\n" +
            "Game Center dùng các thành phần nguồn mở sau, mỗi thành phần giữ giấy phép riêng:\n" +
            "• RetroArch — GPLv3\n• Beetle PSX HW (PS1) — GPLv2\n• Gambatte (GB/GBC) — GPLv2\n• mGBA (GBA) — MPL 2.0\n" +
            "• Snes9x (SNES) — giấy phép phi thương mại riêng\n• Genesis Plus GX (Mega Drive) — giấy phép phi thương mại riêng\n" +
            "• FCEUmm (NES) — GPLv2\n• SDL2 (đọc tay cầm) — zlib\n\n" +
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
        Opened += (_, _) => close.Focus(NavigationMethod.Directional);
    }

    private static void Add(Panel root, string title, string text)
    {
        root.Children.Add(Dialogs.Section(title));
        root.Children.Add(Dialogs.Para(text, 21));
    }

    private static void OpenText(string file)
    {
        try { OsPlatform.OpenFile(file); }
        catch (Exception ex) { Log.Error("Không mở được file giấy phép", ex); }
    }

    public const string DiscGuideText =
        "Khi game báo \"hãy cho đĩa 2 vào\":\n" +
        "1. Mở menu: giữ cùng lúc 2 cần analog (bấm lún L3 + R3), hoặc bấm phím F1.\n" +
        "2. Chọn \"Điều khiển đĩa\".\n" +
        "3. Chọn \"Đẩy đĩa ra\" → ở \"Chỉ số đĩa\" chọn đĩa tiếp theo → chọn \"Thêm đĩa\".\n" +
        "4. Bấm lại L3 + R3 (hoặc F1) để quay về game.\n" +
        "Tiến trình chơi được giữ nguyên.";

    /// <summary>Hướng dẫn đổi đĩa ngắn, hiện khi chạy game nhiều đĩa (mục 8.3).</summary>
    public static Task ShowDiscGuide(Window owner) =>
        Dialogs.Info(owner, "Game này có nhiều đĩa", DiscGuideText);
}
