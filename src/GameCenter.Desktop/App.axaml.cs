using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
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
                    _ = Dialogs.Info(w, "Đã xảy ra lỗi", "Có lỗi xảy ra. Chi tiết đã được ghi vào file log trong thư mục Logs.");
            };

            string? error = null;
            try
            {
                Paths = new AppPaths(AppContext.BaseDirectory, AppPaths.ResolveDataDir(AppContext.BaseDirectory));
                Paths.EnsureDataFolders();
                Log.Init(Paths.LogFile);
                Log.Info($"Khởi động {OsPlatform.Current}. AppDir={Paths.AppDir} DataDir={Paths.DataDir}");

                Catalog = PlatformCatalog.Load(Paths.PlatformsJson);
                Settings = AppSettings.Load(Paths.SettingsJson);
                if (!File.Exists(Paths.SettingsJson)) Settings.Save(Paths.SettingsJson);
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
}
