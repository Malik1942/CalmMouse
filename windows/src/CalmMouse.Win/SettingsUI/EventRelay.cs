namespace CalmMouse.Win.SettingsUI;

/// <summary>
/// Passes a source event on as a plain <see cref="EventHandler"/>, to listeners that come and go
/// (a settings window opened and closed many times). The source is subscribed to once, with one
/// delegate, while anyone listens, and let go when the last one leaves, so nothing a closed window
/// subscribed stays reachable from the source.
/// </summary>
public sealed class EventRelay<TArgs>
{
    private readonly Action<EventHandler<TArgs>> _subscribe;
    private readonly Action<EventHandler<TArgs>> _unsubscribe;
    private readonly EventHandler<TArgs> _relay;
    private readonly Lock _lock = new();
    private EventHandler? _listeners;

    /// <param name="subscribe">Adds the relay's handler to the source (for example <c>h => engine.DevicesChanged += h</c>).</param>
    /// <param name="unsubscribe">Removes it again.</param>
    public EventRelay(Action<EventHandler<TArgs>> subscribe, Action<EventHandler<TArgs>> unsubscribe)
    {
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _relay = OnSource;   // one delegate, so removing it always finds it
    }

    /// <summary>How many listeners there are now.</summary>
    public int ListenerCount
    {
        get { lock (_lock) { return _listeners?.GetInvocationList().Length ?? 0; } }
    }

    public void Add(EventHandler? listener)
    {
        if (listener == null) { return; }
        lock (_lock)
        {
            bool first = _listeners == null;
            _listeners += listener;
            if (first) { _subscribe(_relay); }
        }
    }

    public void Remove(EventHandler? listener)
    {
        if (listener == null) { return; }
        lock (_lock)
        {
            if (_listeners == null) { return; }
            _listeners -= listener;
            if (_listeners == null) { _unsubscribe(_relay); }
        }
    }

    private void OnSource(object? sender, TArgs args)
    {
        EventHandler? listeners;
        lock (_lock) { listeners = _listeners; }
        listeners?.Invoke(sender, EventArgs.Empty);
    }
}
