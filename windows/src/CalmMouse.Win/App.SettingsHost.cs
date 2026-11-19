// What the settings window sees of the running app (SettingsUI.ISettingsHost): the engine's mice,
// its health in the tray's words, the conflict notice, and Pause / Try again as the tray does them.

using CalmMouse.Win.Input;
using CalmMouse.Win.SettingsUI;
using CalmMouse.Win.Tray;

namespace CalmMouse.Win;

public partial class App
{
    private sealed class SettingsHost(App app) : ISettingsHost
    {
        public IReadOnlyList<MouseDeviceInfo> Devices => app._engine?.Devices ?? [];

        public event EventHandler? DevicesChanged
        {
            add { if (app._engine is { } engine) { engine.DevicesChanged += Forward(value); } }
            remove { if (app._engine is { } engine) { engine.DevicesChanged -= Forward(value); } }
        }

        public EngineHealth Health
        {
            get
            {
                if (app._store is not { } store || app._engine is not { } engine) { return EngineHealth.Fine; }
                TrayView view = app.CurrentTrayView(store, engine);
                return view.State == TrayState.Problem ? new EngineHealth(view.ProblemText, view.CanRetry) : EngineHealth.Fine;
            }
        }

        public string? ConflictMessage => app._conflicts?.Message;

        public event EventHandler? ConflictsChanged
        {
            add { if (app._conflicts is { } conflicts) { conflicts.ConflictsChanged += value; } }
            remove { if (app._conflicts is { } conflicts) { conflicts.ConflictsChanged -= value; } }
        }

        public string? LaunchAtLoginNote => app.LaunchAtLoginNote();

        public void SetPaused(bool paused)
        {
            if (app._store is { } store && store.Current.Paused != paused) { app.TogglePause(); }
        }

        public void RetryEngine() => app.RetryEngine();

        // The engine's event carries the list; the window reads it again itself. One adapter per
        // handler, so removing finds the same one.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EventHandler, EventHandler<IReadOnlyList<MouseDeviceInfo>>> s_adapters = new();

        private static EventHandler<IReadOnlyList<MouseDeviceInfo>> Forward(EventHandler? handler) =>
            handler == null ? (_, _) => { } : s_adapters.GetValue(handler, h => (sender, _) => h(sender, EventArgs.Empty));
    }
}
