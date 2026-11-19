using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CalmMouse.Win.Input;
using CalmMouse.Win.Settings;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

/// <summary>
/// The running app as the settings window sees it, made up for tests. Its device event goes
/// through the same EventRelay the app uses, from an engine-like source event.
/// </summary>
internal sealed class FakeHost : ISettingsHost
{
    private readonly EventRelay<IReadOnlyList<MouseDeviceInfo>> _devices;

    public FakeHost() => _devices = new(h => EngineDevicesChanged += h, h => EngineDevicesChanged -= h);

    /// <summary>Stands in for InputThread.DevicesChanged.</summary>
    private event EventHandler<IReadOnlyList<MouseDeviceInfo>>? EngineDevicesChanged;

    public IReadOnlyList<MouseDeviceInfo> Devices { get; set; } = [];

    public event EventHandler? DevicesChanged
    {
        add => _devices.Add(value);
        remove => _devices.Remove(value);
    }

    public EngineHealth Health { get; set; } = EngineHealth.Fine;

    public string? ConflictMessage { get; set; }

    public event EventHandler? ConflictsChanged;

    public string? LaunchAtLoginNote { get; set; }

    public List<bool> PauseRequests { get; } = [];

    public int Retries { get; private set; }

    public void SetPaused(bool paused) => PauseRequests.Add(paused);

    public void RetryEngine() => Retries++;

    public void ChangeDevices(params MouseDeviceInfo[] devices)
    {
        Devices = devices;
        EngineDevicesChanged?.Invoke(this, devices);
    }

    public void ChangeConflicts(string? message)
    {
        ConflictMessage = message;
        ConflictsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Listeners on the relay.</summary>
    public int DevicesChangedHandlers => _devices.ListenerCount;

    /// <summary>Handlers on the engine-like source: the relay's one, or none.</summary>
    public int EngineHandlers => EngineDevicesChanged?.GetInvocationList().Length ?? 0;
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
