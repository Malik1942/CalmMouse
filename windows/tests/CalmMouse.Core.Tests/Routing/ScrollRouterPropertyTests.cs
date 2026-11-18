using CalmMouse.Core.Routing;
using static CalmMouse.Core.Tests.Routing.RouterHarness;

namespace CalmMouse.Core.Tests.Routing;

/// <summary>
/// Randomised runs with fixed seeds: whatever order and timing the hook and Raw Input deliver
/// in, every physical wheel event comes out exactly once, and nothing the router does throws.
/// </summary>
public sealed class ScrollRouterPropertyTests
{
    /// <summary>One physical wheel tick and how its two halves reach the router.</summary>
    private sealed record Tick(long Device, int Delta, bool Horizontal, double HookAt, double? RawAt);

    /// <summary>Something that happens at a moment: a hook event, a Raw Input wheel, a timer tick or a change of mode.</summary>
    private sealed record Step(double At, int Order, Action Run);

    /// <summary>
    /// Ticks from A, B, the touchpad (which never shows up in Raw Input) and an unknown mouse,
    /// with Raw Input mostly just after the hook, sometimes just before, sometimes held up,
    /// sometimes very late.
    /// </summary>
    private static List<Tick> MakeTicks(Random rng, int count, Func<int> delta)
    {
        long[] devices = [MouseA, MouseB, Pad, Stranger];
        var ticks = new List<Tick>();
        double t = 0;
        for (int i = 0; i < count; i++)
        {
            t += rng.Next(4) == 0 ? 0 : rng.NextDouble() * 0.03;
            long device = devices[rng.Next(devices.Length)];
            double? rawAt = null;
            if (device != Pad)
            {
                double roll = rng.NextDouble();
                double lag = roll < 0.7 ? rng.NextDouble() * 0.0015
                    : roll < 0.8 ? -rng.NextDouble() * 0.0015
                    : roll < 0.9 ? 0.002 + rng.NextDouble() * 0.007
                    : 0.010 + rng.NextDouble() * 0.060;
                rawAt = t + lag;
            }
            ticks.Add(new Tick(device, delta(), rng.Next(3) == 0, t, rawAt));
        }
        return ticks;
    }

