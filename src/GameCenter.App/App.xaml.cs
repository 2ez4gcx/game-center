using System.Windows;
using System.Windows.Threading;
using GameCenter.Core.Config;
using GameCenter.Core.Data;
using GameCenter.Core.Emulation;
using GameCenter.Core.Util;

namespace GameCenter.App;

public partial class App : Application
{
    public static AppPaths Paths { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static PlatformCatalog Catalog { get; private set; } = null!;
    public static GameDatabase Db { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        try
        {
            Paths = new AppPaths(AppContext.BaseDirectory, AppPaths.ResolveDataDir(AppContext.BaseDirectory));
            Paths.EnsureDataFolders();
            Log.Init(Paths.LogFile);
            Log.Info($"Khởi động. AppDir={Paths.AppDir} DataDir={Paths.DataDir}");

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
            MessageBox.Show("Game Center không khởi động được.\n\n" + ex.Message, "Game Center", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Lỗi không mong muốn", e.Exception);
        Dialogs.Info(MainWindow, "Đã xảy ra lỗi", "Có lỗi xảy ra. Chi tiết đã được ghi vào file log trong thư mục Logs.");
        e.Handled = true;
    }
}
