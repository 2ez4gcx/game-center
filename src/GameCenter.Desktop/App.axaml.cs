using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Localization;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

public partial class App : Application
{
    public static AppPaths Paths { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static PlatformCatalog Catalog { get; private set; } = null!;
    public static GameDatabase Db { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Log.Error("Lỗi không mong muốn", e.Exception);
                e.Handled = true;
                if (desktop.MainWindow is { } w)
                    _ = Dialogs.Info(w, Loc.T("app.error_title"), Loc.T("app.error_text"));
            };

            string? error = null;
            try
            {
                Paths = new AppPaths(AppContext.BaseDirectory, AppPaths.ResolveDataDir(AppContext.BaseDirectory));
                Paths.EnsureDataFolders();
                Log.Init(Paths.LogFile);
                Log.Info($"Khởi động {OsPlatform.Current}. AppDir={Paths.AppDir} DataDir={Paths.DataDir}");

                bool hadSettings = File.Exists(Paths.SettingsJson);
                Settings = AppSettings.Load(Paths.SettingsJson);
                // Lần đầu chạy bản có nhiều ngôn ngữ: chọn theo bộ cài / bản cũ / hệ điều hành
                if (!Loc.IsAvailable(Settings.Language))
                {
                    Settings.Language = Loc.DetectDefault(Paths.InstallerIni, upgradedFromOldVersion: hadSettings);
                    Settings.Save(Paths.SettingsJson);
                }
                Loc.Use(Settings.Language);
                Log.Info($"Ngôn ngữ: {Loc.Language}");

                Catalog = PlatformCatalog.Load(Paths.PlatformsJson);
                RetroArchConfig.Write(Paths, Settings);

                Db = new GameDatabase(Paths.DatabaseFile);
                Db.Initialize(Catalog);
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi khởi động", ex);
                error = ex.Message;
            }

            desktop.MainWindow = error == null ? new MainWindow() : Dialogs.StartupError(error);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Đổi ngôn ngữ giao diện ngay: lưu cài đặt, ghi lại retroarch.cfg (menu RetroArch cùng ngôn ngữ),
    /// rồi tạo lại cửa sổ chính với chuỗi mới.
    /// </summary>
    public static void SwitchLanguage(string code)
    {
        if (code == Loc.Language) return;
        Settings.Language = code;
        Loc.Use(code);
        try
        {
            Settings.Save(Paths.SettingsJson);
            RetroArchConfig.Write(Paths, Settings);
        }
        catch (Exception ex) { Log.Error("Không lưu được ngôn ngữ", ex); }
        Log.Info($"Đổi ngôn ngữ: {code}");
        ReloadMainWindow();
    }

    /// <summary>Tạo lại cửa sổ chính (giữ vị trí, kích thước). Mở cửa sổ mới trước rồi mới đóng cửa sổ cũ để app không thoát.</summary>
    private static void ReloadMainWindow()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not MainWindow old) return;
        var w = new MainWindow { Width = old.Width, Height = old.Height, WindowStartupLocation = WindowStartupLocation.Manual, Position = old.Position };
        if (old.WindowState == WindowState.Maximized) w.WindowState = WindowState.Maximized;
        desktop.MainWindow = w;
        w.Show();
        old.Close();
    }
}
