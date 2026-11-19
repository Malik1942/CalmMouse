using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CalmMouse.Win.Tests.Input;

/// <summary>
/// A second low-level mouse hook, installed before the engine's, on its own thread. Windows calls
/// the newest hook first, so this one sees an event only if the engine's hook let it through: it
/// is how the tests tell "passed" from "swallowed".
/// </summary>
/// <remarks>
/// It also keeps the tests from touching the owner's PC: injected events carrying one of its
/// markers (and at most a 1-unit wheel delta) are recorded and then swallowed, so no app ever
/// sees them. Everything else passes. Windows drops zero-delta wheels and zero relative moves
/// before any hook, so the tests inject 1/120-notch wheels and moves to where the cursor already is.
/// </remarks>
internal sealed class WitnessHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const uint WM_QUIT = 0x0012, WM_MOUSEMOVE = 0x0200, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    private const uint LLMHF_INJECTED = 1;
    // "TS" in the high half. Markers are 32 bits: LL hooks only get dwExtraInfo's low 32 bits.
    private const uint TestPrefix = 0x5453_0000;

    private static int s_nextMarker;

    private readonly ConcurrentQueue<ulong> _seen = new();
    private readonly ConcurrentDictionary<ulong, bool> _absorb = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private readonly HookProc _proc;
    private uint _threadId;
    private nint _hook;

    private WitnessHook()
    {
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "witness hook" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
        if (_hook == 0) { throw new InvalidOperationException("The witness hook couldn't be installed."); }
    }

    public static WitnessHook Start() => new();

    /// <summary>A marker no other test or program uses.</summary>
    public static nuint NewMarker() => TestPrefix | (uint)(Interlocked.Increment(ref s_nextMarker) & 0xFFFF);

    /// <summary>Also swallow (after recording) injected events with this marker that reach the witness.</summary>
    public void Absorb(nuint extraInfo) => _absorb[extraInfo] = true;

    public bool Saw(nuint extraInfo) => _seen.Contains(extraInfo);

    public bool WaitFor(nuint extraInfo, int timeoutMs = 1000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (Saw(extraInfo)) { return true; }
            Thread.Sleep(2);
        }
        return Saw(extraInfo);
    }

    /// <summary>Injects a 1/120-notch wheel event with <paramref name="extraInfo"/>. False if SendInput refused.</summary>
    public static bool InjectTinyWheel(nuint extraInfo, bool horizontal = false) =>
        Send(new MOUSEINPUT { mouseData = 1, dwFlags = horizontal ? 0x1000u : 0x0800u, dwExtraInfo = extraInfo });

    /// <summary>Injects a move to where the cursor already is, with <paramref name="extraInfo"/>.</summary>
    public static bool InjectMoveToCursor(nuint extraInfo)
    {
        if (!GetCursorPos(out POINT p)) { return false; }
        int x = CalmMouse.Win.Input.HookWatchdog.AbsoluteCoordinate(p.x, GetSystemMetrics(76), GetSystemMetrics(78));
        int y = CalmMouse.Win.Input.HookWatchdog.AbsoluteCoordinate(p.y, GetSystemMetrics(77), GetSystemMetrics(79));
        return Send(new MOUSEINPUT { dx = x, dy = y, dwFlags = 0x0001 | 0x4000 | 0x8000, dwExtraInfo = extraInfo });
    }

    /// <summary>
    /// Skips the test where injected input never reaches low-level hooks (a CI machine without an
    /// interactive desktop), so a skip there can't hide a failure of the engine itself.
    /// </summary>
    public void RequireInjectedInputReachesHooks()
    {
        nuint marker = NewMarker();
        if (!InjectTinyWheel(marker)) { Assert.Skip("SendInput is refused here (no interactive desktop)."); }
        if (!WaitFor(marker, 1000)) { Assert.Skip("Injected input doesn't reach low-level hooks here."); }
    }

    public void Dispose()
    {
        if (_threadId != 0) { PostThreadMessageW(_threadId, WM_QUIT, 0, 0); }
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private static bool Send(MOUSEINPUT mi) =>
        SendInput(1, [new INPUT { type = 0, mi = mi }], Marshal.SizeOf<INPUT>()) == 1;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        _ready.Set();
        if (_hook == 0) { return; }
        try
        {
            while (GetMessageW(out MSG msg, 0, 0, 0) > 0) { DispatchMessageW(ref msg); }
        }
        finally
        {
            UnhookWindowsHookEx(_hook);
        }
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (uint)wParam is WM_MOUSEWHEEL or WM_MOUSEHWHEEL or WM_MOUSEMOVE)
        {
            MSLLHOOKSTRUCT s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((s.flags & LLMHF_INJECTED) != 0)
            {
                ulong extra = (uint)s.dwExtraInfo;
                bool ours = (extra & 0xFFFF_0000) == TestPrefix || _absorb.ContainsKey(extra);
                bool tiny = (uint)wParam == WM_MOUSEMOVE || Math.Abs((short)(s.mouseData >> 16)) <= 1;
                if (ours)
                {
                    _seen.Enqueue(extra);
                    if (tiny) { return 1; }
                }
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hmod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern int UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern int PostThreadMessageW(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}
