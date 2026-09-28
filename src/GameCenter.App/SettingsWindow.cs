using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Util;

namespace GameCenter.App;

/// <summary>Cài đặt: một trang, không menu lồng nhau.</summary>
public sealed class SettingsWindow : Window
{
    private readonly CheckBox _fullscreen, _launcherFs, _discGuide, _useDuck, _resume;
    private readonly ComboBox _renderer;
    private readonly TextBox _duckPath;
    private readonly TextBlock _dataDir;
    private string _newDataDir = App.Paths.DataDir;

    public SettingsWindow()
    {
        Title = "Cài đặt";
        Width = 860;
        Height = Math.Min(900, SystemParameters.WorkArea.Height * 0.95);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Fg"];
        Dialogs.AttachGamepad(this);

        var s = App.Settings;
        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading("Cài đặt"));

        // --- Hiển thị
        root.Children.Add(Section("Màn hình"));
        _fullscreen = new CheckBox { Content = "Chơi game toàn màn hình", IsChecked = s.Fullscreen };
        _launcherFs = new CheckBox { Content = "Game Center mở toàn màn hình", IsChecked = s.LauncherFullscreen };
        _discGuide = new CheckBox { Content = "Hiện hướng dẫn đổi đĩa với game nhiều đĩa", IsChecked = s.ShowDiscChangeGuide };
        root.Children.Add(_fullscreen);
        root.Children.Add(_launcherFs);
        root.Children.Add(_discGuide);

        // --- Save game
        root.Children.Add(Section("Lưu game"));
        _resume = new CheckBox { Content = "Khi thoát game, hỏi có muốn lưu lại chỗ đang chơi không", IsChecked = s.AskSaveStateOnExit };
        root.Children.Add(_resume);
        root.Children.Add(Dialogs.Para("Save trong game (lưu vào memory card, lưu trong băng) luôn được giữ tự động, không cần làm gì thêm. Lưu chỗ đang chơi là tính năng thêm của giả lập, giúp chơi tiếp ở đúng khoảnh khắc đã thoát.", 18));

        // --- Tay cầm
        root.Children.Add(Section("Điều khiển"));
        root.Children.Add(Dialogs.Para("Tay cầm Xbox và tay cầm tương thích dùng được ngay. Bàn phím dùng song song được với tay cầm. Cấu hình một lần là dùng cho mọi hệ máy.", 20));
        root.Children.Add(Row(
            Dialogs.Button("🎮  Cấu hình tay cầm", (_, _) => OpenRetroArchMenu(this)),
            Dialogs.Button("⌨  Thiết lập phím", (_, _) => new KeyBindingWindow { Owner = this }.ShowDialog())));

        // --- Dữ liệu
        root.Children.Add(Section("Dữ liệu"));
        _dataDir = Dialogs.Para("Thư mục dữ liệu: " + App.Paths.DataDir, 20);
        root.Children.Add(_dataDir);
        root.Children.Add(Row(
            Dialogs.Button("📂  Mở thư mục save", (_, _) => MainWindow.OpenFolder(App.Paths.SavesDir)),
            Dialogs.Button("💾  Sao lưu dữ liệu", (_, _) => Backup()),
            Dialogs.Button("🧩  Mở thư mục BIOS", (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir)),
            Dialogs.Button("Đổi thư mục dữ liệu…", (_, _) => ChangeDataDir())));

