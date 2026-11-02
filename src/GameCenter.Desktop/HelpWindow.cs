using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using GameCenter.Core.Config;
using GameCenter.Core.Localization;
using GameCenter.Core.Util;

namespace GameCenter.Desktop;

/// <summary>Trợ giúp: thoát game, đổi đĩa, tay cầm, PS2, BIOS, giấy phép, giới thiệu.</summary>
public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = Loc.T("help.title");
        Width = 900;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dialogs.AttachGamepad(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var root = new StackPanel { Margin = new Thickness(32) };
        root.Children.Add(Dialogs.Heading(Loc.T("help.title")));

        Add(root, "help.exit_title", "help.exit_text");
        Add(root, "help.play_title", "help.play_text");
        Add(root, "help.pad_title", "help.pad_text");
        Add(root, "help.disc_title", "help.disc_text");
        Add(root, "help.ps2_title", "help.ps2_text");

        Add(root, "help.bios_title", "help.bios_text");
        root.Children.Add(Dialogs.Row(Dialogs.Button(Loc.T("settings.open_bios"), (_, _) => MainWindow.OpenFolder(App.Paths.BiosDir))));

        Add(root, "help.errors_title", "help.errors_text");
        root.Children.Add(Dialogs.Row(Dialogs.Button(Loc.T("help.open_logs"), (_, _) => MainWindow.OpenFolder(App.Paths.LogsDir))));

        root.Children.Add(Dialogs.Section(Loc.T("help.about_title")));
        root.Children.Add(Dialogs.Para(Loc.T("help.about_text", AppInfo.Version, AppInfo.Author, AppInfo.Contact), 21));

        // --- Giấy phép (mục 17.2: ghi rõ thành phần nguồn mở)
        Add(root, "help.license_title", "help.license_text");
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

        var close = Dialogs.Button(Loc.T("dialog.close"), (_, _) => Close(), primary: true);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 24, 0, 0);
        root.Children.Add(close);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += (_, _) => close.Focus(NavigationMethod.Directional);
    }

    private static void Add(Panel root, string titleKey, string textKey)
    {
        root.Children.Add(Dialogs.Section(Loc.T(titleKey)));
        root.Children.Add(Dialogs.Para(Loc.T(textKey), 21));
    }

    private static void OpenText(string file)
    {
        try { OsPlatform.OpenFile(file); }
        catch (Exception ex) { Log.Error("Không mở được file giấy phép", ex); }
    }

    /// <summary>Hướng dẫn đổi đĩa ngắn, hiện khi chạy game nhiều đĩa (mục 8.3).</summary>
    public static Task ShowDiscGuide(Window owner) =>
        Dialogs.Info(owner, Loc.T("help.disc_dialog_title"), Loc.T("help.disc_text"));
}
