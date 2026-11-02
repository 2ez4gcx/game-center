using Avalonia.Markup.Xaml;
using GameCenter.Core.Localization;

namespace GameCenter.Desktop;

/// <summary>
/// Chuỗi giao diện theo ngôn ngữ đang chọn, dùng trong XAML: Text="{local:T main.scan}".
/// Đổi ngôn ngữ thì cửa sổ chính được tạo lại (App.ReloadMainWindow) nên không cần cập nhật trực tiếp.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
