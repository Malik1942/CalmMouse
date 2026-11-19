using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>
/// The hook callback's logic, driven directly with physical (not injected) wheel events: no
/// Windows hook, no thread, a fake clock, fake keys and a fake target lookup. Nothing reaches the PC.
/// </summary>
public sealed class InputThreadHookTests
{
    private const long FakeMouse = 0x7FFF_0002;
    private static readonly WheelTarget Notepad = new("notepad.exe", Elevated: false);
    private static readonly WheelTarget TaskManager = new("Taskmgr.exe", Elevated: true);

    private double _now = 1000;
    private int _lookups;
    private WheelTarget _target = Notepad;
    private (WinModifiers, bool) _keys = (WinModifiers.None, false);
    private readonly InputThread _engine;

    public InputThreadHookTests()
    {
        _engine = new InputThread(new InputThreadOptions
        {
            ClockOverride = () => _now,
            KeysOverride = () => _keys,
            TargetOverride = () =>
            {
                _lookups++;
                return _target;
            },
        });
    }

    private ScrollRouter Router => _engine.RouterForTest;

    private bool Wheel(int delta, bool horizontal = false, bool injected = false) =>
        _engine.HookForTest(horizontal ? Native.WM_MOUSEHWHEEL : Native.WM_MOUSEWHEEL, new Native.MSLLHOOKSTRUCT
        {
            mouseData = unchecked((uint)(delta << 16)),
            flags = injected ? Native.LLMHF_INJECTED : 0,
        });

    private void Reverse() =>
        Router.SetDevice(new RoutedDevice(FakeMouse, DeviceKind.WheelMouse, "fake") { Wheel = new WheelSettings(reverse: true) });

    private List<WheelSend> Sends()
    {
        var sends = new List<WheelSend>();
        while (Router.TryTakeSend(out WheelSend s)) { sends.Add(s); }
        return sends;
    }

    [Fact]
    public void WithNothingOnAPhysicalWheelPassesWithoutLookingUpTheWindow()
    {
        Assert.False(Wheel(120));
        Assert.False(Wheel(-120, horizontal: true));
        Assert.Equal(0, _lookups);
        Assert.Equal(2, Router.Stats.HooksPassed);
    }

    [Fact]
    public void WithReverseOnAPhysicalWheelIsSwallowedAndItsTwinSendsItBackFlipped()
    {
        Reverse();
        Assert.True(Wheel(120));
        Assert.Equal(1, _lookups);
        _now += 0.0001;
        Router.RawWheel(_now, FakeMouse, 120, horizontal: false);
        Assert.Equal([new WheelSend(-120, false)], Sends());
    }

    [Fact]
    public void TheDeltaIsReadAsASignedShortFromTheHighWord()
    {
        Reverse();
        Assert.True(Wheel(-15, horizontal: true));
        Router.RawWheel(_now, FakeMouse, -15, horizontal: true);
        Assert.Equal([new WheelSend(15, true)], Sends());
    }

    [Fact]
    public void AnInjectedWheelPassesEvenWithAFeatureOn()
    {
        Reverse();
        Assert.False(Wheel(120, injected: true));
        Assert.Equal(0, _lookups);
        Assert.Equal(0, Router.Stats.HooksSwallowed);
    }

    [Fact]
    public void AnElevatedWindowsWheelPasses()
    {
        Reverse();
        _target = TaskManager;
        Assert.False(Wheel(120));
        Assert.Equal(1, _lookups);
    }

    [Fact]
    public void PausedNothingIsSwallowedOrLookedUp()
    {
        Reverse();
        Router.SetPaused(true);
        Assert.False(Wheel(120));
        Assert.Equal(0, _lookups);
    }

    [Fact]
    public void TheKeysHeldGoToTheRouter()
    {
        Router.SetDevice(new RoutedDevice(FakeMouse, DeviceKind.WheelMouse, "fake") { Wheel = new WheelSettings(smooth: true) });
        // Ctrl is zoom: never smoothed, so its twin goes on unchanged instead of starting a glide.
        _keys = (WinModifiers.Ctrl, false);
        Assert.True(Wheel(120));
        Router.RawWheel(_now, FakeMouse, 120, horizontal: false);
        Assert.Equal([new WheelSend(120, false)], Sends());
        Assert.False(Router.NeedsTicks);
    }
}
