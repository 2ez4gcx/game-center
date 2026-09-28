using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

/// <summary>Cài đặt: một trang, không menu lồng nhau.</summary>
public sealed class SettingsWindow : Window
{
    private readonly CheckBox _fullscreen, _launcherFs, _discGuide, _useDuck, _resume;
    private readonly ComboBox _renderer;
    private readonly TextBox _duckPath;
    private readonly TextBlock _dataDir;
    private string _newDataDir = App.Paths.DataDir;

    private static readonly (string Tag, string Label)[] Renderers =
    {
        ("auto", "Tự động (khuyên dùng)"),
        ("vulkan", "Vulkan"),
        ("software", "Phần mềm (máy yếu / lỗi hình)"),
    };

    public SettingsWindow()
    {
        Title = "Cài đặt";
        Width = 860;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dialogs.AttachGamepad(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var s = App.Settings;
        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading("Cài đặt"));

        // --- Màn hình
        root.Children.Add(Dialogs.Section("Màn hình"));
        _fullscreen = new CheckBox { Content = "Chơi game toàn màn hình", IsChecked = s.Fullscreen };
        _launcherFs = new CheckBox { Content = "Game Center mở toàn màn hình", IsChecked = s.LauncherFullscreen };
        _discGuide = new CheckBox { Content = "Hiện hướng dẫn đổi đĩa với game nhiều đĩa", IsChecked = s.ShowDiscChangeGuide };
        root.Children.Add(_fullscreen);
        root.Children.Add(_launcherFs);
        root.Children.Add(_discGuide);

        // --- Save game
        root.Children.Add(Dialogs.Section("Lưu game"));
        _resume = new CheckBox { Content = "Khi thoát game, hỏi có muốn lưu lại chỗ đang chơi không", IsChecked = s.AskSaveStateOnExit };
        root.Children.Add(_resume);
        root.Children.Add(Dialogs.Para("Save trong game (lưu vào memory card, lưu trong băng) luôn được giữ tự động, không cần làm gì thêm. Lưu chỗ đang chơi là tính năng thêm của giả lập, giúp chơi tiếp ở đúng khoảnh khắc đã thoát.", 18));

        // --- Điều khiển
        root.Children.Add(Dialogs.Section("Điều khiển"));
        root.Children.Add(Dialogs.Para("Tay cầm Xbox, PlayStation và tay cầm tương thích dùng được ngay. Bàn phím dùng song song được với tay cầm. Cấu hình một lần là dùng cho mọi hệ máy.", 20));
        root.Children.Add(Dialogs.Row(
            Dialogs.Button("🎮  Cấu hình tay cầm", async (_, _) => await OpenRetroArchMenu(this)),
            Dialogs.Button("⌨  Thiết lập phím", async (_, _) => await new KeyBindingWindow().ShowDialog(this))));

        // --- Dữ liệu
        root.Children.Add(Dialogs.Section("Dữ liệu"));
        _dataDir = Dialogs.Para("Thư mục dữ liệu: " + App.Paths.DataDir, 20);
        root.Children.Add(_dataDir);
        root.Children.Add(Dialogs.Row(
            Dialogs.Button("📂  Mở thư mục save", (_, _) => MainWindow.OpenFolder(App.Paths.SavesDir)),
            Dialogs.Button("💾  Sao lưu dữ liệu", async (_, _) => await Backup()),
            Dialogs.Button("🧩  Mở thư mục BIOS", (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir)),
            Dialogs.Button("Đổi thư mục dữ liệu…", async (_, _) => await ChangeDataDir())));

        // --- Nâng cao
        root.Children.Add(Dialogs.Section("Nâng cao"));
        root.Children.Add(Dialogs.Para("Cách vẽ hình game PS1:", 20));
        _renderer = new ComboBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = Renderers.Select(r => r.Label).ToList() };
        _renderer.SelectedIndex = Math.Max(0, Array.FindIndex(Renderers, r => r.Tag == s.Ps1Renderer));
        root.Children.Add(_renderer);

        _useDuck = new CheckBox { Content = "Chơi PS1 bằng DuckStation mà tôi đã tự cài", IsChecked = s.UseUserDuckStation, Margin = new Thickness(0, 16, 0, 4) };
        root.Children.Add(_useDuck);
        root.Children.Add(Dialogs.Para("Game Center không kèm DuckStation. Bạn tự tải từ trang chính thức, rồi chọn file chạy DuckStation ở đây.", 18));
        _duckPath = new TextBox { Text = s.DuckStationPath ?? "", FontSize = 18 };
        var browse = Dialogs.Button("Chọn…", async (_, _) => await BrowseDuck());
        var duckRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        duckRow.Children.Add(browse);
        duckRow.Children.Add(_duckPath);
        root.Children.Add(duckRow);

