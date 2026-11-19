using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CalmMouse.Win.Input;
using CalmMouse.Win.Settings;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

/// <summary>The running app as the settings window sees it, made up for tests.</summary>
internal sealed class FakeHost : ISettingsHost
{
    public IReadOnlyList<MouseDeviceInfo> Devices { get; set; } = [];

    public event EventHandler? DevicesChanged;

    public EngineHealth Health { get; set; } = EngineHealth.Fine;

    public string? ConflictMessage { get; set; }

    public event EventHandler? ConflictsChanged;

    public List<bool> PauseRequests { get; } = [];

    public int Retries { get; private set; }

    public void SetPaused(bool paused) => PauseRequests.Add(paused);

    public void RetryEngine() => Retries++;

    public void ChangeDevices(params MouseDeviceInfo[] devices)
    {
        Devices = devices;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeConflicts(string? message)
    {
        ConflictMessage = message;
        ConflictsChanged?.Invoke(this, EventArgs.Empty);
    }

    public int DevicesChangedHandlers => DevicesChanged?.GetInvocationList().Length ?? 0;
}

/// <summary>Apps without asking Windows.</summary>
internal sealed class FakeApps : IAppCatalog
{
    public List<AppCandidate> Running { get; } = [];

    public IReadOnlyList<AppCandidate> RunningApps() => Running;

    public AppCandidate Describe(string exePath) =>
        new(Path.GetFileName(exePath).ToLowerInvariant(), Path.GetFileNameWithoutExtension(exePath), exePath);

    public ImageSource? IconFor(string exeName, string? path) => null;
}

/// <summary>A settings file in a temp folder (never the real %AppData% one), deleted afterwards.</summary>
internal sealed class TempStore : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"CalmMouse.Tests-{Guid.NewGuid():N}");

    public TempStore() => Store = new SettingsStore(Path.Combine(_folder, SettingsStore.FileName), saveDelay: TimeSpan.FromHours(1));

    public SettingsStore Store { get; }

    public void Dispose()
    {
        Store.Dispose();
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}

/// <summary>
/// One STA thread with a running Dispatcher for every test that needs WPF objects (an
/// Application can exist only once per process, so they share it).
/// </summary>
internal static class WpfThread
{
    private static readonly Lazy<Dispatcher> s_dispatcher = new(Start);

    public static void Run(Action action) => s_dispatcher.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => s_dispatcher.Value.Invoke(func);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WPF tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>Lets queued work (layout, BeginInvoke) run.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
