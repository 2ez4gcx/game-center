using Avalonia.Threading;
using GameCenter.Core.Util;
using static SDL2.SDL;

namespace GameCenter.Desktop;

public enum PadButton { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back }

/// <summary>
/// Đọc tay cầm để điều khiển launcher, dùng SDL2 (Windows / macOS / Linux, nhận tay cầm Xbox,
/// PlayStation, Switch...). Một bộ đọc chung cho cả app; từng cửa sổ tự kiểm tra mình có đang được chọn.
/// Thiếu thư viện SDL2 thì tắt êm, app vẫn dùng chuột và bàn phím.
/// Trong game RetroArch tự xử lý tay cầm.
/// </summary>
public sealed class Gamepad
{
    public static Gamepad Instance { get; } = new();

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, IntPtr> _controllers = new();
    private readonly Dictionary<PadButton, DateTime> _heldSince = new();
    private readonly Dictionary<PadButton, DateTime> _lastRepeat = new();
    private bool _available;

    public event Action<PadButton>? Pressed;
    public event Action<bool>? ConnectionChanged;
    public bool IsConnected => _controllers.Count > 0;
    /// <summary>Tạm ngừng phát sự kiện (ví dụ khi đang chơi game).</summary>
    public bool Suspended { get; set; }

    private static readonly (SDL_GameControllerButton Sdl, PadButton Pad)[] Map =
    {
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP, PadButton.Up),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN, PadButton.Down),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT, PadButton.Left),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT, PadButton.Right),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A, PadButton.A),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B, PadButton.B),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X, PadButton.X),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y, PadButton.Y),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER, PadButton.LB),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, PadButton.RB),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START, PadButton.Start),
        (SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK, PadButton.Back),
    };

    private Gamepad()
    {
        try
        {
            SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
            _available = SDL_Init(SDL_INIT_GAMECONTROLLER) == 0;
            if (!_available) Log.Warn("SDL2 không khởi tạo được tay cầm: " + SDL_GetError());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Log.Warn("Thiếu thư viện SDL2, tắt điều khiển launcher bằng tay cầm: " + ex.Message);
        }

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => Poll());
        if (_available) _timer.Start();
    }

    private void Poll()
    {
        // Nhận tay cầm cắm / rút
        while (SDL_PollEvent(out var ev) == 1)
        {
            if (ev.type == SDL_EventType.SDL_CONTROLLERDEVICEADDED) Open(ev.cdevice.which);
            else if (ev.type == SDL_EventType.SDL_CONTROLLERDEVICEREMOVED) Close(ev.cdevice.which);
        }

        if (Suspended || _controllers.Count == 0) { _heldSince.Clear(); return; }

        var down = new HashSet<PadButton>();
        foreach (var c in _controllers.Values)
        {
            foreach (var (sdl, pad) in Map)
                if (SDL_GameControllerGetButton(c, sdl) == 1) down.Add(pad);
            const short dz = 16000;
            short lx = SDL_GameControllerGetAxis(c, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
            short ly = SDL_GameControllerGetAxis(c, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);
            if (ly < -dz) down.Add(PadButton.Up);
            if (ly > dz) down.Add(PadButton.Down);
            if (lx < -dz) down.Add(PadButton.Left);
            if (lx > dz) down.Add(PadButton.Right);
        }

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

    private void Open(int deviceIndex)
    {
        if (SDL_IsGameController(deviceIndex) != SDL_bool.SDL_TRUE) return;
        var c = SDL_GameControllerOpen(deviceIndex);
        if (c == IntPtr.Zero) return;
        int id = SDL_JoystickInstanceID(SDL_GameControllerGetJoystick(c));
        _controllers[id] = c;
        Log.Info($"Tay cầm đã cắm: {SDL_GameControllerName(c)}");
        ConnectionChanged?.Invoke(true);
    }

    private void Close(int instanceId)
    {
        if (!_controllers.Remove(instanceId, out var c)) return;
        SDL_GameControllerClose(c);
        ConnectionChanged?.Invoke(IsConnected);
    }
}
