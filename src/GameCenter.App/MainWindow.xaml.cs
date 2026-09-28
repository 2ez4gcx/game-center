using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Scanning;
using GameCenter.Core.Util;

namespace GameCenter.App;

public partial class MainWindow : Window
{
    private const string TabAll = "__all";
    private const string TabFav = "__fav";
    private const string TabRecent = "__recent";

    private List<GameItem> _all = new();
    private string _tab = TabAll;
    private readonly List<RadioButton> _tabButtons = new();
    private readonly Gamepad _pad;
    private bool _gameRunning;

    public MainWindow()
    {
        InitializeComponent();
        // Vừa màn hình nhỏ / scale 125–150%
        Height = Math.Min(Height, SystemParameters.WorkArea.Height * 0.94);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width * 0.96);
        _pad = new Gamepad { IsEnabled = () => IsActive && !_gameRunning };
        _pad.Pressed += OnPad;
        _pad.ConnectionChanged += _ => UpdateControlUi();
        Loaded += async (_, _) =>
        {
            (App.Settings.ControlMode == ControlModes.Keyboard ? ModeKeyboard : ModeGamepad).IsChecked = true;
            UpdateControlUi();
            AuthorText.Text = $"Game Center v{AppInfo.Version}  ·  by {AppInfo.Author}";
            ApplyLauncherFullscreen();
            Reload();
            // Mỗi lần mở đều tự quét, sau đó theo dõi thư mục game
            await ScanAsync(silent: true);
            StartWatching();
            GameList.Focus();
        };
        Closed += (_, _) => { _pad.Dispose(); _watcher?.Dispose(); };
    }

    // ------------------------------------------------------------------ Cách điều khiển

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        var mode = (string)((FrameworkElement)sender).Tag;
        if (App.Settings.ControlMode != mode)
        {
            App.Settings.ControlMode = mode;
            App.Settings.Save(App.Paths.SettingsJson);
        }
        UpdateControlUi();
    }

    private void UpdateControlUi()
    {
        bool keyboard = App.Settings.ControlMode == ControlModes.Keyboard;
        var k = KeyboardLayout.Resolve(App.Settings.KeyBindings);
        string D(string id) => KeyboardLayout.DisplayName(k[id]);
        ExitHint.Text = keyboard ? " bấm phím Esc" : " giữ START + SELECT  (hoặc Esc)";
        ControlHint.Text = keyboard
            ? $"Di chuyển {D("up")}{D("left")}{D("down")}{D("right")}  ·  Nút {D("x")}{D("y")}{D("b")}{D("a")}  ·  START {D("start")}"
            : "Chọn: ↑↓   Chơi: A   Đổi mục: LB / RB";
        ControlSetupButton.Content = keyboard ? "⌨  Thiết lập phím" : "🎮  Thiết lập tay cầm";

        bool connected = _pad?.IsConnected == true;
        PadDot.Fill = connected ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
        PadDot.Effect = connected ? (System.Windows.Media.Effects.Effect)FindResource("Glow") : null;
        PadStatus.Text = connected ? "Tay cầm đã kết nối" : "Chưa thấy tay cầm";
    }

    private void ControlSetup_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.ControlMode == ControlModes.Keyboard)
        {
            new KeyBindingWindow { Owner = this }.ShowDialog();
            UpdateControlUi();
        }
        else
        {
            SettingsWindow.OpenRetroArchMenu(this);
        }
    }

    // ------------------------------------------------------------------ Tự quét khi thư mục game thay đổi

    private FileSystemWatcher? _watcher;
    private System.Windows.Threading.DispatcherTimer? _rescanTimer;
    private bool _scanning;
    private bool _rescanPending;

    private void StartWatching()
    {
        try
        {
            // Chờ 3 giây sau thay đổi cuối cùng (chép game lớn tạo rất nhiều sự kiện)
            _rescanTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _rescanTimer.Tick += async (_, _) =>
            {
                _rescanTimer.Stop();
                if (_gameRunning || _scanning) { _rescanPending = true; return; }
                await ScanAsync(silent: true);
            };

            _watcher = new FileSystemWatcher(App.Paths.GamesDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            FileSystemEventHandler onChange = (_, _) => Dispatcher.BeginInvoke(RequestRescan);
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Changed += onChange;
            _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(RequestRescan);
            _watcher.Error += (_, e) => { Log.Warn($"Theo dõi thư mục game lỗi: {e.GetException().Message}"); Dispatcher.BeginInvoke(RequestRescan); };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Log.Error("Không theo dõi được thư mục game", ex); }
    }

    private void RequestRescan()
    {
        if (_rescanTimer == null) return;
        _rescanTimer.Stop();
        _rescanTimer.Start();
    }

    public void ApplyLauncherFullscreen()
    {
        if (App.Settings.LauncherFullscreen)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        }
    }

    // ------------------------------------------------------------------ Danh sách

    private void Reload(long? selectId = null)
    {
        selectId ??= (GameList.SelectedItem as GameItem)?.Record.Id;
        _all = App.Db.GetGames().Select(g => new GameItem(g)).ToList();
        BuildTabs();
        ApplyFilter(selectId);
    }

    private void BuildTabs()
    {
        int Count(Func<GameItem, bool> f) => _all.Count(f);
        var tabs = new List<(string Key, string Icon, string Label, int Count)> { (TabAll, "◆", "Tất cả", _all.Count) };
        foreach (var p in App.Catalog.Platforms.Where(p => p.Enabled && _all.Any(g => g.Record.Platform == p.Name)))
            tabs.Add((p.Name, "▸", p.DisplayName, Count(g => g.Record.Platform == p.Name)));
        tabs.Add((TabFav, "★", "Yêu thích", Count(g => g.Record.IsFavorite)));
        tabs.Add((TabRecent, "◷", "Chơi gần đây", Count(g => g.Record.LastPlayedAt != null)));
        if (_all.Any(g => g.IsUnknown))
            tabs.Add((GameDatabase.UnknownPlatform, "?", "Chưa nhận ra", Count(g => g.IsUnknown)));

        if (!tabs.Any(t => t.Key == _tab)) _tab = TabAll;
        TabsPanel.Children.Clear();
        _tabButtons.Clear();
        foreach (var (key, icon, label, count) in tabs)
        {
            var content = new DockPanel { LastChildFill = true };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 1, 9, 1), Background = (Brush)FindResource("Line"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), FontSize = 15, FontFamily = (FontFamily)FindResource("TechFont"), Foreground = (Brush)FindResource("Accent") },
            };
            DockPanel.SetDock(badge, Dock.Right);
            content.Children.Add(badge);
            var text = new TextBlock { Text = $"{icon}   {label}", VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) });
            content.Children.Add(text);

            var rb = new RadioButton
            {
                Content = content, Tag = key, GroupName = "tabs", IsChecked = key == _tab,
                Style = (Style)FindResource("NavButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            rb.Checked += (_, _) => { _tab = key; ApplyFilter(null); };
            TabsPanel.Children.Add(rb);
            _tabButtons.Add(rb);
        }
    }

    private string TabTitle => _tab switch
    {
        TabAll => "Tất cả game",
        TabFav => "Yêu thích",
        TabRecent => "Chơi gần đây",
        GameDatabase.UnknownPlatform => "Chưa nhận ra",
        _ => App.Catalog.Get(_tab)?.DisplayName ?? _tab,
    };

    private void ApplyFilter(long? selectId)
    {
        IEnumerable<GameItem> q = _all;
        q = _tab switch
        {
            TabAll => q,
            TabFav => q.Where(g => g.Record.IsFavorite),
            TabRecent => q.Where(g => g.Record.LastPlayedAt != null).OrderByDescending(g => g.Record.LastPlayedAt),
            _ => q.Where(g => g.Record.Platform == _tab),
        };
        var search = SearchBox.Text.Trim();
        if (search.Length > 0)
        {
            var s = RemoveDiacritics(search);
            q = q.Where(g => RemoveDiacritics(g.Name).Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        var list = q.ToList();
        GameList.ItemsSource = list;
        GameList.SelectedItem = list.FirstOrDefault(g => g.Record.Id == selectId) ?? list.FirstOrDefault();
        if (GameList.SelectedItem != null) GameList.ScrollIntoView(GameList.SelectedItem);

        bool empty = list.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            if (_all.Count == 0)
            {
                EmptyTitle.Text = "Chưa có game nào";
                EmptyText.Text = $"1. Bấm \"Mở thư mục game\".\n2. Chép game vào thư mục đó (để chung một chỗ cũng được).\n3. Game sẽ tự hiện ra sau vài giây.\n\nThư mục game: {App.Paths.GamesDir}";
            }
            else
            {
                EmptyTitle.Text = "Không có game nào ở đây";
                EmptyText.Text = search.Length > 0 ? $"Không tìm thấy game có tên \"{search}\"." : "Mục này hiện chưa có game.";
            }
        }
        SectionTitle.Text = TabTitle;
        SearchPlaceholder.Visibility = search.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = list.Count == _all.Count ? $"{_all.Count} GAME" : $"{list.Count} / {_all.Count} GAME";
    }

    private static string RemoveDiacritics(string s)
    {
        var norm = s.Normalize(System.Text.NormalizationForm.FormD);
        var chars = norm.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter(null);
    }

    private GameItem? Selected => GameList.SelectedItem as GameItem;
    private static GameItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as GameItem;

    // ------------------------------------------------------------------ Quét

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync(silent: false);

    private async Task ScanAsync(bool silent)
    {
        if (_scanning) { _rescanPending = true; return; }
        _scanning = true;
        StatusText.Text = "Đang quét game...";
        // Quét tự động không khóa giao diện
        if (!silent) IsEnabled = false;
        try
        {
            var scanner = new GameScanner(App.Catalog, App.Paths.GamesDir, App.Paths.PlaylistsDir);
            var summary = await Task.Run(() => App.Db.ApplyScan(scanner.Scan()));
            Log.Info($"Quét xong: {summary}");
            Reload();
            if (!silent)
            {
                var msg = $"Game mới: {summary.Added}\nTổng số game: {_all.Count}";
                if (summary.Moved > 0) msg += $"\nGame đã chuyển chỗ: {summary.Moved}";
                if (summary.Missing > 0) msg += $"\nGame không còn tìm thấy: {summary.Missing}";
                if (summary.Unknown > 0) msg += $"\n\nCó {summary.Unknown} game chưa nhận ra. Hãy xem mục \"Chưa nhận ra\".";
                if (summary.Broken > 0) msg += $"\n\nCó {summary.Broken} game bị thiếu file (có dấu ⚠).";
                Dialogs.Info(this, "Quét xong", msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi quét", ex);
            Dialogs.Info(this, "Không quét được", "Có lỗi khi quét thư mục game. Chi tiết đã ghi trong file log.");
        }
        finally
        {
            _scanning = false;
            if (!silent) { IsEnabled = true; GameList.Focus(); }
            if (_rescanPending && !_gameRunning) { _rescanPending = false; RequestRescan(); }
        }
    }

    // ------------------------------------------------------------------ Chơi

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) { GameList.SelectedItem = item; _ = PlayAsync(item); }
    }

    private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } item) _ = PlayAsync(item);
    }

    private void GameList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is { } item) { _ = PlayAsync(item); e.Handled = true; }
    }

    private async Task PlayAsync(GameItem item)
    {
        if (_gameRunning) return;
        if (item.IsUnknown) { ChoosePlatform(item); return; }

        var launcher = new EmulatorLauncher(App.Paths, App.Catalog, App.Settings);
        var plan = launcher.Plan(item.Record);
        if (plan.Problem != LaunchProblem.None)
        {
            Log.Warn($"Không chạy được {item.Record.LaunchFile}: {plan.Problem}");
            var title = plan.Problem == LaunchProblem.BiosMissing ? "Thiếu BIOS" : "Không chạy được game";
            Dialogs.Info(this, title, plan.Message);
            return;
        }

        if (item.Record.DiscCount >= 2 && App.Settings.ShowDiscChangeGuide && !App.Settings.UseUserDuckStation)
            HelpWindow.ShowDiscGuide(this);

        // Save state của giả lập: hỏi chơi tiếp hay chơi từ đầu (save trong game luôn được giữ)
        bool useStates = App.Settings.AskSaveStateOnExit && !App.Settings.UseUserDuckStation;
        var states = new SaveStateService(App.Paths.StatesDir);
        if (useStates && states.FindAutoState(item.Record.LaunchFile) != null)
        {
            bool resume = Dialogs.Confirm(this, item.Name,
                "Bạn đã lưu chỗ đang chơi ở lần trước.\nMuốn chơi tiếp từ chỗ đó, hay chơi từ đầu?\n\n(Save trong game của bạn vẫn được giữ nguyên dù chọn cách nào.)",
                yes: "▶  Chơi tiếp", no: "Chơi từ đầu");
            if (resume)
            {
                // Chèn trước "-L": chỉ lần chạy này mới nạp save state
                plan.StartInfo!.ArgumentList.Insert(2, "--appendconfig");
                plan.StartInfo!.ArgumentList.Insert(3, WriteResumeConfig());
            }
        }

        _gameRunning = true;
        var startedUtc = DateTime.UtcNow;
        try
        {
            App.Db.MarkPlayed(item.Record.Id);
            var previous = useStates ? states.BackupBeforePlay(item.Record.LaunchFile) : null;
            Hide();
            await launcher.RunAsync(plan.StartInfo!);
            BringToFront();
            if (useStates) AskKeepSaveState(item, states, startedUtc, previous);
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi chạy game", ex);
            Show();
            Dialogs.Info(this, "Không chạy được game", "Không mở được trình giả lập. Chi tiết đã ghi trong file log.");
        }
        finally
        {
            _gameRunning = false;
            BringToFront();
            Reload(item.Record.Id);
            GameList.Focus();
            if (_rescanPending) { _rescanPending = false; RequestRescan(); }
        }
    }

    /// <summary>File cấu hình phụ chỉ bật nạp save state cho lần chạy này.</summary>
    private static string WriteResumeConfig()
    {
        var path = Path.Combine(App.Paths.ConfigDir, "resume.cfg");
        File.WriteAllText(path, "savestate_auto_load = \"true\"\n");
        return path;
    }

    /// <summary>Sau khi thoát game: hỏi có lưu lại chỗ đang chơi không. Bắt buộc chọn (Esc/B không đóng được).</summary>
    private void AskKeepSaveState(GameItem item, SaveStateService states, DateTime startedUtc, string? previous)
    {
        var state = states.NewStateSince(item.Record.LaunchFile, startedUtc);
        if (state == null)
        {
            // Giả lập không chụp được (ví dụ bị tắt ngang): giữ nguyên bản cũ, bỏ bản sao lưu
            if (previous != null) SaveStateService.Keep(previous);
            return;
        }
        bool keep = Dialogs.Confirm(this, "Lưu lại chỗ đang chơi?",
            $"Bạn vừa thoát \"{item.Name}\".\nCó muốn lưu lại đúng chỗ đang chơi để lần sau chơi tiếp không?\n\n(Save trong game của bạn vẫn được giữ nguyên dù chọn cách nào.)",
            yes: "💾  Lưu lại", no: "Không lưu", cancellable: false);
        try
        {
            if (keep) SaveStateService.Keep(state);
            else SaveStateService.Discard(state);
            Log.Info($"Save state {(keep ? "giữ" : "bỏ")}: {state}");
        }
        catch (Exception ex) { Log.Error("Lỗi xử lý save state", ex); }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Sau khi game đóng: hiện lại launcher và đưa lên trước.</summary>
    private void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ApplyLauncherFullscreen();
        Topmost = true;
        Activate();
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Topmost = false;
        Focus();
    }

    // ------------------------------------------------------------------ Thao tác khác

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) ToggleFavorite(item);
    }

    private void ToggleFavorite(GameItem item)
    {
        item.Record.IsFavorite = !item.Record.IsFavorite;
        App.Db.SetFavorite(item.Record.Id, item.Record.IsFavorite);
        item.Refresh();
        if (_tab == TabFav) ApplyFilter(item.Record.Id);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) { GameList.SelectedItem = item; ShowMore(item); }
    }

    private void ShowMore(GameItem item)
    {
        var options = new List<(string Label, Action Run)>
        {
            ("✏  Đổi tên hiển thị", () => Rename(item)),
            ("📂  Mở thư mục chứa game", () => OpenFolder(item.Record.FolderPath)),
            ("🎮  Chọn hệ máy", () => ChoosePlatform(item)),
            ("🖼  Chọn ảnh bìa", () => ChooseCover(item)),
            (item.Record.IsFavorite ? "☆  Bỏ yêu thích" : "★  Thêm vào yêu thích", () => ToggleFavorite(item)),
        };
        int i = Dialogs.Choose(this, item.Name, null, options.Select(o => o.Label).ToList());
        if (i >= 0) options[i].Run();
    }

    private void Rename(GameItem item)
    {
        var name = Dialogs.AskText(this, "Đổi tên hiển thị",
            "Tên mới chỉ hiển thị trong Game Center, không đổi tên file. Để trống để dùng lại tên gốc.", item.Name);
        if (name == null) return;
        App.Db.SetDisplayTitle(item.Record.Id, name == item.Record.Title ? null : name);
        Reload(item.Record.Id);
    }

    private void ChoosePlatform(GameItem item)
    {
        var platforms = App.Catalog.Platforms.Where(p => p.Enabled).ToList();
        int i = Dialogs.Choose(this, "Chọn hệ máy", $"Game \"{item.Name}\" là game của máy nào?",
            platforms.Select(p => p.DisplayName).ToList());
        if (i < 0) return;
        App.Db.SetPlatform(item.Record.Id, platforms[i].Name);
        Reload(item.Record.Id);
    }

    private void ChooseCover(GameItem item)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Ảnh (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "Chọn ảnh bìa" };
        if (dlg.ShowDialog(this) != true) return;
        // Chép ảnh vào Covers/ để không phụ thuộc vị trí gốc
        var dir = Path.Combine(App.Paths.CoversDir, item.Record.Platform);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{item.Record.Id}{Path.GetExtension(dlg.FileName).ToLowerInvariant()}");
        File.Copy(dlg.FileName, dest, overwrite: true);
        App.Db.SetCover(item.Record.Id, dest);
        Reload(item.Record.Id);
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception ex) { Log.Error($"Không mở được thư mục {path}", ex); }
    }

    private void OpenGamesFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(App.Paths.GamesDir);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
        ApplyLauncherFullscreen();
        Reload();
    }

    private void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

    // ------------------------------------------------------------------ Tay cầm

    private void OnPad(PadButton b)
    {
        var list = GameList.ItemsSource as List<GameItem>;
        int idx = GameList.SelectedIndex;
        switch (b)
        {
            case PadButton.Up:
                if (list is { Count: > 0 }) GameList.SelectedIndex = Math.Max(0, idx - 1);
                break;
            case PadButton.Down:
                if (list is { Count: > 0 }) GameList.SelectedIndex = Math.Min(list.Count - 1, idx + 1);
                break;
            case PadButton.LB:
            case PadButton.RB:
            case PadButton.Left:
            case PadButton.Right:
                int cur = _tabButtons.FindIndex(t => (string)t.Tag == _tab);
                int next = b is PadButton.LB or PadButton.Left ? cur - 1 : cur + 1;
                if (next < 0) next = _tabButtons.Count - 1;
                if (next >= _tabButtons.Count) next = 0;
                _tabButtons[next].IsChecked = true;
                break;
            case PadButton.A:
                if (Selected is { } a) _ = PlayAsync(a);
                break;
            case PadButton.Y:
                if (Selected is { } y) ToggleFavorite(y);
                break;
            case PadButton.X:
                if (Selected is { } x) ShowMore(x);
                break;
            case PadButton.B:
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";
                break;
            case PadButton.Start:
                Settings_Click(this, new RoutedEventArgs());
                break;
            case PadButton.Back:
                Help_Click(this, new RoutedEventArgs());
                break;
        }
        if (GameList.SelectedItem != null)
        {
            GameList.ScrollIntoView(GameList.SelectedItem);
            (GameList.ItemContainerGenerator.ContainerFromItem(GameList.SelectedItem) as ListBoxItem)?.Focus();
        }
    }
}
