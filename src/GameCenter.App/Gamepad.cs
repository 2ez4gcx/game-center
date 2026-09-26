using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace GameCenter.App;

public enum PadButton { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back }

/// <summary>
/// Đọc tay cầm Xbox/XInput để điều khiển launcher (mục 12): lên/xuống chọn game, A chơi, LB/RB đổi tab.
/// Chỉ dùng cho launcher; trong game RetroArch tự xử lý tay cầm.
/// </summary>
public sealed class Gamepad : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint user, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint user, out XInputState state);

    private static bool _use910;
    private static bool _unavailable;

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<PadButton, DateTime> _heldSince = new();
    private readonly Dictionary<PadButton, DateTime> _lastRepeat = new();
    private int _idleTicks;

    public event Action<PadButton>? Pressed;
    /// <summary>Chỉ đọc tay cầm khi hàm này trả về true (ví dụ cửa sổ đang được chọn, không có game đang chạy).</summary>
    public Func<bool> IsEnabled { get; set; } = () => true;

    public Gamepad()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private static bool TryRead(uint user, out XInputState s)
    {
        s = default;
        if (_unavailable) return false;
        try
        {
            return (_use910 ? GetState910(user, out s) : GetState14(user, out s)) == 0;
        }
        catch (DllNotFoundException) when (!_use910)
        {
            _use910 = true;
            return TryRead(user, out s);
        }
        catch (Exception)
        {
            _unavailable = true;
            return false;
        }
    }

    private void Poll()
    {
        // Không có tay cầm thì đọc thưa hơn cho nhẹ máy
        if (_idleTicks > 0) { _idleTicks--; return; }
        if (!IsEnabled()) { _heldSince.Clear(); return; }

        var down = new HashSet<PadButton>();
        bool any = false;
        for (uint i = 0; i < 4; i++)
        {
            if (!TryRead(i, out var s)) continue;
            any = true;
            var g = s.Gamepad;
            void Map(ushort mask, PadButton b) { if ((g.wButtons & mask) != 0) down.Add(b); }
            Map(0x0001, PadButton.Up); Map(0x0002, PadButton.Down); Map(0x0004, PadButton.Left); Map(0x0008, PadButton.Right);
            Map(0x0010, PadButton.Start); Map(0x0020, PadButton.Back);
            Map(0x0100, PadButton.LB); Map(0x0200, PadButton.RB);
            Map(0x1000, PadButton.A); Map(0x2000, PadButton.B); Map(0x4000, PadButton.X); Map(0x8000, PadButton.Y);
            const short dz = 16000;
            if (g.sThumbLY > dz) down.Add(PadButton.Up);
            if (g.sThumbLY < -dz) down.Add(PadButton.Down);
            if (g.sThumbLX < -dz) down.Add(PadButton.Left);
            if (g.sThumbLX > dz) down.Add(PadButton.Right);
        }
        if (!any) { _idleTicks = 25; _heldSince.Clear(); return; }

        var now = DateTime.UtcNow;
        foreach (var b in _heldSince.Keys.Where(k => !down.Contains(k)).ToList()) _heldSince.Remove(b);
        foreach (var b in down)
        {
            if (!_heldSince.ContainsKey(b))
            {
                _heldSince[b] = now;
                _lastRepeat[b] = now;
                Pressed?.Invoke(b);
            }
            else if (b is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right
                     && now - _heldSince[b] > TimeSpan.FromMilliseconds(450)
                     && now - _lastRepeat[b] > TimeSpan.FromMilliseconds(120))
            {
                _lastRepeat[b] = now;
                Pressed?.Invoke(b); // giữ để cuộn nhanh
            }
        }
    }

    public void Dispose() => _timer.Stop();
}
