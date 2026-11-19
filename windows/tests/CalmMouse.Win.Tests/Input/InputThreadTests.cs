using System.Diagnostics;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>Raw Input registration is per process: these tests must not run alongside each other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InputThreadCollection
{
    public const string Name = "Input thread";
}

/// <summary>
/// The real input thread on a real desktop. Safe while someone uses the PC: every feature of every
/// real mouse stays off, so physical scrolling passes (at worst it is resent unchanged), and the
/// tests only inject 1/120-notch wheels and moves to where the cursor already is, which the witness
/// hook (installed before the engine's, so it tells passed from swallowed) swallows before any app.
/// Every test disposes the engine and the witness in `using`, so no hook outlives a failing test
/// (and Windows removes hooks with their thread anyway).
/// </summary>
/// <remarks>
/// On the Phase 0 PC an injected event now and then (about one in a hundred) reached Raw Input but
/// neither of our hooks: something in front of both, most likely another program's low-level
/// hook, ate it. So an
/// injection that neither hook saw is tried again; one that the engine's hook saw but the witness
/// didn't is a failure (the engine swallowed it).
/// </remarks>
[Collection(InputThreadCollection.Name)]
public sealed class InputThreadTests
{
    /// <summary>A Raw Input handle no real device has, with reverse on.</summary>
    private const long FakeMouse = 0x7FFF_0001;

    private const int Attempts = 3;

    private static readonly WheelTarget Notepad = new("notepad.exe", Elevated: false);

    private static RoutedDevice FakeReversingMouse() =>
        new(FakeMouse, DeviceKind.WheelMouse, "FFFF:FFFF:Fake mouse") { Wheel = new WheelSettings(reverse: true) };

    [Fact]
    public void StartsWithTheHookInstalledAndSeesItsOwnPingWhichNobodyElseGets()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        engine.Start();

