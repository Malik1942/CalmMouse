using CalmMouse.Core.Routing;

namespace CalmMouse.Core.Tests.Routing;

/// <summary>
/// A <see cref="ScrollRouter"/> with a fake clock, three mice and short names for the calls the
/// input thread makes. Time only moves when a test says so.
/// </summary>
internal sealed class RouterHarness
{
    public const long MouseA = 101;
    public const long MouseB = 202;
    public const long Pad = 303;
    public const long Stranger = 404;   // never registered
    public const int Notch = 120;

    /// <summary>Seconds per frame of the glide timer (a 120 Hz display).</summary>
    public const double Frame = 1.0 / 120;

    public ScrollRouter Router { get; } = new();

    /// <summary>Seconds. Starts away from zero, as a real monotonic clock would.</summary>
    public double Now { get; private set; } = 1000;

    /// <summary>Sets up the three mice: A and B are wheel mice (all off unless given), Pad is the touchpad.</summary>
    public RouterHarness(WheelSettings? a = null, WheelSettings? b = null)
    {
        Router.SetDevices([
            new RoutedDevice(MouseA, DeviceKind.WheelMouse, "VID_3554&PID_F58A") { Wheel = a ?? new WheelSettings() },
            new RoutedDevice(MouseB, DeviceKind.WheelMouse, "VID_5253&PID_1020") { Wheel = b ?? new WheelSettings() },
            // Every feature on: a touchpad must ignore them all.
            new RoutedDevice(Pad, DeviceKind.Touchpad, "VID_04F3&PID_3282")
            {
                Wheel = new WheelSettings(reverse: true, blockWhilePressed: true, smooth: true),
            },
        ]);
        Router.ForegroundChanged("notepad.exe", elevated: false);
    }

    /// <summary>Moves the clock without ticking (nothing runs in between).</summary>
    public void Wait(double ms) => Now += ms / 1000;

    /// <summary>Moves the clock one frame at a time, ticking each frame, as the input thread's timer would.</summary>
    public void Run(double ms)
    {
        double end = Now + ms / 1000;
        while (Now + Frame <= end + 1e-12)
        {
            Now += Frame;
            Router.Tick(Now);
        }
        Now = end;
        Router.Tick(Now);
    }

    public void Tick() => Router.Tick(Now);

    public HookAnswer Hook(int delta = Notch, bool horizontal = false, WinModifiers mods = WinModifiers.None) =>
        Router.HookWheel(Now, delta, horizontal, mods);

    public void Raw(long device, int delta = Notch, bool horizontal = false, ulong extraInfo = 0) =>
        Router.RawWheel(Now, device, delta, horizontal, extraInfo);

    /// <summary>One physical tick in the order Phase 0 suggests is likely: hook first, Raw Input a moment later.</summary>
    public HookAnswer Turn(long device, int delta = Notch, bool horizontal = false, WinModifiers mods = WinModifiers.None)
    {
        HookAnswer answer = Hook(delta, horizontal, mods);
        Raw(device, delta, horizontal);
        return answer;
    }

    /// <summary>One physical tick with Raw Input first.</summary>
    public HookAnswer TurnRawFirst(long device, int delta = Notch, bool horizontal = false, WinModifiers mods = WinModifiers.None)
    {
        Raw(device, delta, horizontal);
        return Hook(delta, horizontal, mods);
    }

    public void PressWheel(long device) => Router.RawButton(Now, device, RawMouseButton.Middle, down: true);

    public void ReleaseWheel(long device) => Router.RawButton(Now, device, RawMouseButton.Middle, down: false);

    /// <summary>Everything the router has asked to inject since the last call.</summary>
    public List<WheelSend> Sends()
    {
        var sends = new List<WheelSend>();
        while (Router.TryTakeSend(out WheelSend send)) { sends.Add(send); }
        return sends;
    }

    /// <summary>Ticks frame by frame until the router needs no more ticks, and returns everything sent.</summary>
    public List<WheelSend> RunToIdle()
    {
        double limit = Now + 10;
        while (Router.NeedsTicks)
        {
            Assert.True(Now < limit, "The router still wanted ticks after 10 seconds.");
            Now += Frame;
            Router.Tick(Now);
        }
        return Sends();
    }

    public static WheelSend Up(int delta = Notch) => new(delta, Horizontal: false);

    public static WheelSend Right(int delta = Notch) => new(delta, Horizontal: true);
}
