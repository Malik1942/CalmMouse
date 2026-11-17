using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using static CalmMouseProbe.Win32;

namespace CalmMouseProbe;

internal sealed record MouseDevice(IntPtr Handle, string Path, ushort? Vid, ushort? Pid, string? Product)
{
    /// Same IDs the Mac app uses (DeviceIdentifier.swift): first generation, Lightning, USB-C.
    static readonly HashSet<ushort> MagicMousePids = [0x030D, 0x0269, 0x0323];
    static readonly HashSet<ushort> AppleVids = [0x05AC, 0x004C];

    public bool IsMagicMouse =>
        (Vid is { } v && AppleVids.Contains(v) && Pid is { } p && MagicMousePids.Contains(p))
        || (Product?.Contains("Magic Mouse", StringComparison.OrdinalIgnoreCase) ?? false);

    public string Label
    {
        get
        {
            var ids = Vid is { } v && Pid is { } p ? $"VID {v:X4} PID {p:X4}" : "no VID/PID";
            var name = string.IsNullOrWhiteSpace(Product) ? "(no name)" : Product;
            return $"{name} [{ids}]{(IsMagicMouse ? " ← Magic Mouse" : "")}";
        }
    }
}

[SupportedOSPlatform("windows")]
internal static unsafe class Devices
{
    public static List<MouseDevice> Mice()
    {
        uint count = 0;
        uint itemSize = (uint)sizeof(RAWINPUTDEVICELIST);
        GetRawInputDeviceList(null, ref count, itemSize);
        var items = new RAWINPUTDEVICELIST[count];
        fixed (RAWINPUTDEVICELIST* p = items)
            count = GetRawInputDeviceList(p, ref count, itemSize);
        var result = new List<MouseDevice>();
        if (count == uint.MaxValue) return result;
        for (int i = 0; i < count; i++)
            if (items[i].dwType == RIM_TYPEMOUSE)
                result.Add(Describe(items[i].hDevice));
        return result;
    }

