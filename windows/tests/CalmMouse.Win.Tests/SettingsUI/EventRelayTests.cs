using System.Reflection;
using CalmMouse.Win.Input;
using CalmMouse.Win.Localization;
using CalmMouse.Win.SettingsUI;

namespace CalmMouse.Win.Tests.SettingsUI;

public sealed class EventRelayTests
{
    /// <summary>The engine's DevicesChanged handlers, read from its backing field (the engine isn't started).</summary>
    private static int EngineHandlers(InputThread engine) =>
        (typeof(InputThread).GetField(nameof(InputThread.DevicesChanged), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(engine) as Delegate)?.GetInvocationList().Length ?? 0;

    [Fact]
    public void ClosingEveryWindowLetsGoOfTheEngine()
    {
        using var engine = new InputThread();
        var relay = new EventRelay<IReadOnlyList<MouseDeviceInfo>>(h => engine.DevicesChanged += h, h => engine.DevicesChanged -= h);
        Assert.Equal(0, EngineHandlers(engine));

        // Each window subscribes a method group: a new delegate every time, equal but not the same object.
        var windows = new List<Listener>();
        for (int i = 0; i < 3; i++)
        {
            var listener = new Listener();
            relay.Add(listener.OnChanged);
            windows.Add(listener);
        }
        Assert.Equal(1, EngineHandlers(engine));   // one subscription, however many windows
        Assert.Equal(3, relay.ListenerCount);

        foreach (Listener listener in windows) { relay.Remove(listener.OnChanged); }
        Assert.Equal(0, relay.ListenerCount);
        Assert.Equal(0, EngineHandlers(engine));   // nothing left for the engine to keep alive
    }

    [Fact]
    public void PassesTheSourceEventOn()
    {
        EventHandler<string>? source = null;
        var relay = new EventRelay<string>(h => source += h, h => source -= h);
        var listener = new Listener();
        relay.Add(listener.OnChanged);
        source?.Invoke(this, "list");
        Assert.Equal(1, listener.Calls);
        relay.Remove(listener.OnChanged);
        relay.Remove(listener.OnChanged);   // twice: harmless
        Assert.Null(source);
    }

    private sealed class Listener
    {
        public int Calls { get; private set; }

        public void OnChanged(object? sender, EventArgs e) => Calls++;
    }

    // MARK: Explanations as running text

    [Theory]
    [InlineData("Flips only the wheel's direction.\nOther devices stay as they are.", UiLanguage.English, "Flips only the wheel's direction. Other devices stay as they are.")]
    [InlineData("只反转滚轮的方向。\n其他设备不受影响。", UiLanguage.SimplifiedChinese, "只反转滚轮的方向。其他设备不受影响。")]
    [InlineData("登录 Windows 时\nCalmMouse 自动启动。", UiLanguage.SimplifiedChinese, "登录 Windows 时 CalmMouse 自动启动。")]
    [InlineData("按 Windows\n键", UiLanguage.TraditionalChinese, "按 Windows 键")]
    [InlineData("用 2\n根手指", UiLanguage.TraditionalChinese, "用 2 根手指")]
    [InlineData("No breaks", UiLanguage.English, "No breaks")]
    public void LineBreaksBecomeSpacesOnlyWhereTheLanguageNeedsThem(string text, UiLanguage language, string expected)
    {
        Assert.Equal(expected, OneLine.Join(text, language));
    }
}