        // --- Nút
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(Dialogs.Button("Lưu", async (_, _) => await Save(), primary: true));
        buttons.Children.Add(Dialogs.Button("Hủy", (_, _) => Close()));
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += (_, _) => _fullscreen.Focus(NavigationMethod.Directional);
    }

    public static async Task OpenRetroArchMenu(Window owner)
    {
        var psi = new EmulatorLauncher(App.Paths, App.Catalog, App.Settings).BuildMenuStartInfo();
        if (psi == null)
        {
            await Dialogs.Info(owner, "Thiếu RetroArch", "Không tìm thấy RetroArch trong thư mục cài đặt. Hãy cài lại Game Center.");
            return;
        }
        await Dialogs.Info(owner, "Cấu hình tay cầm",
            "Cửa sổ giả lập sẽ mở ra.\n\nVào: Cài đặt → Đầu vào → Điều khiển cổng 1 → Gán tất cả nút.\nBấm lần lượt từng nút trên tay cầm theo hướng dẫn.\n\nXong thì đóng cửa sổ giả lập (Esc).");
        try
        {
            RetroArchConfig.Write(App.Paths, App.Settings);
            Process.Start(psi);
        }
        catch (Exception ex) { Log.Error("Không mở được RetroArch", ex); }
    }

    private async Task Backup()
    {
        try
        {
            var zip = await Task.Run(() => BackupService.CreateBackup(App.Paths));
            await Dialogs.Info(this, "Đã sao lưu", $"Đã sao lưu save game vào:\n{zip}");
            MainWindow.OpenFolder(App.Paths.BackupsDir);
        }
        catch (Exception ex)
        {
            Log.Error("Sao lưu lỗi", ex);
            await Dialogs.Info(this, "Không sao lưu được", ex.Message);
        }
    }

    private async Task ChangeDataDir()
    {
        var start = await StorageProvider.TryGetFolderFromPathAsync(App.Paths.DataDir);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Chọn thư mục dữ liệu Game Center",
            SuggestedStartLocation = start,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        _newDataDir = path;
        _dataDir.Text = "Thư mục dữ liệu (mới): " + _newDataDir;
    }

    private async Task BrowseDuck()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Chọn file chạy DuckStation", AllowMultiple = false });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        _duckPath.Text = path;
        _useDuck.IsChecked = true;
    }

    private async Task Save()
    {
        var s = App.Settings;
        s.Fullscreen = _fullscreen.IsChecked == true;
        s.LauncherFullscreen = _launcherFs.IsChecked == true;
        s.ShowDiscChangeGuide = _discGuide.IsChecked == true;
        s.AskSaveStateOnExit = _resume.IsChecked == true;
        s.Ps1Renderer = Renderers[Math.Max(0, _renderer.SelectedIndex)].Tag;
        s.UseUserDuckStation = _useDuck.IsChecked == true;
        s.DuckStationPath = string.IsNullOrWhiteSpace(_duckPath.Text) ? null : _duckPath.Text.Trim();

        if (s.UseUserDuckStation && (s.DuckStationPath == null || !File.Exists(s.DuckStationPath)))
        {
            await Dialogs.Info(this, "Chưa chọn DuckStation", "Hãy bấm \"Chọn…\" và chọn file chạy DuckStation, hoặc bỏ đánh dấu ô DuckStation.");
            return;
        }

        try
        {
            s.Save(App.Paths.SettingsJson);
            RetroArchConfig.Write(App.Paths, s);

            if (!string.Equals(Path.GetFullPath(_newDataDir), App.Paths.DataDir, StringComparison.OrdinalIgnoreCase))
            {
                AppPaths.SaveDataDirLocation(_newDataDir);
                // Mang theo cài đặt hiện tại sang thư mục mới (nếu chưa có)
                var newPaths = new AppPaths(App.Paths.AppDir, _newDataDir);
                newPaths.EnsureDataFolders();
                if (!File.Exists(newPaths.SettingsJson)) s.Save(newPaths.SettingsJson);
                await Dialogs.Info(this, "Đã đổi thư mục dữ liệu",
                    "Hãy đóng và mở lại Game Center để dùng thư mục mới.\nDữ liệu ở thư mục cũ vẫn được giữ nguyên.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lưu cài đặt lỗi", ex);
            await Dialogs.Info(this, "Không lưu được", ex.Message);
            return;
        }
        Close();
    }
}
