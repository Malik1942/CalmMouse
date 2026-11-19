using CalmMouse.Win.Input;

namespace CalmMouse.Win.SettingsUI;

/// <summary>A problem worth a line in General: the same plain words as the tray's.</summary>
/// <param name="Text">What's wrong, or null when all is well.</param>
/// <param name="CanRetry">"Try again" can do something (the engine stopped or couldn't start).</param>
public sealed record EngineHealth(string? Text, bool CanRetry)
{
    public static readonly EngineHealth Fine = new(null, false);
}

/// <summary>
/// What the settings window needs from the running app besides the settings file: the mice the
/// engine sees, its health, the conflict notice, and the actions that go through the app.
/// The app implements it (App.SettingsHost); tests use a fake.
/// </summary>
public interface ISettingsHost
{
    /// <summary>The mice the engine knows about now, touchpads included.</summary>
    IReadOnlyList<MouseDeviceInfo> Devices { get; }

    /// <summary>A mouse came or went. Any thread.</summary>
    event EventHandler? DevicesChanged;

    /// <summary>The engine's state, in plain words.</summary>
    EngineHealth Health { get; }

    /// <summary>"SmoothScroll can also change how your mouse scrolls. …", or null.</summary>
    string? ConflictMessage { get; }

    /// <summary>The conflict notice changed. Any thread.</summary>
    event EventHandler? ConflictsChanged;

    /// <summary>Pause or resume, exactly as the tray does (resuming after a problem tries again too).</summary>
    void SetPaused(bool paused);

    /// <summary>Starts the engine again after a problem.</summary>
    void RetryEngine();
}
