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
/// tests only inject wheels of a few 1/120ths of a notch, which the witness hook (installed before
/// the engine's, so it tells passed from swallowed) swallows before any app sees them. Every test
/// disposes the engine and the witness in `using`, so no hook outlives a failing test (and Windows
/// removes hooks with their thread anyway).
/// </summary>
/// <remarks>
/// On the Phase 0 PC an injected event now and then (about one in a hundred) reached Raw Input but
/// neither of our hooks: something in front of both, most likely another program's low-level
/// hook, ate it. So an injection that neither hook saw is tried again; one that the engine's hook
/// saw but the witness didn't is a failure (the engine swallowed it).
/// </remarks>
[Collection(InputThreadCollection.Name)]
public sealed class InputThreadTests
{
    /// <summary>A Raw Input handle no real device has.</summary>
    private const long FakeMouse = 0x7FFF_0001;

    private const int Attempts = 3;

    private static readonly WheelTarget Notepad = new("notepad.exe", Elevated: false);

    private static RoutedDevice FakeMouseWith(WheelSettings wheel) =>
        new(FakeMouse, DeviceKind.WheelMouse, "FFFF:FFFF:Fake mouse") { Wheel = wheel };

    private static readonly WheelSettings Reverse = new(reverse: true);

    /// <summary>
    /// One physical tick of the made-up mouse, as the hook and Raw Input would report it, straight
    /// into the engine's router on its thread. Its sends are injected right after, for real.
    /// </summary>
    private static int TurnFakeMouse(InputThread engine, int delta) => engine.WithRouter(r =>
    {
        double t = InputThread.ClockNow();
        r.HookWheel(t, delta, false, WinModifiers.None, false, Notepad);
        r.RawWheel(t + 0.0001, FakeMouse, delta, false);
        return r.PendingSendCount;
    });

    [Fact]
    public void StartsWithTheHookInstalledAndItSwallowsWhatItShould()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        nuint swallow = WitnessHook.NewMarker();
        using var engine = new InputThread(new InputThreadOptions { SwallowMarker = (uint)swallow });
        engine.Start();

