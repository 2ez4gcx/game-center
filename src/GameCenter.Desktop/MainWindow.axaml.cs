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
using GameCenter.Core.Localization;
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
            LanguageButton.Content = "🌐  " + string.Join(" / ", Loc.Available.Where(a => a.Code != Loc.Language).Select(a => a.Name));
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
            // Cửa sổ được tạo lại khi đổi ngôn ngữ: không để cửa sổ cũ quét thêm lần nữa
            _rescanTimer?.Stop();
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
        ExitHint.Text = Loc.T(keyboard ? "main.exit_hint_keyboard" : "main.exit_hint_gamepad");
        ControlHint.Text = keyboard
            ? Loc.T("main.control_hint_keyboard", $"{D("up")}{D("left")}{D("down")}{D("right")}", $"{D("x")}{D("y")}{D("b")}{D("a")}", D("start"))
            : Loc.T("main.control_hint_gamepad");
        ControlSetupButton.Content = Loc.T(keyboard ? "main.setup_keys" : "main.setup_gamepad");

        bool connected = Gamepad.Instance.IsConnected;
        PadDot.Fill = connected ? Dialogs.Res("Accent") : new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
        PadStatus.Text = Loc.T(connected ? "main.pad_connected" : "main.pad_missing");
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
        var tabs = new List<(string Key, string Icon, string Label, int Count)> { (TabAll, "◆", Loc.T("tab.all"), _all.Count) };
        foreach (var p in App.Catalog.Platforms.Where(p => p.Enabled && _all.Any(g => g.Record.Platform == p.Name)))
            tabs.Add((p.Name, "▸", p.DisplayName, Count(g => g.Record.Platform == p.Name)));
        tabs.Add((TabFav, "★", Loc.T("tab.favorites"), Count(g => g.Record.IsFavorite)));
        tabs.Add((TabRecent, "◷", Loc.T("tab.recent"), Count(g => g.Record.LastPlayedAt != null)));
        if (_all.Any(g => g.IsUnknown))
            tabs.Add((GameDatabase.UnknownPlatform, "?", Loc.T("tab.unknown"), Count(g => g.IsUnknown)));

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
        TabAll => Loc.T("tab.all_title"),
        TabFav => Loc.T("tab.favorites"),
        TabRecent => Loc.T("tab.recent"),
        GameDatabase.UnknownPlatform => Loc.T("tab.unknown"),
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
                EmptyTitle.Text = Loc.T("main.empty_title");
                EmptyText.Text = Loc.T("main.empty_text", App.Paths.GamesDir);
            }
            else
            {
                EmptyTitle.Text = Loc.T("main.empty_here_title");
                EmptyText.Text = search.Length > 0 ? Loc.T("main.empty_search", search) : Loc.T("main.empty_tab");
            }
        }
        SectionTitle.Text = TabTitle;
        StatusText.Text = list.Count == _all.Count ? Loc.Plural("main.status_count", _all.Count) : Loc.T("main.status_filtered", list.Count, _all.Count);
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
        StatusText.Text = Loc.T("main.scanning");
        // Quét tự động không khóa giao diện
        if (!silent) IsEnabled = false;
        try
        {
            var scanner = new GameScanner(App.Catalog, App.Paths.GamesDir, App.Paths.PlaylistsDir);
            var summary = await Task.Run(() => App.Db.ApplyScan(scanner.Scan()));
            Log.Info($"Quét xong: {summary}");
            ImportBios(scanner.BiosFiles);
            Reload();
            if (!silent)
            {
                IsEnabled = true;
                var msg = Loc.T("scan.result", summary.Added, _all.Count);
                if (summary.Moved > 0) msg += Loc.T("scan.moved", summary.Moved);
                if (summary.Missing > 0) msg += Loc.T("scan.missing", summary.Missing);
                if (summary.Unknown > 0) msg += Loc.T("scan.unknown", summary.Unknown);
                if (summary.Broken > 0) msg += Loc.T("scan.broken", summary.Broken);
                await Dialogs.Info(this, Loc.T("scan.done_title"), msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi quét", ex);
            IsEnabled = true;
            await Dialogs.Info(this, Loc.T("scan.error_title"), Loc.T("scan.error_text"));
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
            if (plan.Problem == LaunchProblem.BiosMissing)
            {
                // Mở luôn thư mục BIOS để người dùng chép file vào
                if (await Dialogs.Confirm(this, Loc.T("play.bios_missing_title"), plan.Message, yes: Loc.T("play.open_bios"), no: Loc.T("dialog.close")))
                    OpenFolder(App.Paths.BiosDir);
            }
            else await Dialogs.Info(this, Loc.T("play.error_title"), plan.Message);
            return;
        }

        if (item.Record.DiscCount >= 2 && App.Settings.ShowDiscChangeGuide && !App.Settings.UseUserDuckStation)
            await HelpWindow.ShowDiscGuide(this);

        // RetroArch ghi lại toàn bộ cấu hình khi thoát (kể cả giá trị từ --appendconfig của lần trước),
        // nên ghi lại cấu hình chuẩn trước mỗi lần chạy.
        try { RetroArchConfig.Write(App.Paths, App.Settings); }
        catch (Exception ex) { Log.Error("Không ghi được retroarch.cfg", ex); }

        // Bảo vệ save (docs/bao-toan-save.md): game trùng tên file với game khác lưu ở thư mục riêng,
        // nhận lại save từ tên cũ, sao lưu trước khi chơi. DuckStation tự quản lý save của nó.
        bool retroArch = !(App.Settings.UseUserDuckStation && item.Record.Platform == "PS1");
        var launchFile = item.Record.LaunchFile;
        var appendConfigs = new List<string>();
        SaveLocation? saves = null;
        if (retroArch)
        {
            var guard = new SaveGuard(App.Paths);
            try { saves = guard.Locate(item.Record, App.Db.GetGames(includeMissing: true), App.Catalog); }
            catch (Exception ex) { Log.Error("Không xác định được nơi lưu save", ex); }
            if (saves != null)
            {
                guard.PrepareForLaunch(App.Db, item.Record, saves);
                if (SaveGuard.AppendConfig(saves) is { } perGame)
                    appendConfigs.Add(WriteAppendConfig("save-dir.cfg", perGame));
            }
        }

        // Save state của giả lập: hỏi chơi tiếp hay chơi từ đầu (save trong game luôn được giữ)
        bool useStates = App.Settings.AskSaveStateOnExit && retroArch;
        var states = new SaveStateService(saves?.StatesDir ?? App.Paths.StatesDir);
        if (useStates && states.FindAutoState(launchFile) != null)
        {
            bool resume = await Dialogs.Confirm(this, item.Name, Loc.T("play.resume_text"),
                yes: Loc.T("play.resume_yes"), no: Loc.T("play.resume_no"));
            // Chỉ lần chạy này mới nạp save state
            if (resume) appendConfigs.Add(WriteAppendConfig("resume.cfg", "savestate_auto_load = \"true\"\n"));
        }
        EmulatorLauncher.AddAppendConfigs(plan.StartInfo!, appendConfigs);

        _gameRunning = true;
        Gamepad.Instance.Suspended = true;
        var startedUtc = DateTime.UtcNow;
        try
        {
            App.Db.MarkPlayed(item.Record.Id);
            var previous = useStates ? states.BackupBeforePlay(launchFile) : null;
            Hide();
            await launcher.RunAsync(plan.StartInfo!);
            BringToFront();
            if (useStates) await AskKeepSaveState(item, launchFile, states, startedUtc, previous);
        }
        catch (Exception ex)
        {
            Log.Error("Lỗi khi chạy game", ex);
            Show();
            await Dialogs.Info(this, Loc.T("play.error_title"), Loc.T("play.start_failed"));
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

    /// <summary>File cấu hình phụ chỉ áp dụng cho lần chạy này (nạp save state, thư mục save riêng...).</summary>
    private static string WriteAppendConfig(string name, string content)
    {
        var path = Path.Combine(App.Paths.ConfigDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Sau khi thoát game: hỏi có lưu lại chỗ đang chơi không. Bắt buộc chọn (Esc/B không đóng được).</summary>
    private async Task AskKeepSaveState(GameItem item, string launchFile, SaveStateService states, DateTime startedUtc, string? previous)
    {
        var state = states.NewStateSince(launchFile, startedUtc);
        if (state == null)
        {
            // Giả lập không chụp được (ví dụ bị tắt ngang): giữ nguyên bản cũ, bỏ bản sao lưu
            if (previous != null) SaveStateService.Keep(previous);
            return;
        }
        bool keep = await Dialogs.Confirm(this, Loc.T("play.keep_title"), Loc.T("play.keep_text", item.Name),
            yes: Loc.T("play.keep_yes"), no: Loc.T("play.keep_no"), cancellable: false);
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
            (Loc.T("more.rename"), () => Rename(item)),
            (Loc.T("more.open_folder"), () => { OpenFolder(item.Record.FolderPath); return Task.CompletedTask; }),
            (Loc.T("more.choose_platform"), () => ChoosePlatform(item)),
            (Loc.T("more.choose_cover"), () => ChooseCover(item)),
            (Loc.T(item.Record.IsFavorite ? "more.unfavorite" : "more.favorite"), () => { ToggleFavorite(item); return Task.CompletedTask; }),
            (Loc.T("more.restore_save"), () => RestoreSave(item)),
        };
        int i = await Dialogs.Choose(this, item.Name, null, options.Select(o => o.Label).ToList());
        if (i >= 0) await options[i].Run();
    }

    /// <summary>Khôi phục save từ các bản sao lưu tự động (tạo mỗi lần trước khi chơi).</summary>
    private async Task RestoreSave(GameItem item)
    {
        var guard = new SaveGuard(App.Paths);
        var saves = guard.Locate(item.Record, App.Db.GetGames(includeMissing: true), App.Catalog);
        var backups = guard.ListBackups(saves);
        if (backups.Count == 0)
        {
            await Dialogs.Info(this, Loc.T("restore.title"), Loc.T("restore.none"));
            return;
        }
        string When(SaveBackup b) => $"{Loc.Date(b.Time)}  {b.Time:HH:mm:ss}";
        int i = await Dialogs.Choose(this, Loc.T("restore.title"), Loc.T("restore.text"), backups.Select(When).ToList());
        if (i < 0) return;
        if (!await Dialogs.Confirm(this, Loc.T("restore.title"), Loc.T("restore.confirm", item.Name, When(backups[i])),
                yes: Loc.T("restore.yes"), no: Loc.T("dialog.cancel"))) return;
        try
        {
            guard.Restore(saves, backups[i]);
            await Dialogs.Info(this, Loc.T("restore.title"), Loc.T("restore.done"));
        }
        catch (Exception ex)
        {
            Log.Error("Khôi phục save lỗi", ex);
            await Dialogs.Info(this, Loc.T("settings.save_error_title"), ex.Message);
        }
    }

    private async Task Rename(GameItem item)
    {
        var name = await Dialogs.AskText(this, Loc.T("rename.title"), Loc.T("rename.text"), item.Name);
        if (name == null) return;
        App.Db.SetDisplayTitle(item.Record.Id, name == item.Record.Title ? null : name);
        Reload(item.Record.Id);
    }

    private async Task ChoosePlatform(GameItem item)
    {
        var platforms = App.Catalog.Platforms.Where(p => p.Enabled).ToList();
        int i = await Dialogs.Choose(this, Loc.T("platform.title"), Loc.T("platform.text", item.Name),
            platforms.Select(p => p.DisplayName).ToList());
        if (i < 0) return;
        App.Db.SetPlatform(item.Record.Id, platforms[i].Name);
        Reload(item.Record.Id);
    }

    private async Task ChooseCover(GameItem item)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("cover.title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("cover.images")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
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
        var settings = new SettingsWindow();
        await settings.ShowDialog(this);
        if (settings.ChosenLanguage != null) { App.SwitchLanguage(settings.ChosenLanguage); return; }
        ApplyLauncherFullscreen();
        UpdateControlUi();
        Reload();
    }

    private async void Help_Click(object? sender, RoutedEventArgs e) => await new HelpWindow().ShowDialog(this);

    /// <summary>Đổi sang ngôn ngữ kế tiếp (có 2 ngôn ngữ thì bấm là đổi ngay; nhiều hơn thì hỏi chọn).</summary>
    private async void Language_Click(object? sender, RoutedEventArgs e)
    {
        var others = Loc.Available.Where(a => a.Code != Loc.Language).ToList();
        if (others.Count == 0) return;
        int i = 0;
        if (others.Count > 1)
        {
            i = await Dialogs.Choose(this, Loc.T("settings.language"), null, others.Select(a => a.Name).ToList());
            if (i < 0) return;
        }
        App.SwitchLanguage(others[i].Code);
    }

    /// <summary>BIOS người dùng chép nhầm vào thư mục Games: chép sang thư mục BIOS (không hiện như game).</summary>
    private static void ImportBios(IReadOnlyList<string> biosFiles)
    {
        if (biosFiles.Count == 0) return;
        try
        {
            int n = Ps2Bios.ImportFromGames(biosFiles, App.Paths.BiosDir);
            if (n > 0) Log.Info($"Đã chép {n} file BIOS từ thư mục Games sang thư mục BIOS");
        }
        catch (Exception ex) { Log.Error("Không chép được BIOS từ thư mục Games", ex); }
    }

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
