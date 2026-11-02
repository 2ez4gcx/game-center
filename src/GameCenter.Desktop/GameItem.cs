using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GameCenter.Core.Data;
using GameCenter.Core.Localization;
using GameCenter.Core.Scanning;

namespace GameCenter.Desktop;

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
        IsUnknown ? Loc.T("tab.unknown") : App.Catalog.Get(Record.Platform)?.DisplayName ?? Record.Platform;

    public string PlatformShort => Record.Platform switch
    {
        "MegaDrive" => "MD",
        "MasterSystem" => "SMS",
        "GameGear" => "GG",
        GameDatabase.UnknownPlatform => "?",
        _ => Record.Platform,
    };

    /// <summary>Màu nhận diện từng hệ máy.</summary>
    public Color PlatformColor => Record.Platform switch
    {
        "NES" => Color.FromRgb(0xFF, 0x52, 0x52),
        "SNES" => Color.FromRgb(0xB3, 0x88, 0xFF),
        "GB" => Color.FromRgb(0x9C, 0xCC, 0x65),
        "GBC" => Color.FromRgb(0x1D, 0xE9, 0xB6),
        "GBA" => Color.FromRgb(0x7C, 0x8C, 0xFF),
        "MasterSystem" => Color.FromRgb(0xFF, 0x6E, 0x40),
        "GameGear" => Color.FromRgb(0x26, 0xC6, 0xDA),
        "MegaDrive" => Color.FromRgb(0x40, 0xC4, 0xFF),
        "PS1" => Color.FromRgb(0xFF, 0xD7, 0x40),
        "PS2" => Color.FromRgb(0x44, 0x8A, 0xFF),
        _ => Color.FromRgb(0xFF, 0x98, 0x00),
    };

    public IBrush PlatformBrush => new SolidColorBrush(PlatformColor);
    public IBrush PlatformBrushFaint => new SolidColorBrush(Color.FromArgb(0x26, PlatformColor.R, PlatformColor.G, PlatformColor.B));
    public IBrush TileBrush => new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(PlatformColor, 0), new GradientStop(Color.FromRgb(0x0B, 0x13, 0x28), 1.1) },
    };
    public IBrush FavoriteBrush => Record.IsFavorite ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x40)) : Brushes.White;

    /// <summary>Thông tin phụ (không gồm tên hệ máy, đã có chip riêng).</summary>
    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (Record.DiscCount >= 2) parts.Add(Loc.T("game.discs", Record.DiscCount));
            if (Record.PlayCount > 0) parts.Add(Loc.Plural("game.played", Record.PlayCount));
            if (Record.LastPlayedAt != null && DateTime.TryParse(Record.LastPlayedAt, out var d))
                parts.Add(Loc.T("game.last_played", Loc.Date(d)));
            return string.Join("  ·  ", parts);
        }
    }

    public string? Warning => Record.ScanStatus switch
    {
        ScanStatus.BrokenCue => "⚠ " + (Record.ScanMessage ?? Loc.T("game.file_missing")),
        // File nén cần giải nén, máy chưa hỗ trợ...: hiện đúng lý do; còn lại nhắc chọn hệ máy
        ScanStatus.Unknown => Record.ScanMessage ?? Loc.T("game.unknown_hint"),
        // Chơi được nhưng cần lưu ý (ví dụ bộ nhiều đĩa thiếu đĩa)
        ScanStatus.Ok when Record.ScanMessage != null => "⚠ " + Record.ScanMessage,
        _ => null,
    };

    public bool HasWarning => Warning != null;
    public string FavoriteIcon => Record.IsFavorite ? "★" : "☆";
    public string PlayLabel => Loc.T(IsUnknown ? "game.choose_platform" : "game.play");

    private Bitmap? _cover;
    private bool _coverLoaded;

    /// <summary>Ảnh bìa: cover_path, hoặc Covers/&lt;Platform&gt;/&lt;tên&gt;.png|jpg.</summary>
    public Bitmap? Cover
    {
        get
        {
            if (_coverLoaded) return _cover;
            _coverLoaded = true;
            var path = Record.CoverPath;
            if (path == null || !File.Exists(path))
            {
                var dir = Path.Combine(App.Paths.CoversDir, Record.Platform);
                path = new[] { Record.Title, Path.GetFileNameWithoutExtension(GameScanner.PhysicalPath(Record.SourceFile)) }
                    .SelectMany(n => new[] { ".png", ".jpg", ".jpeg" }.Select(e => Path.Combine(dir, n + e)))
                    .FirstOrDefault(File.Exists);
            }
            if (path == null) return null;
            try
            {
                using var fs = File.OpenRead(path); // đọc hết rồi đóng, không khóa file
                _cover = Bitmap.DecodeToWidth(fs, 176);
            }
            catch { _cover = null; }
            return _cover;
        }
    }

    public bool HasCover => Cover != null;
}