        Assert.True(engine.IsRunning);
        Assert.True(engine.Status.HookInstalled);
        // The engine's hook eats the test marker: a wheel sent after it reaches the witness, the
        // one before it never does.
        Assert.True(engine.InjectWheel(1, swallow));
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());
        Assert.False(witness.Saw(swallow));
    }

    [Fact]
    public void WithEveryFeatureOffTheRouterPassesAndInjectedWheelsGoThrough()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread(new InputThreadOptions { RecordWheelLog = true });
        engine.Start();

        Assert.False(engine.WithRouter(r => r.Swallows(Notepad) || r.MightSwallow));
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
            r.SetDevice(FakeMouseWith(Reverse));
            return r.Swallows(Notepad);
        }));
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker(), horizontal: true);
        // Our own resend marker too: those are what the router itself injects.
        witness.Absorb(ScrollRouter.ResendMarker);
        AssertWheelPasses(engine, witness, ScrollRouter.ResendMarker);
    }

    [Fact]
    public void AResendGoesOutWithTheMarkerPassesTheHookAndComesBackThroughRawInput()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        witness.Absorb(ScrollRouter.ResendMarker);
        using var engine = new InputThread();
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeMouseWith(Reverse));
            return true;
        });

        for (int attempt = 0; attempt < Attempts && !witness.Saw(ScrollRouter.ResendMarker); attempt++)
        {
            long echoes = engine.CountersNow().ResendEchoes;
            Assert.Equal(1, TurnFakeMouse(engine, 1));
            // Raw Input sees every resend, whatever happens to it at the hooks.
            Assert.True(SpinUntil(() => engine.CountersNow().ResendEchoes > echoes, 1000));
            witness.WaitFor(ScrollRouter.ResendMarker, 500);
        }
        // Reversed, and let through by the engine's own hook.
        Assert.Contains(-1, witness.DeltasOf(ScrollRouter.ResendMarker));
        EngineCounters c = engine.CountersNow();
        Assert.True(c.Sends >= 1);
        Assert.Equal(0, c.SendFailures);
        Assert.False(engine.Status.BreakerOpen);
    }

    [Fact]
    public void TheGlideTimerRunsOnlyWhileAGlidePlays()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        witness.Absorb(ScrollRouter.ResendMarker, maxDelta: 4);
        using var engine = new InputThread(new InputThreadOptions { RecordWheelLog = true });
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeMouseWith(new WheelSettings(smooth: true, speed: 12)));
            return true;
        });
        Assert.False(engine.TickingForTest);

        // One 1/120 tick at the top speed glides 4 units over a fraction of a second.
        Assert.Equal(0, TurnFakeMouse(engine, 1));
        Assert.True(SpinUntil(() => engine.TickingForTest, 500));
        Assert.True(SpinUntil(() => !engine.TickingForTest, 2000));
        Assert.Equal(4, SentTotal(engine));
    }

    [Fact]
    public void StopSendsTheRestOfAGlideBeforeItGoes()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        witness.Absorb(ScrollRouter.ResendMarker, maxDelta: 16);
        using var engine = new InputThread(new InputThreadOptions { RecordWheelLog = true });
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeMouseWith(new WheelSettings(smooth: true, speed: 12)));
            return true;
        });

        // 4/120 at the top speed: 16 units over about a third of a second. Stop at once.
        TurnFakeMouse(engine, 4);
        engine.Stop();
        Assert.Equal(16, SentTotal(engine));
    }

    [Fact]
    public void RefusedResendsTripTheBreakerAndEverythingPasses()
    {
        // Only the test's 1/120 ticks are "refused"; anything of the owner's (resent unchanged
        // while the made-up mouse has a feature on) still goes out for real.
        using var engine = new InputThread(new InputThreadOptions { ResendOverride = (delta, _) => Math.Abs(delta) <= 1 ? false : null });
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeMouseWith(Reverse));
            return true;
        });

        for (int i = 0; i < ResendBreaker.DefaultTripAfter; i++) { TurnFakeMouse(engine, 1); }
        Assert.True(engine.BreakerOpenForTest);
        Assert.Equal(1, engine.CountersNow().BreakerTrips);
        Assert.False(engine.WithRouter(r => r.Swallows(Notepad) || r.MightSwallow));
        Assert.Contains(engine.TakeMessages(), m => m.Contains("aren't getting through", StringComparison.Ordinal));
        // The user's own pause and resume don't override it.
        engine.SetPaused(true);
        engine.SetPaused(false);
        Assert.False(engine.WithRouter(r => r.MightSwallow));
    }

    [Fact]
    public void ResendsThatNeverComeBackTripTheBreakerToo()
    {
        // "Accepted" but never injected, so no echo ever comes through Raw Input.
        using var engine = new InputThread(new InputThreadOptions { ResendOverride = (delta, _) => Math.Abs(delta) <= 1 ? true : null });
        engine.Start();
        engine.WithRouter(r =>
        {
            r.SetDevice(FakeMouseWith(Reverse));
            return true;
        });

        for (int i = 0; i < ResendBreaker.DefaultTripAfter; i++) { TurnFakeMouse(engine, 1); }
        Assert.True(SpinUntil(() => engine.BreakerOpenForTest, 1500));
        Assert.False(engine.WithRouter(r => r.MightSwallow));
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
            r.SetDevice(FakeMouseWith(Reverse));
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
        nuint swallow = WitnessHook.NewMarker();
        using var engine = new InputThread(new InputThreadOptions { SwallowMarker = (uint)swallow });
        engine.Start();

        engine.Stop();
        Assert.False(engine.IsRunning);
        Assert.False(engine.Status.HookInstalled);
        Assert.False(engine.HookHandlePresent);
        // Nobody swallows the marker any more: it reaches the witness.
        AssertReachesWitness(witness, swallow);
        engine.Stop();   // twice is fine
    }

    [Fact]
    public void DisposeRemovesTheHookAndAnotherEngineCanStartAfterIt()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        nuint swallow = WitnessHook.NewMarker();
        using (var engine = new InputThread(new InputThreadOptions { SwallowMarker = (uint)swallow }))
        {
            engine.Start();
        }
        AssertReachesWitness(witness, swallow);

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
        nuint swallow = WitnessHook.NewMarker();
        using var engine = new InputThread(new InputThreadOptions { SwallowMarker = (uint)swallow });
        using var reported = new ManualResetEventSlim();
        bool hookGoneWhenReported = false;
        engine.Faulted += (_, _) =>
        {
            hookGoneWhenReported = !engine.HookHandlePresent;
            reported.Set();
        };
        engine.Start();

        engine.FaultForTest();
        Assert.True(reported.Wait(1000, TestContext.Current.CancellationToken));
        Assert.True(hookGoneWhenReported);
        Assert.True(SpinUntil(() => !engine.IsRunning, 1000));
        Assert.False(engine.Status.HookInstalled);
        Assert.Contains(engine.TakeMessages(), m => m.Contains("FAULT", StringComparison.Ordinal));
        AssertReachesWitness(witness, swallow);
    }

    [Fact]
    public void ItStartsAgainAfterAFault()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread();
        engine.Start();
        int devices = engine.Devices.Count;

        engine.FaultForTest();
        Assert.True(SpinUntil(() => !engine.IsRunning, 1000));
        engine.Start();
        Assert.True(engine.IsRunning);
        Assert.True(engine.Status.HookInstalled);
        Assert.Equal(devices, engine.Devices.Count);
        AssertHookAlive(engine, witness);
    }

    [Fact]
    public void TheWatchdogReinstallsAHookThatHearsNothingWhileTheMouseIsUsed()
    {
        using var witness = WitnessHook.Start();
        witness.RequireInjectedInputReachesHooks();
        using var engine = new InputThread(new InputThreadOptions { HookSilenceLimit = 0.2, HookReinstallGap = 0.2 });
        engine.Start();

        engine.LoseHookForTest();
        // Raw Input keeps reporting the mouse (as moving a real one would) while the hook is silent.
        Assert.True(SpinUntil(() =>
        {
            engine.PhysicalInputForTest();
            return engine.CountersNow().HookReinstalls >= 1;
        }, 1500));
        Assert.True(engine.HookHandlePresent);
        AssertHookAlive(engine, witness);
        Assert.Contains(engine.TakeMessages(), m => m.Contains("reinstalled", StringComparison.Ordinal));
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
        Assert.False(engine.WithRouter(r => r.MightSwallow));
    }

    [Fact]
    public void TheSettingsKeyIsTheSameEveryTime()
    {
        using var engine = new InputThread();
        engine.Start();
        Dictionary<long, string> first = engine.Devices.ToDictionary(d => d.DeviceId, d => d.StableKey);
        engine.Stop();
        engine.Start();
        foreach (MouseDeviceInfo d in engine.Devices)
        {
            Assert.Equal(first[d.DeviceId], d.StableKey);
        }
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

    /// <summary>The engine's hook is installed and called: an injected wheel goes through it.</summary>
    private static void AssertHookAlive(InputThread engine, WitnessHook witness)
    {
        long before = engine.CountersNow().InjectedWheels;
        AssertWheelPasses(engine, witness, WitnessHook.NewMarker());
        Assert.True(SpinUntil(() => engine.CountersNow().InjectedWheels > before, 500));
    }

    private static void AssertReachesWitness(WitnessHook witness, nuint marker)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            Assert.True(WitnessHook.InjectTinyWheel(marker));
            if (witness.WaitFor(marker, 500)) { return; }
        }
        Assert.Fail($"0x{marker:X} never reached the witness in {Attempts} tries.");
    }

    /// <summary>The total of everything the engine has injected for the router so far (from its wheel log).</summary>
    private static int SentTotal(InputThread engine)
    {
        int total = 0;
        while (engine.WheelLog!.TryRead(out WheelLogEntry e))
        {
            if (e.Kind == WheelLogKind.Sent && !e.Swallowed) { total += e.Delta; }
        }
        return total;
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
