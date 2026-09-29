using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Scanning;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

public partial class MainWindow : Window
{
    private const string TabAll = "__all";
    private const string TabFav = "__fav";
    private const string TabRecent = "__recent";

    private List<GameItem> _all = new();
    private string _tab = TabAll;
    private readonly List<RadioButton> _tabButtons = new();
    private bool _gameRunning;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        Gamepad.Instance.Pressed += OnPad;
        Gamepad.Instance.ConnectionChanged += _ => UpdateControlUi();

        Opened += async (_, _) =>
        {
            // Vừa màn hình nhỏ / scale 125–150%
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
                Height = Math.Min(Height, area.Height * 0.94);
                Width = Math.Min(Width, area.Width * 0.96);
            }
            (App.Settings.ControlMode == ControlModes.Keyboard ? ModeKeyboard : ModeGamepad).IsChecked = true;
            AuthorText.Text = $"Game Center v{AppInfo.Version}  ·  by {AppInfo.Author}";
            UpdateControlUi();
            ApplyLauncherFullscreen();
            _loaded = true;
            Reload();
            // Mỗi lần mở đều tự quét, sau đó theo dõi thư mục game
            await ScanAsync(silent: true);
            StartWatching();
            GameList.Focus();
        };
        Closed += (_, _) =>
        {
            Gamepad.Instance.Pressed -= OnPad;
            _watcher?.Dispose();
        };
    }

    // ------------------------------------------------------------------ Cách điều khiển

    private void Mode_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } rb) return;
        var mode = (string)rb.Tag!;
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
        ExitHint.Text = keyboard ? "bấm Esc 2 lần" : "bấm START + SELECT 2 lần  (hoặc Esc 2 lần)";
        ControlHint.Text = keyboard
            ? $"Di chuyển {D("up")}{D("left")}{D("down")}{D("right")}  ·  Nút {D("x")}{D("y")}{D("b")}{D("a")}  ·  START {D("start")}"
            : "Chọn: ↑↓   Chơi: A   Đổi mục: LB / RB";
        ControlSetupButton.Content = keyboard ? "⌨  Thiết lập phím" : "🎮  Thiết lập tay cầm";

        bool connected = Gamepad.Instance.IsConnected;
        PadDot.Fill = connected ? Dialogs.Res("Accent") : new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
        PadStatus.Text = connected ? "Tay cầm đã kết nối" : "Chưa thấy tay cầm";
    }

    private async void ControlSetup_Click(object? sender, RoutedEventArgs e)
    {
        if (App.Settings.ControlMode == ControlModes.Keyboard)
        {
            await new KeyBindingWindow().ShowDialog(this);
            UpdateControlUi();
        }
        else
        {
            await SettingsWindow.OpenRetroArchMenu(this);
        }
    }

    // ------------------------------------------------------------------ Tự quét khi thư mục game thay đổi

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _rescanTimer;
    private bool _scanning;
    private bool _rescanPending;

    private void StartWatching()
    {
        try
        {
            // Chờ 3 giây sau thay đổi cuối cùng (chép game lớn tạo rất nhiều sự kiện)
            _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
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
            FileSystemEventHandler onChange = (_, _) => Dispatcher.UIThread.Post(RequestRescan);
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Changed += onChange;
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(RequestRescan);
            _watcher.Error += (_, e) => { Log.Warn($"Theo dõi thư mục game lỗi: {e.GetException().Message}"); Dispatcher.UIThread.Post(RequestRescan); };
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
        if (App.Settings.LauncherFullscreen) WindowState = WindowState.FullScreen;
        else if (WindowState == WindowState.FullScreen) WindowState = WindowState.Normal;
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
            var content = new DockPanel();
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 1), Background = Dialogs.Res("Line"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), FontSize = 15, FontFamily = Dialogs.TechFont, Foreground = Dialogs.Res("Accent") },
            };
            DockPanel.SetDock(badge, Dock.Right);
            content.Children.Add(badge);
            content.Children.Add(new TextBlock { Text = $"{icon}   {label}", VerticalAlignment = VerticalAlignment.Center });

            var rb = new RadioButton { Content = content, Tag = key, GroupName = "tabs", IsChecked = key == _tab };
            rb.Classes.Add("nav");
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && _tab != key) { _tab = key; ApplyFilter(null); } };
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
        IEnumerable<GameItem> q = _tab switch
        {
            TabAll => _all,
            TabFav => _all.Where(g => g.Record.IsFavorite),
            TabRecent => _all.Where(g => g.Record.LastPlayedAt != null).OrderByDescending(g => g.Record.LastPlayedAt),
            _ => _all.Where(g => g.Record.Platform == _tab),
        };
        var search = (SearchBox.Text ?? "").Trim();
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
        EmptyPanel.IsVisible = empty;
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
        StatusText.Text = list.Count == _all.Count ? $"{_all.Count} GAME" : $"{list.Count} / {_all.Count} GAME";
    }

    private static string RemoveDiacritics(string s)
    {
        var norm = s.Normalize(System.Text.NormalizationForm.FormD);
        var chars = norm.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_loaded) ApplyFilter(null);
    }

    private GameItem? Selected => GameList.SelectedItem as GameItem;
    private static GameItem? ItemOf(object? sender) => (sender as Control)?.DataContext as GameItem;

    // ------------------------------------------------------------------ Quét

    private async void Scan_Click(object? sender, RoutedEventArgs e) => await ScanAsync(silent: false);

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
                IsEnabled = true;
                var msg = $"Game mới: {summary.Added}\nTổng số game: {_all.Count}";
                if (summary.Moved > 0) msg += $"\nGame đã chuyển chỗ: {summary.Moved}";
                if (summary.Missing > 0) msg += $"\nGame không còn tìm thấy: {summary.Missing}";
                if (summary.Unknown > 0) msg += $"\n\nCó {summary.Unknown} game chưa nhận ra. Hãy xem mục \"Chưa nhận ra\".";
                if (summary.Broken > 0) msg += $"\n\nCó {summary.Broken} game bị thiếu file (có dấu ⚠).";
                await Dialogs.Info(this, "Quét xong", msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi quét", ex);
            IsEnabled = true;
            await Dialogs.Info(this, "Không quét được", "Có lỗi khi quét thư mục game. Chi tiết đã ghi trong file log.");
        }
        finally
        {
            _scanning = false;
            if (!silent) { IsEnabled = true; GameList.Focus(); }
            if (_rescanPending && !_gameRunning) { _rescanPending = false; RequestRescan(); }
        }
    }

    // ------------------------------------------------------------------ Chơi

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) { GameList.SelectedItem = item; _ = PlayAsync(item); }
    }

    private void GameList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Selected is { } item) _ = PlayAsync(item);
    }

    private void GameList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is { } item) { _ = PlayAsync(item); e.Handled = true; }
    }

    private async Task PlayAsync(GameItem item)
    {
        if (_gameRunning) return;
        if (item.IsUnknown) { await ChoosePlatform(item); return; }

        var launcher = new EmulatorLauncher(App.Paths, App.Catalog, App.Settings);
        var plan = launcher.Plan(item.Record);
        if (plan.Problem != LaunchProblem.None)
        {
            Log.Warn($"Không chạy được {item.Record.LaunchFile}: {plan.Problem}");
            var title = plan.Problem == LaunchProblem.BiosMissing ? "Thiếu BIOS" : "Không chạy được game";
            await Dialogs.Info(this, title, plan.Message);
            return;
        }

        if (item.Record.DiscCount >= 2 && App.Settings.ShowDiscChangeGuide && !App.Settings.UseUserDuckStation)
            await HelpWindow.ShowDiscGuide(this);

        // RetroArch ghi lại toàn bộ cấu hình khi thoát (kể cả giá trị từ --appendconfig của lần trước),
        // nên ghi lại cấu hình chuẩn trước mỗi lần chạy.
        try { RetroArchConfig.Write(App.Paths, App.Settings); }
        catch (Exception ex) { Log.Error("Không ghi được retroarch.cfg", ex); }

        // Save state của giả lập: hỏi chơi tiếp hay chơi từ đầu (save trong game luôn được giữ)
        bool useStates = App.Settings.AskSaveStateOnExit && !App.Settings.UseUserDuckStation;
        var states = new SaveStateService(App.Paths.StatesDir);
        if (useStates && states.FindAutoState(item.Record.LaunchFile) != null)
        {
            bool resume = await Dialogs.Confirm(this, item.Name,
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
        Gamepad.Instance.Suspended = true;
        var startedUtc = DateTime.UtcNow;
        try
        {
            App.Db.MarkPlayed(item.Record.Id);
            var previous = useStates ? states.BackupBeforePlay(item.Record.LaunchFile) : null;
            Hide();
            await launcher.RunAsync(plan.StartInfo!);
            BringToFront();
            if (useStates) await AskKeepSaveState(item, states, startedUtc, previous);
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi chạy game", ex);
            Show();
            await Dialogs.Info(this, "Không chạy được game", "Không mở được trình giả lập. Chi tiết đã ghi trong file log.");
        }
        finally
        {
            _gameRunning = false;
            Gamepad.Instance.Suspended = false;
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
    private async Task AskKeepSaveState(GameItem item, SaveStateService states, DateTime startedUtc, string? previous)
    {
        var state = states.NewStateSince(item.Record.LaunchFile, startedUtc);
        if (state == null)
        {
            // Giả lập không chụp được (ví dụ bị tắt ngang): giữ nguyên bản cũ, bỏ bản sao lưu
            if (previous != null) SaveStateService.Keep(previous);
            return;
        }
        bool keep = await Dialogs.Confirm(this, "Lưu lại chỗ đang chơi?",
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

    /// <summary>Sau khi game đóng: hiện lại launcher và đưa lên trước.</summary>
    private void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ApplyLauncherFullscreen();
        Topmost = true;
        Activate();
        Topmost = false;
    }

    // ------------------------------------------------------------------ Thao tác khác

    private void Favorite_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) ToggleFavorite(item);
    }

    private void ToggleFavorite(GameItem item)
    {
        item.Record.IsFavorite = !item.Record.IsFavorite;
        App.Db.SetFavorite(item.Record.Id, item.Record.IsFavorite);
        item.Refresh();
        if (_tab == TabFav) ApplyFilter(item.Record.Id);
        BuildTabs();
    }

    private async void More_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) { GameList.SelectedItem = item; await ShowMore(item); }
    }

    private async Task ShowMore(GameItem item)
    {
        var options = new List<(string Label, Func<Task> Run)>
        {
            ("✏  Đổi tên hiển thị", () => Rename(item)),
            ("📂  Mở thư mục chứa game", () => { OpenFolder(item.Record.FolderPath); return Task.CompletedTask; }),
            ("🎮  Chọn hệ máy", () => ChoosePlatform(item)),
            ("🖼  Chọn ảnh bìa", () => ChooseCover(item)),
            (item.Record.IsFavorite ? "☆  Bỏ yêu thích" : "★  Thêm vào yêu thích", () => { ToggleFavorite(item); return Task.CompletedTask; }),
        };
        int i = await Dialogs.Choose(this, item.Name, null, options.Select(o => o.Label).ToList());
        if (i >= 0) await options[i].Run();
    }

    private async Task Rename(GameItem item)
    {
        var name = await Dialogs.AskText(this, "Đổi tên hiển thị",
            "Tên mới chỉ hiển thị trong Game Center, không đổi tên file. Để trống để dùng lại tên gốc.", item.Name);
        if (name == null) return;
        App.Db.SetDisplayTitle(item.Record.Id, name == item.Record.Title ? null : name);
        Reload(item.Record.Id);
    }

    private async Task ChoosePlatform(GameItem item)
    {
        var platforms = App.Catalog.Platforms.Where(p => p.Enabled).ToList();
        int i = await Dialogs.Choose(this, "Chọn hệ máy", $"Game \"{item.Name}\" là game của máy nào?",
            platforms.Select(p => p.DisplayName).ToList());
        if (i < 0) return;
        App.Db.SetPlatform(item.Record.Id, platforms[i].Name);
        Reload(item.Record.Id);
    }

    private async Task ChooseCover(GameItem item)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn ảnh bìa",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Ảnh") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        var src = files.FirstOrDefault()?.TryGetLocalPath();
        if (src == null) return;
        // Chép ảnh vào Covers/ để không phụ thuộc vị trí gốc
        var dir = Path.Combine(App.Paths.CoversDir, item.Record.Platform);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{item.Record.Id}{Path.GetExtension(src).ToLowerInvariant()}");
        File.Copy(src, dest, overwrite: true);
        App.Db.SetCover(item.Record.Id, dest);
        Reload(item.Record.Id);
    }

    public static void OpenFolder(string path)
    {
        try { OsPlatform.OpenFolder(path); }
        catch (Exception ex) { Log.Error($"Không mở được thư mục {path}", ex); }
    }

    private void OpenGamesFolder_Click(object? sender, RoutedEventArgs e) => OpenFolder(App.Paths.GamesDir);

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        await new SettingsWindow().ShowDialog(this);
        ApplyLauncherFullscreen();
        UpdateControlUi();
        Reload();
    }

    private async void Help_Click(object? sender, RoutedEventArgs e) => await new HelpWindow().ShowDialog(this);

    // ------------------------------------------------------------------ Tay cầm

    private void OnPad(PadButton b)
    {
        if (!IsActive || _gameRunning) return;
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
            case PadButton.LB or PadButton.RB or PadButton.Left or PadButton.Right:
                int cur = _tabButtons.FindIndex(t => (string)t.Tag! == _tab);
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
                if (Selected is { } x) _ = ShowMore(x);
                break;
            case PadButton.B:
                if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
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
            GameList.ContainerFromItem(GameList.SelectedItem)?.Focus();
        }
    }
}
