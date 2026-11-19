// One CalmMouse per user. The first one to start creates a named mutex, Local\CalmMouse-<user SID>,
// and two named events next to it. A second launch finds the mutex, sets the "open Settings" event
// (so double-clicking the app again shows its window) and exits. `CalmMouse.Win.exe --quit` sets
// the "quit" event instead, which an installer or updater can use to close the running app.
//
// Local\ keeps it to this sign-in session; the SID keeps apart a copy another user starts in the
// same session ("Run as different user").

using System.Security.Principal;

namespace CalmMouse.Win.Settings;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSettings;
    private readonly EventWaitHandle _quit;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _quitWait;
    private bool _disposed;

    /// <summary>"CalmMouse-" and the current user's SID.</summary>
    public static string DefaultName
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return $"CalmMouse-{identity.User?.Value ?? Environment.UserName}";
        }
    }

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName(name));
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName(name));
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSettings, (_, _) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Another launch asked for the Settings window. Raised on a thread-pool thread.</summary>
    public event EventHandler? ShowSettingsRequested;

    /// <summary>Someone ran <c>--quit</c>. Raised on a thread-pool thread.</summary>
    public event EventHandler? QuitRequested;

    public static string MutexName(string name) => $@"Local\{name}";
    public static string ShowSettingsEventName(string name) => $@"Local\{name}-settings";
    public static string QuitEventName(string name) => $@"Local\{name}-quit";

    /// <summary>This process becomes the one instance, or null if another already is.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName(name), out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, name);
    }

    /// <summary>Asks the running instance to open Settings. False if none answers within <paramref name="timeout"/>.</summary>
    public static bool SignalShowSettings(string name, TimeSpan? timeout = null) => Signal(ShowSettingsEventName(name), timeout);

    /// <summary>Asks the running instance to quit. False if none is running.</summary>
    public static bool SignalQuit(string name, TimeSpan? timeout = null) => Signal(QuitEventName(name), timeout);

    private static bool Signal(string eventName, TimeSpan? timeout)
    {
        // The first instance creates its events right after its mutex, so a launch racing a
        // starting instance waits a moment for them.
        DateTime giveUp = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (true)
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out EventWaitHandle? handle))
            {
                using (handle) { handle.Set(); }
                return true;
            }
            if (DateTime.UtcNow >= giveUp) { return false; }
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _showWait.Unregister(null);
        _quitWait.Unregister(null);
        _showSettings.Dispose();
        _quit.Dispose();
        _mutex.Dispose();
    }
}
