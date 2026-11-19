// One CalmMouse per user. The first one to start creates a named mutex, Local\CalmMouse-<user SID>,
// and two named events next to it, all three open to the current user only. A second launch finds
// the mutex, sets the "open Settings" event (so double-clicking the app again shows its window) and
// exits. `CalmMouse.Win.exe --quit` sets the "quit" event instead and waits for the running copy to
// be gone, which an installer or updater can use to close the app.
//
// Local\ keeps it to this sign-in session; the SID keeps apart a copy another user starts in the
// same session ("Run as different user").

using System.Security.AccessControl;
using System.Security.Principal;

namespace CalmMouse.Win.Settings;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSettings;
    private readonly EventWaitHandle _quit;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _quitWait;
    private readonly Lock _lock = new();
    private EventHandler? _showSettingsRequested;
    private EventHandler? _quitRequested;
    private bool _pendingShow;
    private bool _pendingQuit;
    private bool _disposed;

    /// <summary>"CalmMouse-" and the current user's SID.</summary>
    public static string DefaultName => $"CalmMouse-{CurrentUser.Value}";

    private static SecurityIdentifier CurrentUser
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.User ?? throw new InvalidOperationException("The current user has no SID.");
        }
    }

    private SingleInstance(Mutex mutex, EventWaitHandle showSettings, EventWaitHandle quit, bool eventsWereNew)
    {
        _mutex = mutex;
        _showSettings = showSettings;
        _quit = quit;
        EventsWereNew = eventsWereNew;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSettings, (_, _) => Raise(quit: false), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => Raise(quit: true), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// False if an event with our name was already there when this instance started (left by a
    /// launch that was just exiting, most likely). It is used all the same; the app logs it.
    /// </summary>
    public bool EventsWereNew { get; }

    /// <summary>
    /// Another launch asked for the Settings window. Raised on a thread-pool thread. A request that
    /// arrives before anyone listens is kept and raised as soon as a handler is added.
    /// </summary>
    public event EventHandler? ShowSettingsRequested
    {
        add => Subscribe(ref _showSettingsRequested, ref _pendingShow, value);
        remove { lock (_lock) { _showSettingsRequested -= value; } }
    }

    /// <summary>Someone ran <c>--quit</c>. Raised on a thread-pool thread; kept until a handler is added, as above.</summary>
    public event EventHandler? QuitRequested
    {
        add => Subscribe(ref _quitRequested, ref _pendingQuit, value);
        remove { lock (_lock) { _quitRequested -= value; } }
    }

    private void Subscribe(ref EventHandler? field, ref bool pending, EventHandler? handler)
    {
        bool replay;
        lock (_lock)
        {
            field += handler;
            replay = pending && handler != null;
            if (replay) { pending = false; }
        }
        if (replay) { ThreadPool.QueueUserWorkItem(_ => handler!(this, EventArgs.Empty)); }
    }

    private void Raise(bool quit)
    {
        EventHandler? handler;
        lock (_lock)
        {
            handler = quit ? _quitRequested : _showSettingsRequested;
            if (handler == null)
            {
                if (quit) { _pendingQuit = true; } else { _pendingShow = true; }
                return;
            }
        }
        handler(this, EventArgs.Empty);
    }

    public static string MutexName(string name) => $@"Local\{name}";
    public static string ShowSettingsEventName(string name) => $@"Local\{name}-settings";
    public static string QuitEventName(string name) => $@"Local\{name}-quit";

    /// <summary>This process becomes the one instance, or null if another already is.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        SecurityIdentifier user = CurrentUser;
        var mutexSecurity = new MutexSecurity();
        mutexSecurity.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = MutexAcl.Create(initiallyOwned: false, MutexName(name), out createdNew, mutexSecurity);
        }
        catch (UnauthorizedAccessException)
        {
            return null;   // there, and not ours to open: someone is running
        }
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        EventWaitHandle show = CreateEvent(ShowSettingsEventName(name), user, out bool showNew);
        EventWaitHandle quit = CreateEvent(QuitEventName(name), user, out bool quitNew);
        return new SingleInstance(mutex, show, quit, showNew && quitNew);
    }

    private static EventWaitHandle CreateEvent(string eventName, SecurityIdentifier user, out bool createdNew)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, eventName, out createdNew, security);
    }

    /// <summary>Asks the running instance to open Settings. False if none answers within <paramref name="timeout"/>.</summary>
    public static bool SignalShowSettings(string name, TimeSpan? timeout = null) => Signal(ShowSettingsEventName(name), timeout);

    /// <summary>Asks the running instance to quit. False if none is running.</summary>
    public static bool SignalQuit(string name, TimeSpan? timeout = null) => Signal(QuitEventName(name), timeout);

    /// <summary>Whether an instance holds the mutex now.</summary>
    public static bool IsRunning(string name)
    {
        if (!Mutex.TryOpenExisting(MutexName(name), out Mutex? mutex)) { return false; }
        mutex.Dispose();
        return true;
    }

    /// <summary>Waits until no instance holds the mutex. False if one still does after <paramref name="timeout"/>.</summary>
    public static bool WaitForExit(string name, TimeSpan timeout)
    {
        DateTime giveUp = DateTime.UtcNow + timeout;
        while (IsRunning(name))
        {
            if (DateTime.UtcNow >= giveUp) { return false; }
            Thread.Sleep(50);
        }
        return true;
    }

    /// <summary>
    /// Tries to become the instance for up to <paramref name="timeout"/>: for a launch that found
    /// one running but got no answer from it (it was quitting).
    /// </summary>
    public static SingleInstance? TryAcquire(string name, TimeSpan timeout)
    {
        DateTime giveUp = DateTime.UtcNow + timeout;
        while (true)
        {
            if (TryAcquire(name) is { } instance) { return instance; }
            if (DateTime.UtcNow >= giveUp) { return null; }
            Thread.Sleep(100);
        }
    }

    private static bool Signal(string eventName, TimeSpan? timeout)
    {
        // The first instance creates its events right after its mutex, so a launch racing a
        // starting instance waits a moment for them.
        DateTime giveUp = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
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
