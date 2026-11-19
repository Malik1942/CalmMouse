using CalmMouse.Win.Settings;

namespace CalmMouse.Win.Tests.Settings;

/// <summary>In this process, each test with its own unique name, so it never meets a running CalmMouse.</summary>
public sealed class SingleInstanceTests
{
    private static string UniqueName() => $"CalmMouse.Tests-{Guid.NewGuid():N}";

    [Fact]
    public void TheDefaultNameCarriesTheUserSid()
    {
        Assert.StartsWith("CalmMouse-S-1-", SingleInstance.DefaultName);
        Assert.Equal(@"Local\CalmMouse-x", SingleInstance.MutexName("CalmMouse-x"));
    }

    [Fact]
    public void OnlyTheFirstGetsIt()
    {
        string name = UniqueName();
        using SingleInstance? first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(name));
        Assert.Null(SingleInstance.TryAcquire(name));
    }

    [Fact]
    public void ItCanBeTakenAgainOnceReleased()
    {
        string name = UniqueName();
        SingleInstance.TryAcquire(name)!.Dispose();
        using SingleInstance? again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void ASecondLaunchAsksTheFirstToOpenSettings()
    {
        string name = UniqueName();
        using SingleInstance first = SingleInstance.TryAcquire(name)!;
        using var shown = new ManualResetEventSlim();
        int quits = 0;
        first.ShowSettingsRequested += (_, _) => shown.Set();
        first.QuitRequested += (_, _) => Interlocked.Increment(ref quits);

        Assert.True(SingleInstance.SignalShowSettings(name));
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, quits);
    }

    [Fact]
    public void QuitIsItsOwnSignal()
    {
        string name = UniqueName();
        using SingleInstance first = SingleInstance.TryAcquire(name)!;
        using var quit = new ManualResetEventSlim();
        first.QuitRequested += (_, _) => quit.Set();
        Assert.True(SingleInstance.SignalQuit(name));
        Assert.True(quit.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ARequestBeforeAnyoneListensIsKept()
    {
        string name = UniqueName();
        using SingleInstance first = SingleInstance.TryAcquire(name)!;
        Assert.True(first.EventsWereNew);
        Assert.True(SingleInstance.SignalShowSettings(name));
        Thread.Sleep(200);   // the signal arrives while nobody listens yet

        using var shown = new ManualResetEventSlim();
        first.ShowSettingsRequested += (_, _) => shown.Set();
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void QuitCanWaitForTheRunningCopyToBeGone()
    {
        string name = UniqueName();
        SingleInstance first = SingleInstance.TryAcquire(name)!;
        first.QuitRequested += (_, _) => { Thread.Sleep(200); first.Dispose(); };
        Assert.True(SingleInstance.IsRunning(name));
        Assert.True(SingleInstance.SignalQuit(name));
        Assert.True(SingleInstance.WaitForExit(name, TimeSpan.FromSeconds(5)));
        Assert.False(SingleInstance.IsRunning(name));
    }

    [Fact]
    public void WaitingForACopyThatStaysTimesOut()
    {
        string name = UniqueName();
        using SingleInstance first = SingleInstance.TryAcquire(name)!;
        Assert.False(SingleInstance.WaitForExit(name, TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void ALaunchCanTakeOverOnceTheOldCopyIsGone()
    {
        string name = UniqueName();
        SingleInstance old = SingleInstance.TryAcquire(name)!;
        Task.Run(async () => { await Task.Delay(300); old.Dispose(); }, TestContext.Current.CancellationToken);
        using SingleInstance? taken = SingleInstance.TryAcquire(name, TimeSpan.FromSeconds(3));
        Assert.NotNull(taken);
    }

    [Fact]
    public void SignallingWithNobodyRunningSaysSo()
    {
        Assert.False(SingleInstance.SignalShowSettings(UniqueName(), TimeSpan.Zero));
        Assert.False(SingleInstance.SignalQuit(UniqueName(), TimeSpan.FromMilliseconds(100)));
    }
}