        Assert.True(engine.IsRunning);
        Assert.True(engine.Status.HookInstalled);
        Assert.True(engine.PingAndWait());
        // The ping is swallowed by the engine's hook: a wheel sent after it reaches the witness,
        // the ping before it never does.
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());
        Assert.False(witness.Saw(engine.PingExtraInfo));
    }

    [Fact]
    public void WithEveryFeatureOffTheRouterPassesAndInjectedWheelsGoThrough()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread(new InputThreadOptions { RecordWheelLog = true });
        engine.Start();

        Assert.False(engine.WithRouter(r => r.Swallows(Notepad)));
        nuint marker = WitnessHook.NewMarker();
        AssertWheelPasses(engine, witness, marker);
        Assert.True(WaitForLog(engine, e => e.Kind == WheelLogKind.InjectedWheel && e.ExtraInfo == marker, out WheelLogEntry entry));
        Assert.False(entry.Swallowed);
    }

    [Fact]
    public void InjectedWheelsAreNeverSwallowedEvenWithAFeatureOn()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        engine.Start();

        // A made-up mouse with reverse on: the router now swallows physical wheel events (a real
        // mouse's, all off, are resent unchanged), but injected ones must still pass.
        Assert.True(engine.WithRouter(r =>
        {
            r.SetDevice(FakeReversingMouse());
            return r.Swallows(Notepad);
        }));
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker(), horizontal: true);
        // Our own resend marker too: those are what the router itself injects.
        witness.Absorb(ScrollRouter.ResendMarker);
        AssertWheelPasses(engine, witness, ScrollRouter.ResendMarker);
    }

    [Fact]
    public void PauseMakesTheRouterPassEverythingAndResumeUndoesIt()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeReversingMouse());
            return true;
        });

        engine.SetPaused(true);
        Assert.True(engine.IsPaused);
        Assert.False(engine.WithRouter(r => r.Swallows(Notepad)));
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());

        engine.SetPaused(false);
        Assert.True(engine.WithRouter(r => r.Swallows(Notepad)));
        // Elevated windows are always left alone.
        Assert.False(engine.WithRouter(r => r.Swallows(new WheelTarget("Taskmgr.exe", Elevated: true))));
    }

    [Fact]
    public void StopRemovesTheHook()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeReversingMouse());
            return true;
        });
        nuint ping = engine.PingExtraInfo;
        witness.Absorb(ping);

        engine.Stop();
        Assert.False(engine.IsRunning);
        Assert.False(engine.Status.HookInstalled);
        Assert.False(engine.HookHandlePresent);
        // Nobody swallows the ping any more: it reaches the witness.
        AssertReachesWitness(witness, ping, () => WitnessHook.InjectMoveToCursor(ping));
        engine.Stop();   // twice is fine
    }

    [Fact]
    public void DisposeRemovesTheHookAndAnotherEngineCanStartAfterIt()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        nuint ping;
        using (var engine = new InputThread())
        {
            engine.Start();
            ping = engine.PingExtraInfo;
            witness.Absorb(ping);
        }
        AssertReachesWitness(witness, ping, () => WitnessHook.InjectMoveToCursor(ping));

        using var next = new InputThread();
        next.Start();
        Assert.True(next.Status.HookInstalled);
    }

    [Fact]
    public void OnlyOneEngineRunsAtATime()
    {
        using var first = new InputThread();
        first.Start();
        using var second = new InputThread();
        Assert.Throws<InvalidOperationException>(second.Start);
        Assert.True(first.IsRunning);
    }

    [Fact]
    public void AFaultRemovesTheHookFirstThenReports()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        using var reported = new ManualResetEventSlim();
        bool hookGoneWhenReported = false;
        engine.Faulted += (_, _) =>
        {
            hookGoneWhenReported = !engine.HookHandlePresent;
            reported.Set();
        };
        engine.Start();
        nuint ping = engine.PingExtraInfo;
        witness.Absorb(ping);

        engine.FaultForTest();
        Assert.True(reported.Wait(1000, TestContext.Current.CancellationToken));
        Assert.True(hookGoneWhenReported);
        Assert.True(SpinUntil(() => !engine.IsRunning, 1000));
        Assert.False(engine.Status.HookInstalled);
        Assert.Contains(engine.TakeMessages(), m => m.Contains("FAULT", StringComparison.Ordinal));
        AssertReachesWitness(witness, ping, () => WitnessHook.InjectMoveToCursor(ping));
    }

    [Fact]
    public void TheWatchdogReinstallsAHookWindowsRemoved()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread(new InputThreadOptions
        {
            WatchdogInterval = 0.2,
            PingTimeout = 0.2,
            UserActiveOverride = () => true,
        });
        engine.Start();
        witness.Absorb(engine.PingExtraInfo);

        engine.LoseHookForTest();
        Assert.True(SpinUntil(() => engine.CountersNow().HookReinstalls >= 1, 1500));
        Assert.True(engine.HookHandlePresent);
        Assert.True(engine.PingAndWait());
    }

    [Fact]
    public void ListsThisPCsMice()
    {
        using var engine = new InputThread();
        engine.Start();
        IReadOnlyList<MouseDeviceInfo> devices = engine.Devices;
        foreach (MouseDeviceInfo d in devices)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(d.ToString());
            Assert.False(string.IsNullOrWhiteSpace(d.StableKey));
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.NotEqual(0, d.DeviceId);
        }
        // The in-memory settings have every feature off, so nothing is swallowed anywhere.
        Assert.False(engine.WithRouter(r => r.Swallows(Notepad)));
    }

    // MARK: Helpers

    /// <summary>
    /// Injects a 1/120-notch wheel with <paramref name="marker"/> from the engine's thread and
    /// asserts the engine's hook let it through to the witness.
    /// </summary>
    private static void AssertWheelPasses(InputThread engine, WitnessHook witness, nuint marker, bool horizontal = false)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            long before = engine.CountersNow().InjectedWheels;
            Assert.True(engine.InjectWheel(1, marker, horizontal));
            if (witness.WaitFor(marker, 500)) { return; }
            long after = engine.CountersNow().InjectedWheels;
            Assert.True(after == before, $"The engine's hook saw 0x{marker:X} but the witness never did: the engine swallowed an injected wheel.");
            // Neither hook saw it: lost in front of both. Try again.
        }
        Assert.Fail($"0x{marker:X} reached neither hook in {Attempts} tries.");
    }

    private static void AssertReachesWitness(WitnessHook witness, nuint marker, Func<bool> inject)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            Assert.True(inject());
            if (witness.WaitFor(marker, 500)) { return; }
        }
        Assert.Fail($"0x{marker:X} never reached the witness in {Attempts} tries.");
    }

    private static bool WaitForLog(InputThread engine, Func<WheelLogEntry, bool> match, out WheelLogEntry found)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 1000)
        {
            while (engine.WheelLog!.TryRead(out WheelLogEntry e))
            {
                if (match(e))
                {
                    found = e;
                    return true;
                }
            }
            Thread.Sleep(2);
        }
        found = default;
        return false;
    }

    private static bool SpinUntil(Func<bool> condition, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) { return true; }
            Thread.Sleep(5);
        }
        return condition();
    }
}