        // --- Nâng cao
        root.Children.Add(Section("Nâng cao"));
        root.Children.Add(Dialogs.Para("Cách vẽ hình game PS1:", 20));
        _renderer = new ComboBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        _renderer.Items.Add(new ComboBoxItem { Content = "Tự động (khuyên dùng)", Tag = "auto" });
        _renderer.Items.Add(new ComboBoxItem { Content = "Vulkan", Tag = "vulkan" });
        _renderer.Items.Add(new ComboBoxItem { Content = "Phần mềm (máy yếu / lỗi hình)", Tag = "software" });
        _renderer.SelectedItem = _renderer.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Ps1Renderer) ?? _renderer.Items[0];
        root.Children.Add(_renderer);

        _useDuck = new CheckBox { Content = "Chơi PS1 bằng DuckStation mà tôi đã tự cài", IsChecked = s.UseUserDuckStation, Margin = new Thickness(0, 16, 0, 4) };
        root.Children.Add(_useDuck);
        root.Children.Add(Dialogs.Para("Game Center không kèm DuckStation. Bạn tự tải từ trang chính thức, rồi chọn file DuckStation.exe ở đây.", 18));
        _duckPath = new TextBox { Text = s.DuckStationPath ?? "", FontSize = 18 };
        var browse = Dialogs.Button("Chọn…", (_, _) => BrowseDuck());
        var duckRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        duckRow.Children.Add(browse);
        duckRow.Children.Add(_duckPath);
        root.Children.Add(duckRow);

        // --- Nút
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var save = Dialogs.Button("Lưu", (_, _) => Save(), primary: true);
        buttons.Children.Add(save);
        buttons.Children.Add(Dialogs.Button("Hủy", (_, _) => Close()));
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => _fullscreen.Focus();
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 8),
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Accent"],
    };

    private static WrapPanel Row(params UIElement[] items)
    {
        var p = new WrapPanel();
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    public static void OpenRetroArchMenu(Window owner)
    {
        var psi = new EmulatorLauncher(App.Paths, App.Catalog, App.Settings).BuildMenuStartInfo();
        if (psi == null)
        {
            Dialogs.Info(owner, "Thiếu RetroArch", "Không tìm thấy RetroArch trong thư mục cài đặt. Hãy cài lại Game Center.");
            return;
        }
        Dialogs.Info(owner, "Cấu hình tay cầm",
            "Cửa sổ giả lập sẽ mở ra.\n\nVào: Cài đặt → Đầu vào → Điều khiển cổng 1 → Gán tất cả nút.\nBấm lần lượt từng nút trên tay cầm theo hướng dẫn.\n\nXong thì đóng cửa sổ giả lập (Esc).");
        try { Process.Start(psi); }
        catch (Exception ex) { Log.Error("Không mở được RetroArch", ex); }
    }

    private void Backup()
    {
        try
        {
            var zip = BackupService.CreateBackup(App.Paths);
            Dialogs.Info(this, "Đã sao lưu", $"Đã sao lưu save game vào:\n{zip}");
            MainWindow.OpenFolder(App.Paths.BackupsDir);
        }
        catch (Exception ex)
        {
            Log.Error("Sao lưu lỗi", ex);
            Dialogs.Info(this, "Không sao lưu được", ex.Message);
        }
    }

    private void ChangeDataDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục dữ liệu Game Center", InitialDirectory = App.Paths.DataDir };
        if (dlg.ShowDialog(this) != true) return;
        _newDataDir = dlg.FolderName;
        _dataDir.Text = "Thư mục dữ liệu (mới): " + _newDataDir;
    }

    private void BrowseDuck()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "DuckStation (*.exe)|*.exe", Title = "Chọn DuckStation.exe" };
        if (dlg.ShowDialog(this) == true) { _duckPath.Text = dlg.FileName; _useDuck.IsChecked = true; }
    }

    private void Save()
    {
        var s = App.Settings;
        s.Fullscreen = _fullscreen.IsChecked == true;
        s.LauncherFullscreen = _launcherFs.IsChecked == true;
        s.ShowDiscChangeGuide = _discGuide.IsChecked == true;
        s.AskSaveStateOnExit = _resume.IsChecked == true;
        s.Ps1Renderer = (string)((ComboBoxItem)_renderer.SelectedItem).Tag;
        s.UseUserDuckStation = _useDuck.IsChecked == true;
        s.DuckStationPath = string.IsNullOrWhiteSpace(_duckPath.Text) ? null : _duckPath.Text.Trim();

        if (s.UseUserDuckStation && (s.DuckStationPath == null || !File.Exists(s.DuckStationPath)))
        {
            Dialogs.Info(this, "Chưa chọn DuckStation", "Hãy bấm \"Chọn…\" và chọn file DuckStation.exe, hoặc bỏ đánh dấu ô DuckStation.");
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
                Dialogs.Info(this, "Đã đổi thư mục dữ liệu",
                    "Hãy đóng và mở lại Game Center để dùng thư mục mới.\nDữ liệu ở thư mục cũ vẫn được giữ nguyên.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lưu cài đặt lỗi", ex);
            Dialogs.Info(this, "Không lưu được", ex.Message);
            return;
        }
        Close();
    }
}