    public static MouseDevice Describe(IntPtr handle)
    {
        var path = DeviceName(handle) ?? "";
        var (vid, pid) = ParseIds(path);
        string? product = null;
        if (path.Length > 0)
        {
            // Mice are opened exclusively by Windows; asking for no access still allows
            // reading the HID attributes and the product string.
            var h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != INVALID_HANDLE_VALUE && h != IntPtr.Zero)
            {
                var attrs = new HIDD_ATTRIBUTES { Size = (uint)sizeof(HIDD_ATTRIBUTES) };
                if (HidD_GetAttributes(h, ref attrs)) { vid ??= attrs.VendorID; pid ??= attrs.ProductID; }
                byte* buf = stackalloc byte[512];
                if (HidD_GetProductString(h, buf, 512))
                    product = new string((char*)buf).Trim();
                CloseHandle(h);
            }
        }
        return new MouseDevice(handle, path, vid, pid, product);
    }

    static string? DeviceName(IntPtr handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, null, ref chars);
        if (chars == 0) return null;
        var buf = new char[chars + 1];
        fixed (char* p = buf)
        {
            if ((int)GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, p, ref chars) < 0) return null;
            return new string(p);
        }
    }

    /// Handles both `VID_05AC&PID_0269` (USB) and `VID&0001004c_PID&0269` (Bluetooth) paths.
    static (ushort?, ushort?) ParseIds(string path)
    {
        var v = Regex.Match(path, @"VID[_&](?:000[12])?([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase);
        var p = Regex.Match(path, @"PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase);
        ushort? vid = v.Success ? Convert.ToUInt16(v.Groups[1].Value, 16) : null;
        ushort? pid = p.Success ? Convert.ToUInt16(p.Groups[1].Value, 16) : null;
        return (vid, pid);
    }
}

internal enum HookMode
{
    /// Hook and Raw Input both only watch.
    Observe,
    /// The hook drops every physical wheel event; the Raw Input handler sends it again
    /// with SendInput. This is design A in the spec (section 1.1).
    SwallowAndResend,
}

/// One timed recording: a low-level mouse hook plus Raw Input on a message-only window,
/// both on their own thread with its own message loop. Everything is removed when the
/// recording ends, and Windows removes the hook by itself if the process dies.
[SupportedOSPlatform("windows")]
internal sealed unsafe class MouseSession
{
    public readonly record struct HookEvent(double T, int Msg, short Delta, bool Injected, bool Ours, bool RawPending)
    {
        public bool IsWheel => Msg is WM_MOUSEWHEEL or WM_MOUSEHWHEEL;
    }

    public readonly record struct RawEvent(double T, IntPtr Device, ushort ButtonFlags, short Delta)
    {
        public bool IsWheel => (ButtonFlags & (RI_MOUSE_WHEEL | RI_MOUSE_HWHEEL)) != 0;
    }

    /// Marks events the probe itself injects ("CALM").
    public static readonly IntPtr Marker = new(0x43414C4D);

    public readonly HookMode Mode;
    public readonly List<HookEvent> Hook = new(4096);
    public readonly List<RawEvent> Raw = new(4096);
    public readonly List<double> Resent = new(1024);
    public readonly List<string> Errors = [];
    public int LiveWheelCount;

    uint _threadId;
    IntPtr _hook, _hwnd;
    HookProc? _hookProc;
    WndProc? _wndProc;
    readonly ManualResetEventSlim _ready = new();

    MouseSession(HookMode mode) { Mode = mode; }

    public static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static MouseSession Record(HookMode mode, int seconds, Func<bool> abort)
    {
        var s = new MouseSession(mode);
        var thread = new Thread(s.ThreadMain) { IsBackground = true, Name = "hook" };
        thread.Start();
        s._ready.Wait();
        if (s.Errors.Count == 0)
        {
            for (int left = seconds; left > 0 && !abort(); left--)
            {
                Console.Write($"\r  还剩 {left,2} 秒 · 已记录滚动 {Volatile.Read(ref s.LiveWheelCount)} 次   ");
                for (int i = 0; i < 10 && !abort(); i++) Thread.Sleep(100);
            }
            Console.WriteLine("\r  录制结束。                                   ");
        }
        PostThreadMessageW(s._threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(3000);
        return s;
    }

    void ThreadMain()
    {
        _threadId = GetCurrentThreadId();
        var hInst = GetModuleHandleW(null);
        try
        {
            _wndProc = WindowProc;
            var className = "CalmMouseProbe" + Guid.NewGuid().ToString("N");
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInst,
                lpszClassName = className,
            };
            if (RegisterClassExW(ref wc) == 0) Errors.Add($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
            _hwnd = CreateWindowExW(0, className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) Errors.Add($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

            var rid = new[] { new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd } };
            if (!RegisterRawInputDevices(rid, 1, (uint)sizeof(RAWINPUTDEVICE)))
                Errors.Add($"RegisterRawInputDevices failed: {Marshal.GetLastPInvokeError()}");

            _hookProc = HookCallback;
            _hook = SetWindowsHookExW(WH_MOUSE_LL, _hookProc, hInst, 0);
            if (_hook == IntPtr.Zero) Errors.Add($"SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}");
        }
        catch (Exception e) { Errors.Add(e.ToString()); }
        _ready.Set();

        try
        {
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        finally
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            // RIDEV_REMOVE (0x1) with no target window.
            RegisterRawInputDevices([new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = 0x1 }], 1, (uint)sizeof(RAWINPUTDEVICE));
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        }
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_MOUSEWHEEL or WM_MOUSEHWHEEL or WM_LBUTTONDOWN or WM_LBUTTONUP
                or WM_RBUTTONDOWN or WM_RBUTTONUP or WM_MBUTTONDOWN or WM_MBUTTONUP)
            {
                var s = (MSLLHOOKSTRUCT*)lParam;
                bool injected = (s->flags & LLMHF_INJECTED) != 0;
                bool ours = s->dwExtraInfo == Marker;
                // Is a WM_INPUT already waiting in our queue while the hook runs? (HIWORD = now queued.)
                bool rawPending = ((GetQueueStatus(QS_RAWINPUT) >> 16) & QS_RAWINPUT) != 0;
                var e = new HookEvent(Now(), msg, (short)(s->mouseData >> 16), injected, ours, rawPending);
                Hook.Add(e);
                if (e.IsWheel && !injected)
                {
                    Interlocked.Increment(ref LiveWheelCount);
                    if (Mode == HookMode.SwallowAndResend) return 1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT) ReadRaw(lParam);
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    void ReadRaw(IntPtr hRaw)
    {
        uint headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        GetRawInputData(hRaw, RID_INPUT, null, ref size, headerSize);
        if (size == 0 || size > 1024) return;
        byte* buf = stackalloc byte[(int)size];
        if (GetRawInputData(hRaw, RID_INPUT, buf, ref size, headerSize) != size) return;
        if (*(uint*)buf != RIM_TYPEMOUSE) return;
        var device = *(IntPtr*)(buf + 8);
        byte* mouse = buf + headerSize;
        ushort flags = *(ushort*)(mouse + 4);
        short data = *(short*)(mouse + 6);
        if ((flags & 0x0FFF) == 0) return; // movement only
        var e = new RawEvent(Now(), device, flags, data);
        Raw.Add(e);

        // Send real devices' wheel events back in. Injected input (ours included) arrives
        // here with no device, which is also what stops this from looping.
        if (Mode == HookMode.SwallowAndResend && e.IsWheel && device != IntPtr.Zero)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    mouseData = (uint)(int)data,
                    dwFlags = (flags & RI_MOUSE_WHEEL) != 0 ? MOUSEEVENTF_WHEEL : MOUSEEVENTF_HWHEEL,
                    dwExtraInfo = Marker,
                },
            };
            if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1) Resent.Add(Now());
            else if (Errors.Count < 5) Errors.Add($"SendInput failed: {Marshal.GetLastPInvokeError()}");
        }
    }
}
