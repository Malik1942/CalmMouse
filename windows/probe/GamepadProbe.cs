using SDL;
using static SDL.SDL3;

namespace CalmMouseProbe;

/// Phase 0 question S4: can SDL3 read the DualSense touchpad (USB and Bluetooth), and does
/// input keep arriving while another window is in front?
internal static unsafe class GamepadProbe
{
    sealed class Pad
    {
        public SDL_Gamepad* Handle;
        public string Name = "";
        public int TouchFrames, TouchFramesBackground, MaxFingers;
        public float MinX = 1, MaxX = 0, MinY = 1, MaxY = 0;
        public int InputFramesForeground, InputFramesBackground;
        public readonly double[] MaxAxis = new double[4];
        public readonly SortedSet<string> Buttons = [];
    }

    public static void Run(Report r, int seconds, Func<bool> abort)
    {
        // Must be set before SDL_Init. Enhanced reports are what carry the touchpad over
        // Bluetooth; once on, the controller keeps them until it is switched off.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        SDL_SetHint("SDL_JOYSTICK_ENHANCED_REPORTS", "1");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            r.Line($"SDL_Init 失败: {SDL_GetError()}");
            return;
        }
        var pads = new Dictionary<uint, Pad>();
        try
        {
            while (true)
            {
                Pump(500);
                OpenNew(pads, r);
                if (pads.Count > 0) break;
                var answer = Report.Ask("没找到手柄。连好（USB 线或蓝牙）后按回车再找一次；输入 s 跳过这一步：");
                if (Report.EndOfInput || abort() || answer.Trim().Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    r.Line("（没有手柄，跳过）");
                    return;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"  接下来 {seconds} 秒：");
            Console.WriteLine("  · 在触控板上用一根手指滑动，再用两根手指滑动，再按下触控板；");
            Console.WriteLine("  · 推一推两根摇杆，按几个键；");
            Console.WriteLine("  · 中途用鼠标点一下桌面或别的窗口（让这个窗口不在前台），继续在触控板上滑几秒。");
            Report.Ask("准备好了按回车开始：");

            var foreground = Platform.ForegroundWindow();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            int lastShown = -1;
            while (DateTime.UtcNow < end && !abort())
            {
                Pump(0);
                OpenNew(pads, r);
                bool inFront = foreground == IntPtr.Zero || Platform.ForegroundWindow() == foreground;
                foreach (var pad in pads.Values) Sample(pad, inFront);
                int left = (int)Math.Ceiling((end - DateTime.UtcNow).TotalSeconds);
                if (left != lastShown)
                {
                    lastShown = left;
                    var touches = pads.Values.Sum(p => p.TouchFrames);
                    Console.Write($"\r  还剩 {left,2} 秒 · 触控板帧 {touches} · 窗口{(inFront ? "在前台" : "在后台")}   ");
                }
                SDL_Delay(8);
            }
            Console.WriteLine("\r  录制结束。                                              ");

            foreach (var pad in pads.Values)
            {
                r.Line($"手柄：{pad.Name}");
                r.Line($"  触控板有手指的帧数：{pad.TouchFrames}（其中窗口在后台时 {pad.TouchFramesBackground}）；同时最多 {pad.MaxFingers} 指");
                if (pad.TouchFrames > 0)
                    r.Line($"  触控板坐标范围：x {pad.MinX:0.00}–{pad.MaxX:0.00}，y {pad.MinY:0.00}–{pad.MaxY:0.00}");
                r.Line($"  摇杆最大偏移：左 ({pad.MaxAxis[0]:0.00}, {pad.MaxAxis[1]:0.00}) 右 ({pad.MaxAxis[2]:0.00}, {pad.MaxAxis[3]:0.00})");
                r.Line($"  按过的键：{(pad.Buttons.Count == 0 ? "无" : string.Join(" ", pad.Buttons))}");
                r.Line($"  有输入的帧：前台 {pad.InputFramesForeground}，后台 {pad.InputFramesBackground}");
            }
        }
        finally
        {
            foreach (var pad in pads.Values) SDL_CloseGamepad(pad.Handle);
            SDL_Quit();
        }
    }

    static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            SDL_Event e;
            while (SDL_PollEvent(&e)) { }
            if (ms > 0) SDL_Delay(10);
        } while (DateTime.UtcNow < until);
    }

    static void OpenNew(Dictionary<uint, Pad> pads, Report r)
    {
        int count;
        var ids = SDL_GetGamepads(&count);
        if (ids == null) return;
        for (int i = 0; i < count; i++)
        {
            var id = ids[i];
            uint key = *(uint*)&id;
            if (pads.ContainsKey(key)) continue;
            var handle = SDL_OpenGamepad(id);
            if (handle == null) { r.Line($"打不开手柄 {key}: {SDL_GetError()}"); continue; }
            int percent;
            var power = SDL_GetGamepadPowerInfo(handle, &percent);
            var pad = new Pad { Handle = handle, Name = SDL_GetGamepadName(handle) ?? "(no name)" };
            pads[key] = pad;
            var connection = SDL_GetGamepadConnectionState(handle) switch
            {
                SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRED => "有线",
                SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRELESS => "无线",
                _ => "未知",
            };
            r.Line($"找到手柄：{pad.Name} · VID {SDL_GetGamepadVendor(handle):X4} PID {SDL_GetGamepadProduct(handle):X4} · " +
                   $"连接：{connection} · 电量：{power} {(percent >= 0 ? percent + "%" : "")} · 触控板数：{SDL_GetNumGamepadTouchpads(handle)}");
        }
        SDL_free(ids);
    }

    static void Sample(Pad pad, bool inFront)
    {
        var h = pad.Handle;
        bool any = false;
        int fingersNow = 0;
        int touchpads = SDL_GetNumGamepadTouchpads(h);
        for (int t = 0; t < touchpads; t++)
        {
            int fingers = SDL_GetNumGamepadTouchpadFingers(h, t);
            for (int f = 0; f < fingers; f++)
            {
                SDLBool down;
                float x, y, pressure;
                if (!SDL_GetGamepadTouchpadFinger(h, t, f, &down, &x, &y, &pressure) || !down) continue;
                fingersNow++;
                pad.MinX = Math.Min(pad.MinX, x); pad.MaxX = Math.Max(pad.MaxX, x);
                pad.MinY = Math.Min(pad.MinY, y); pad.MaxY = Math.Max(pad.MaxY, y);
            }
        }
        if (fingersNow > 0)
        {
            any = true;
            pad.TouchFrames++;
            if (!inFront) pad.TouchFramesBackground++;
            pad.MaxFingers = Math.Max(pad.MaxFingers, fingersNow);
        }
        var axes = new[]
        {
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY,
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY,
        };
        for (int i = 0; i < axes.Length; i++)
        {
            double v = Math.Abs(SDL_GetGamepadAxis(h, axes[i]) / 32767.0);
            pad.MaxAxis[i] = Math.Max(pad.MaxAxis[i], v);
            if (v > 0.3) any = true;
        }
        for (var b = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH; b < SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT; b++)
        {
            if (!SDL_GetGamepadButton(h, b)) continue;
            any = true;
            pad.Buttons.Add(b.ToString().Replace("SDL_GAMEPAD_BUTTON_", "").ToLowerInvariant());
        }
        if (!any) return;
        if (inFront) pad.InputFramesForeground++; else pad.InputFramesBackground++;
    }
}
