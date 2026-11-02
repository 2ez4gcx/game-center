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
using GameCenter.Core.Localization;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

/// <summary>Cài đặt: một trang, không menu lồng nhau.</summary>
public sealed class SettingsWindow : Window
{
    private readonly CheckBox _fullscreen, _launcherFs, _discGuide, _useDuck, _resume;
    private readonly ComboBox _renderer;
    private readonly TextBox _duckPath;
    private readonly TextBlock _dataDir;
    private readonly List<(string Code, RadioButton Button)> _languages = new();
    private string _newDataDir = App.Paths.DataDir;

    private static readonly (string Tag, string Key)[] Renderers =
    {
        ("auto", "settings.renderer_auto"),
        ("vulkan", "settings.renderer_vulkan"),
        ("software", "settings.renderer_software"),
    };

    /// <summary>Ngôn ngữ người dùng đã chọn và lưu (cửa sổ chính dùng để biết có cần tải lại giao diện không).</summary>
    public string? ChosenLanguage { get; private set; }

    public SettingsWindow()
    {
        Title = Loc.T("settings.title");
        Width = 860;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dialogs.AttachGamepad(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var s = App.Settings;
        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading(Loc.T("settings.title")));

        // --- Ngôn ngữ (đặt đầu tiên, tiêu đề viết cả 2 thứ tiếng để ai cũng tìm được)
        root.Children.Add(Dialogs.Section(Loc.T("settings.language")));
        var langRow = new WrapPanel();
        foreach (var (code, name) in Loc.Available)
        {
            var rb = new RadioButton { Content = name, GroupName = "language", IsChecked = code == Loc.Language, FontSize = 20, Margin = new Thickness(0, 4, 28, 4) };
            _languages.Add((code, rb));
            langRow.Children.Add(rb);
        }
        root.Children.Add(langRow);

        // --- Màn hình
        root.Children.Add(Dialogs.Section(Loc.T("settings.screen")));
        _fullscreen = new CheckBox { Content = Loc.T("settings.fullscreen"), IsChecked = s.Fullscreen };
        _launcherFs = new CheckBox { Content = Loc.T("settings.launcher_fullscreen"), IsChecked = s.LauncherFullscreen };
        _discGuide = new CheckBox { Content = Loc.T("settings.disc_guide"), IsChecked = s.ShowDiscChangeGuide };
        root.Children.Add(_fullscreen);
        root.Children.Add(_launcherFs);
        root.Children.Add(_discGuide);

        // --- Save game
        root.Children.Add(Dialogs.Section(Loc.T("settings.saves")));
        _resume = new CheckBox { Content = Loc.T("settings.ask_state"), IsChecked = s.AskSaveStateOnExit };
        root.Children.Add(_resume);
        root.Children.Add(Dialogs.Para(Loc.T("settings.saves_text"), 18));

        // --- Điều khiển
        root.Children.Add(Dialogs.Section(Loc.T("settings.controls")));
        root.Children.Add(Dialogs.Para(Loc.T("settings.controls_text"), 20));
        root.Children.Add(Dialogs.Row(
            Dialogs.Button(Loc.T("settings.configure_gamepad"), async (_, _) => await OpenRetroArchMenu(this)),
            Dialogs.Button(Loc.T("main.setup_keys"), async (_, _) => await new KeyBindingWindow().ShowDialog(this))));

        // --- Dữ liệu
        root.Children.Add(Dialogs.Section(Loc.T("settings.data")));
        _dataDir = Dialogs.Para(Loc.T("settings.data_dir", App.Paths.DataDir), 20);
        root.Children.Add(_dataDir);
        root.Children.Add(Dialogs.Row(
            Dialogs.Button(Loc.T("settings.open_saves"), (_, _) => MainWindow.OpenFolder(App.Paths.SavesDir)),
            Dialogs.Button(Loc.T("settings.backup"), async (_, _) => await Backup()),
            Dialogs.Button(Loc.T("settings.open_bios"), (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir)),
            Dialogs.Button(Loc.T("settings.change_data_dir"), async (_, _) => await ChangeDataDir())));

        // --- Nâng cao
        root.Children.Add(Dialogs.Section(Loc.T("settings.advanced")));
        root.Children.Add(Dialogs.Para(Loc.T("settings.ps1_renderer"), 20));
        _renderer = new ComboBox { Width = 460, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = Renderers.Select(r => Loc.T(r.Key)).ToList() };
        _renderer.SelectedIndex = Math.Max(0, Array.FindIndex(Renderers, r => r.Tag == s.Ps1Renderer));
        root.Children.Add(_renderer);

        _useDuck = new CheckBox { Content = Loc.T("settings.use_duck"), IsChecked = s.UseUserDuckStation, Margin = new Thickness(0, 16, 0, 4) };
        root.Children.Add(_useDuck);
        root.Children.Add(Dialogs.Para(Loc.T("settings.duck_text"), 18));
        _duckPath = new TextBox { Text = s.DuckStationPath ?? "", FontSize = 18 };
        var browse = Dialogs.Button(Loc.T("settings.browse"), async (_, _) => await BrowseDuck());
        var duckRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        duckRow.Children.Add(browse);
        duckRow.Children.Add(_duckPath);
        root.Children.Add(duckRow);

        // --- Nút
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(Dialogs.Button(Loc.T("dialog.save"), async (_, _) => await Save(), primary: true));
        buttons.Children.Add(Dialogs.Button(Loc.T("dialog.cancel"), (_, _) => Close()));
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += (_, _) => (_languages.FirstOrDefault(l => l.Button.IsChecked == true).Button ?? (Control)_fullscreen).Focus(NavigationMethod.Directional);
    }