    /// <summary>Plays the steps in time order (ties in the order they were made), with a timer tick every 4 ms.</summary>
    private static void Play(RouterHarness h, double start, List<Step> steps)
    {
        double end = steps.Max(s => s.At);
        for (double t = 0; t <= end; t += 0.004)
        {
            double at = t;
            steps.Add(new Step(at, steps.Count, () => { h.At(start + at); h.Tick(); }));
        }
        foreach (Step step in steps.OrderBy(s => s.At).ThenBy(s => s.Order))
        {
            step.Run();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryPhysicalEventComesOutExactlyOnce_WithModeChanges(int seed)
    {
        var rng = new Random(seed);
        // Unique sizes, random directions: each event can be recognised in the output.
        int[] sizes = Enumerable.Range(1, 2000).OrderBy(_ => rng.Next()).ToArray();
        int next = 0;
        List<Tick> ticks = MakeTicks(rng, 400, () => sizes[next++] * (rng.Next(2) == 0 ? 1 : -1));

        var h = new RouterHarness(a: new WheelSettings(reverse: true));
        h.Router.SetAppRules([new WheelAppRule("figma.exe", "Figma") { LeaveAlone = true }]);
        double start = h.Now;
        var emitted = new List<int>();
        var steps = new List<Step>();
        bool paused = false;

        foreach (Tick tick in ticks)
        {
            steps.Add(new Step(tick.HookAt, steps.Count, () =>
            {
                h.At(start + tick.HookAt);
                if (h.Hook(tick.Delta, tick.Horizontal) == HookAnswer.Pass) { emitted.Add(tick.Delta); }
            }));
            if (tick.RawAt is double rawAt)
            {
                steps.Add(new Step(rawAt, steps.Count, () => { h.At(start + rawAt); h.Raw(tick.Device, tick.Delta, tick.Horizontal); }));
            }
            // Now and then: pause or resume, or the cursor moves to another kind of window.
            if (rng.Next(12) == 0)
            {
                double at = tick.HookAt + rng.NextDouble() * 0.002;
                int what = rng.Next(4);
                steps.Add(new Step(at, steps.Count, () =>
                {
                    h.At(start + at);
                    switch (what)
                    {
                        case 0: paused = !paused; h.Router.SetPaused(paused); break;
                        case 1: h.Target = TaskManager; break;
                        case 2: h.Target = new WheelTarget("figma.exe", Elevated: false); break;
                        default: h.Target = Notepad; paused = false; h.Router.SetPaused(false); break;
                    }
                }));
            }
        }

        Play(h, start, steps);
        h.Target = Notepad;
        h.Router.SetPaused(false);
        emitted.AddRange(h.RunToIdle().Select(s => s.Delta));
        emitted.AddRange(h.Sends().Select(s => s.Delta));

        Assert.Equal(ticks.Select(t => Math.Abs(t.Delta)).Order(), emitted.Select(Math.Abs).Order());
        Assert.Equal(0, h.Router.Stats.Faults);
        Assert.True(h.Router.Stats.HooksSwallowed > 0 && h.Router.Stats.HooksPassed > 0);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void GlidesAndResendsTogetherCoverExactlyWhatTheWheelsTurned(int seed)
    {
        var rng = new Random(seed);
        int[] sizes = [120, 120, 120, 240, 15, 30, 10];
        // One direction per axis: turning round drops what a glide still owed, on purpose.
        List<Tick> ticks = MakeTicks(rng, 300, () => sizes[rng.Next(sizes.Length)]);

        var smooth = new WheelSettings(smooth: true);
        var h = new RouterHarness(a: smooth, b: smooth);
        double start = h.Now;
        var emitted = new List<WheelSend>();
        var steps = new List<Step>();
        foreach (Tick tick in ticks)
        {
            steps.Add(new Step(tick.HookAt, steps.Count, () =>
            {
                h.At(start + tick.HookAt);
                if (h.Hook(tick.Delta, tick.Horizontal) == HookAnswer.Pass) { emitted.Add(new WheelSend(tick.Delta, tick.Horizontal)); }
            }));
            if (tick.RawAt is double rawAt)
            {
                steps.Add(new Step(rawAt, steps.Count, () => { h.At(start + rawAt); h.Raw(tick.Device, tick.Delta, tick.Horizontal); }));
            }
        }

        Play(h, start, steps);
        emitted.AddRange(h.RunToIdle());

        Assert.Equal(ticks.Where(t => !t.Horizontal).Sum(t => t.Delta), emitted.Where(s => !s.Horizontal).Sum(s => s.Delta));
        Assert.Equal(ticks.Where(t => t.Horizontal).Sum(t => t.Delta), emitted.Where(s => s.Horizontal).Sum(s => s.Delta));
        Assert.Equal(0, h.Router.Stats.Faults);
    }

    [Fact]
    public void NothingThrows_EvenOnNonsense()
    {
        var h = new RouterHarness(a: new WheelSettings(reverse: true, smooth: true, speed: double.NaN), b: new WheelSettings(smooth: true));
        h.Turn(MouseA);
        h.Turn(MouseB, int.MaxValue);
        h.Turn(MouseB, int.MinValue, horizontal: true);
        h.Turn(MouseA, int.MinValue);
        h.Router.Tick(double.NaN, WinModifiers.None);          // a broken clock mid-glide
        h.Router.Tick(double.PositiveInfinity, WinModifiers.None);
        h.Router.HookWheel(double.NaN, Notch, false, WinModifiers.None, middleDown: false, new WheelTarget(null, false));
        h.Router.RawWheel(double.NegativeInfinity, MouseA, Notch, false);
        h.Router.RawButton(double.NaN, MouseA, RawMouseButton.Middle, down: true);
        h.Router.SetDevices(null!);
        h.Router.SetAppRules(null!);
        h.Router.SetDevices([null!, new RoutedDevice(MouseA, DeviceKind.WheelMouse, "A") { Wheel = new WheelSettings(smooth: true) }]);
        h.Sends();

        // And it still works afterwards.
        h.Wait(1000);
        h.Router.RawButton(h.Now, MouseA, RawMouseButton.Middle, down: false);
        h.Wait(1000);
        h.Turn(MouseA);
        Assert.Equal(Notch, h.RunToIdle().Sum(s => s.Delta));
    }
}
