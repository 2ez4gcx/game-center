using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameCenter.Core.Data;
using GameCenter.Core.Scanning;

namespace GameCenter.App;

/// <summary>Một dòng trong danh sách game.</summary>
public sealed class GameItem : INotifyPropertyChanged
{
    public GameRecord Record { get; }
    public GameItem(GameRecord record) => Record = record;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public bool IsUnknown => Record.Platform == GameDatabase.UnknownPlatform;
    public string Name => Record.Name;

    public string PlatformName =>
        IsUnknown ? "Chưa nhận ra" : App.Catalog.Get(Record.Platform)?.DisplayName ?? Record.Platform;

    public string PlatformShort => IsUnknown ? "?" : PlatformName;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { PlatformName };
            if (Record.DiscCount >= 2) parts.Add($"{Record.DiscCount} đĩa");
            if (Record.LastPlayedAt != null && DateTime.TryParse(Record.LastPlayedAt, out var d))
                parts.Add($"chơi lần cuối {d:dd/MM/yyyy}");
            return string.Join("  •  ", parts);
        }
    }

    public string? Warning => Record.ScanStatus switch
    {
        ScanStatus.BrokenCue => "⚠ " + (Record.ScanMessage ?? "File game bị thiếu."),
        ScanStatus.Unknown => "Bấm \"Chọn hệ máy\" để cho biết game này của máy nào.",
        _ => null,
    };

    public Visibility WarningVisibility => Warning == null ? Visibility.Collapsed : Visibility.Visible;
    public string FavoriteIcon => Record.IsFavorite ? "★" : "☆";
    public string PlayLabel => IsUnknown ? "Chọn hệ máy" : "▶  Chơi";

    private ImageSource? _cover;
    private bool _coverLoaded;

    /// <summary>Ảnh bìa: cover_path, hoặc Covers/&lt;Platform&gt;/&lt;tên&gt;.png|jpg.</summary>
    public ImageSource? Cover
    {
        get
        {
            if (_coverLoaded) return _cover;
            _coverLoaded = true;
            var path = Record.CoverPath;
            if (path == null || !File.Exists(path))
            {
                var dir = Path.Combine(App.Paths.CoversDir, Record.Platform);
                path = new[] { Record.Title, Path.GetFileNameWithoutExtension(Record.SourceFile) }
                    .SelectMany(n => new[] { ".png", ".jpg", ".jpeg" }.Select(e => Path.Combine(dir, n + e)))
                    .FirstOrDefault(File.Exists);
            }
            if (path == null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // không khóa file
                bmp.DecodePixelWidth = 168;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                _cover = bmp;
            }
            catch { _cover = null; }
            return _cover;
        }
    }

    public void ResetCover() { _coverLoaded = false; _cover = null; }
}