    public static async Task OpenRetroArchMenu(Window owner)
    {
        var psi = new EmulatorLauncher(App.Paths, App.Catalog, App.Settings).BuildMenuStartInfo();
        if (psi == null)
        {
            await Dialogs.Info(owner, Loc.T("ra.missing_title"), Loc.T("launch.retroarch_missing"));
            return;
        }
        await Dialogs.Info(owner, Loc.T("ra.configure_title"), Loc.T("ra.configure_text"));
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
            await Dialogs.Info(this, Loc.T("backup.done_title"), Loc.T("backup.done_text", zip));
            MainWindow.OpenFolder(App.Paths.BackupsDir);
        }
        catch (Exception ex)
        {
            Log.Error("Sao lưu lỗi", ex);
            await Dialogs.Info(this, Loc.T("backup.error_title"), ex.Message);
        }
    }

    private async Task ChangeDataDir()
    {
        var start = await StorageProvider.TryGetFolderFromPathAsync(App.Paths.DataDir);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("settings.pick_data_dir"),
            SuggestedStartLocation = start,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        _newDataDir = path;
        _dataDir.Text = Loc.T("settings.data_dir_new", _newDataDir);
    }

    private async Task BrowseDuck()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("settings.pick_duck"), AllowMultiple = false });
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
            await Dialogs.Info(this, Loc.T("settings.duck_missing_title"), Loc.T("settings.duck_missing_text"));
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
                // Mang save sang thư mục mới để không "mất" save (thư mục cũ giữ nguyên)
                int copied = await Task.Run(() => BackupService.CopyUserData(App.Paths, newPaths));
                var text = Loc.T("settings.data_dir_changed_text");
                if (copied > 0) text += "\n\n" + Loc.T("settings.data_dir_copied", copied);
                await Dialogs.Info(this, Loc.T("settings.data_dir_changed_title"), text);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lưu cài đặt lỗi", ex);
            await Dialogs.Info(this, Loc.T("settings.save_error_title"), ex.Message);
            return;
        }
        // Đổi ngôn ngữ: cửa sổ chính tải lại giao diện sau khi cửa sổ này đóng
        var lang = _languages.FirstOrDefault(l => l.Button.IsChecked == true).Code;
        if (lang != null && lang != Loc.Language) ChosenLanguage = lang;
        Close();
    }
}
