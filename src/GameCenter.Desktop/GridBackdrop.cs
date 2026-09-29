using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GameCenter.Desktop;

/// <summary>Lưới mờ trang trí nền (vẽ trực tiếp, chạy giống nhau trên mọi hệ điều hành).</summary>
public sealed class GridBackdrop : Control
{
    private static readonly IPen Pen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), 1);
    private const double Cell = 48;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        for (double x = 0; x <= size.Width; x += Cell)
            context.DrawLine(Pen, new Point(x, 0), new Point(x, size.Height));
        for (double y = 0; y <= size.Height; y += Cell)
            context.DrawLine(Pen, new Point(0, y), new Point(size.Width, y));
    }
}
