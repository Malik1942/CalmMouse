using CalmMouse.Core;
using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Input;

/// <summary>
/// Where the input thread gets each mouse's settings and the app rules from. The settings file
/// (plan task 13) will implement this; until then <see cref="InMemoryWheelSettingsProvider"/>.
/// Called from the input thread, so implementations must be thread-safe and quick.
/// </summary>
public interface IWheelSettingsProvider
{
    /// <summary>The settings for the mouse with this <see cref="MouseDeviceInfo.StableKey"/>.</summary>
    WheelSettings SettingsFor(string stableKey);

    IReadOnlyList<WheelAppRule> AppRules { get; }

    /// <summary>Raised (on any thread) when a mouse's settings or the app rules changed.</summary>
    event EventHandler? Changed;
}

/// <summary>Settings held in memory. Everything starts off: a mouse nobody set up scrolls as it always did.</summary>
public sealed class InMemoryWheelSettingsProvider : IWheelSettingsProvider
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, WheelSettings> _settings = new(StringComparer.Ordinal);
    private IReadOnlyList<WheelAppRule> _rules = [];

    public event EventHandler? Changed;

    public WheelSettings SettingsFor(string stableKey)
    {
        lock (_lock)
        {
            return _settings.TryGetValue(stableKey, out WheelSettings s) ? s : new WheelSettings();
        }
    }

    public IReadOnlyList<WheelAppRule> AppRules
    {
        get { lock (_lock) { return _rules; } }
    }

    public void Set(string stableKey, WheelSettings settings)
    {
        lock (_lock) { _settings[stableKey] = settings; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAppRules(IEnumerable<WheelAppRule> rules)
    {
        lock (_lock) { _rules = rules.ToArray(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
