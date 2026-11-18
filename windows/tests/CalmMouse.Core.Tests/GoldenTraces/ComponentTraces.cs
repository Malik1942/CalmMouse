// Runs golden-trace steps through each C# port. One class per component, each the counterpart of
// the enum with the same name in Tests/CalmMouseCoreTests/GoldenTraceTests.swift: same fields,
// same defaults, same outputs.

using System.Text.Json.Nodes;

namespace CalmMouse.Core.Tests;

internal static class ScrollBlockerTrace
{
    private static readonly string[] BaseKeys =
    [
        "blockScrollWhileClicked", "releaseGrace", "deadZone", "axisLock", "axisLockThreshold",
        "axisLockRatio", "momentumEnabled", "blockHorizontalScroll", "disableScrollEntirely",
        "modifierActions", "reverseMagicMouseScroll", "reverseWheelScroll",
        "blockScrollWhileWheelPressed", "smoothWheelScroll", "wheelScrollSpeed",
    ];
    private static readonly string[] RuleKeys =
    [
        "bundleID", "name", "enabled", "blockScrollWhileClicked", "disableScrollEntirely",
        "blockHorizontalScroll", "momentumEnabled", "axisLock", "deadZone",
    ];
    private static readonly string[] WheelKeys = ["reverse", "blockWhilePressed", "smooth", "speed"];
    private static readonly string[] ScrollKeys =
    [
        "op", "t", "fromMagicMouse", "fromWheelMouse", "isContinuous", "phase", "momentum",
        "deltaX", "deltaY", "modifiers", "wheel",
    ];

    public static TraceRun Run(JsonNode? config, JsonArray steps, bool strict)
    {
        RuleSet rules = ReadRules(config, strict);
        var blocker = new ScrollBlocker(rules);
        // Steps without a time (setActiveApp, liftBlockingImmediately) report the blocking state
        // at the time of the step before them, 0 at the start.
        double now = 0;
        var stepsOut = new JsonArray();
        var outputs = new JsonArray();
        for (int i = 0; i < steps.Count; i++)
        {
            string context = $"steps[{i}]";
            string op = Fields.Op(steps[i], context);
            JsonNode? decision = null;
            JsonNode? interruptsGlide = null;
            switch (op)
            {
                case "setActiveApp":
                {
                    var f = new Fields(steps[i], context, strict, "op", "bundleID");
                    string? bundleID = f.GetOptionalString("bundleID");
                    blocker.SetActiveApp(bundleID);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["bundleID"] = bundleID });
                    break;
                }
                case "magicMouseButton":
                case "wheelMouseButton":
                {
                    var f = new Fields(steps[i], context, strict, "op", "button", "isDown", "t");
                    long button = f.GetLong("button", null);
                    bool isDown = f.GetBool("isDown", null);
                    now = f.GetDouble("t", null);
                    if (op == "magicMouseButton")
                    {
                        blocker.MagicMouseButton(button, isDown, now);
                    }
                    else
                    {
                        blocker.WheelMouseButton(button, isDown, now);
                    }
                    stepsOut.Add(new JsonObject { ["op"] = op, ["button"] = button, ["isDown"] = isDown, ["t"] = now });
                    break;
                }
                case "scroll":
                {
                    var f = new Fields(steps[i], context, strict, ScrollKeys);
                    ScrollEvent e = ReadEvent(f, strict);
                    now = e.Timestamp;
                    decision = DecisionJson(blocker.HandleScroll(e));
                    interruptsGlide = ScrollBlocker.InterruptsGlide(e);
                    stepsOut.Add(EventJson(e));
                    break;
                }
                case "liftBlockingImmediately":
                {
                    _ = new Fields(steps[i], context, strict, "op");
                    blocker.LiftBlockingImmediately();
                    stepsOut.Add(new JsonObject { ["op"] = op });
                    break;
                }
                case "forceReleaseAllButtons":
                {
                    var f = new Fields(steps[i], context, strict, "op", "t");
                    now = f.GetDouble("t", null);
                    blocker.ForceReleaseAllButtons(now);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["t"] = now });
                    break;
                }
                default:
                    throw new TraceException($"{context}: unknown op \"{op}\" for ScrollBlocker");
            }
            outputs.Add(new JsonObject
            {
                ["decision"] = decision,
                ["interruptsGlide"] = interruptsGlide,
                ["isBlocking"] = blocker.IsBlocking(now),
                ["isBlockingWheel"] = blocker.IsBlockingWheel(now),
                ["anyButtonDown"] = blocker.AnyButtonDown,
            });
        }
        return new TraceRun(RulesJson(rules), stepsOut, outputs);
    }

    /// <summary>
    /// Read from explicit fields, not BlockerConfig's JSON converter: like Swift's Codable, that
    /// one leaves out reverseMagicMouseScroll.
    /// </summary>
    private static RuleSet ReadRules(JsonNode? value, bool strict)
    {
        var d = new BlockerConfig();
        var b = new BlockerConfig();
        var appRules = new List<AppRule>();
        if (value is null)
        {
            return strict ? throw new TraceException("config: missing") : new RuleSet(b, appRules);
        }
        var f = new Fields(value, "config", strict, "base", "appRules");
        if (f.Value("base") is JsonNode baseNode)
        {
            var g = new Fields(baseNode, "config.base", strict, BaseKeys);
            b.BlockScrollWhileClicked = g.GetBool("blockScrollWhileClicked", d.BlockScrollWhileClicked);
            b.ReleaseGrace = g.GetDouble("releaseGrace", d.ReleaseGrace);
            b.DeadZone = g.GetDouble("deadZone", d.DeadZone);
            b.AxisLock = g.GetBool("axisLock", d.AxisLock);
            b.AxisLockThreshold = g.GetDouble("axisLockThreshold", d.AxisLockThreshold);
            b.AxisLockRatio = g.GetDouble("axisLockRatio", d.AxisLockRatio);
            b.MomentumEnabled = g.GetBool("momentumEnabled", d.MomentumEnabled);
            b.BlockHorizontalScroll = g.GetBool("blockHorizontalScroll", d.BlockHorizontalScroll);
            b.DisableScrollEntirely = g.GetBool("disableScrollEntirely", d.DisableScrollEntirely);
            if (g.Value("modifierActions") is JsonNode actions)
            {
                b.ModifierActions = ReadModifierActions(actions, strict);
            }
            b.ReverseMagicMouseScroll = g.GetBool("reverseMagicMouseScroll", d.ReverseMagicMouseScroll);
            b.ReverseWheelScroll = g.GetBool("reverseWheelScroll", d.ReverseWheelScroll);
            b.BlockScrollWhileWheelPressed = g.GetBool("blockScrollWhileWheelPressed", d.BlockScrollWhileWheelPressed);
            b.SmoothWheelScroll = g.GetBool("smoothWheelScroll", d.SmoothWheelScroll);
            b.WheelScrollSpeed = g.GetDouble("wheelScrollSpeed", d.WheelScrollSpeed);
        }
        if (f.Value("appRules") is JsonNode rulesNode)
        {
            if (rulesNode is not JsonArray items)
            {
                throw new TraceException("config.appRules must be an array");
            }
            for (int i = 0; i < items.Count; i++)
            {
                var g = new Fields(items[i], $"config.appRules[{i}]", strict, RuleKeys);
                appRules.Add(new AppRule(
                    bundleID: g.GetString("bundleID", null),
                    name: g.GetString("name", ""),
                    enabled: g.GetBool("enabled", true),
                    blockScrollWhileClicked: g.GetOptionalBool("blockScrollWhileClicked"),
                    disableScrollEntirely: g.GetOptionalBool("disableScrollEntirely"),
                    blockHorizontalScroll: g.GetOptionalBool("blockHorizontalScroll"),
                    momentumEnabled: g.GetOptionalBool("momentumEnabled"),
                    axisLock: g.GetOptionalBool("axisLock"),
                    deadZone: g.GetOptionalDouble("deadZone")));
            }
        }
        return new RuleSet(b, appRules);
    }

    private static Dictionary<ModifierCombo, ScrollAction> ReadModifierActions(JsonNode value, bool strict)
    {
        if (value is not JsonArray items)
        {
            throw new TraceException("config.base.modifierActions must be an array");
        }
        var actions = new Dictionary<ModifierCombo, ScrollAction>();
        for (int i = 0; i < items.Count; i++)
        {
            string context = $"config.base.modifierActions[{i}]";
            var f = new Fields(items[i], context, strict, "modifiers", "action");
            long raw = f.GetLong("modifiers", null);
            ScrollAction action = TraceNames.Action(f.GetString("action", null), context);
            if (!actions.TryAdd((ModifierCombo)raw, action))
            {
                throw new TraceException($"{context}: modifiers {raw} are listed twice");
            }
        }
        return actions;
    }

    private static JsonObject RulesJson(RuleSet rules)
    {
        BlockerConfig c = rules.Base;
        var actions = new JsonArray();
        foreach (ModifierCombo modifiers in c.ModifierActions.Keys.OrderBy(m => (int)m))
        {
            actions.Add(new JsonObject { ["modifiers"] = (int)modifiers, ["action"] = TraceNames.Action(c.ModifierActions[modifiers]) });
        }
        var appRules = new JsonArray();
        foreach (AppRule r in rules.AppRules)
        {
            appRules.Add(new JsonObject
            {
                ["bundleID"] = r.BundleID,
                ["name"] = r.Name,
                ["enabled"] = r.Enabled,
                ["blockScrollWhileClicked"] = r.BlockScrollWhileClicked,
                ["disableScrollEntirely"] = r.DisableScrollEntirely,
                ["blockHorizontalScroll"] = r.BlockHorizontalScroll,
                ["momentumEnabled"] = r.MomentumEnabled,
                ["axisLock"] = r.AxisLock,
                ["deadZone"] = r.DeadZone,
            });
        }
        return new JsonObject
        {
            ["base"] = new JsonObject
            {
                ["blockScrollWhileClicked"] = c.BlockScrollWhileClicked,
                ["releaseGrace"] = c.ReleaseGrace,
                ["deadZone"] = c.DeadZone,
                ["axisLock"] = c.AxisLock,
                ["axisLockThreshold"] = c.AxisLockThreshold,
                ["axisLockRatio"] = c.AxisLockRatio,
                ["momentumEnabled"] = c.MomentumEnabled,
                ["blockHorizontalScroll"] = c.BlockHorizontalScroll,
                ["disableScrollEntirely"] = c.DisableScrollEntirely,
                ["modifierActions"] = actions,
                ["reverseMagicMouseScroll"] = c.ReverseMagicMouseScroll,
                ["reverseWheelScroll"] = c.ReverseWheelScroll,
                ["blockScrollWhileWheelPressed"] = c.BlockScrollWhileWheelPressed,
                ["smoothWheelScroll"] = c.SmoothWheelScroll,
                ["wheelScrollSpeed"] = c.WheelScrollSpeed,
            },
            ["appRules"] = appRules,
        };
    }

    private static ScrollEvent ReadEvent(Fields f, bool strict)
    {
        WheelSettings? wheel = null;
        if (f.Value("wheel") is JsonNode w)
        {
            var g = new Fields(w, $"{f.Context}.wheel", strict, WheelKeys);
            var d = new WheelSettings();
            wheel = new WheelSettings(
                reverse: g.GetBool("reverse", d.Reverse),
                blockWhilePressed: g.GetBool("blockWhilePressed", d.BlockWhilePressed),
                smooth: g.GetBool("smooth", d.Smooth),
                speed: g.GetDouble("speed", d.Speed));
        }
        return new ScrollEvent(
            timestamp: f.GetDouble("t", null),
            fromMagicMouse: f.GetBool("fromMagicMouse", true),
            isContinuous: f.GetBool("isContinuous", true),
            phase: TraceNames.Phase(f.GetString("phase", "none"), f.Context),
            momentum: TraceNames.Momentum(f.GetString("momentum", "none"), f.Context),
            deltaX: f.GetDouble("deltaX", 0),
            deltaY: f.GetDouble("deltaY", 0),
            modifiers: (ModifierCombo)f.GetLong("modifiers", 0),
            fromWheelMouse: f.GetBool("fromWheelMouse", false),
            wheel: wheel);
    }

    private static JsonObject EventJson(ScrollEvent e) => new()
    {
        ["op"] = "scroll",
        ["t"] = e.Timestamp,
        ["fromMagicMouse"] = e.FromMagicMouse,
        ["fromWheelMouse"] = e.FromWheelMouse,
        ["isContinuous"] = e.IsContinuous,
        ["phase"] = TraceNames.Phase(e.Phase),
        ["momentum"] = TraceNames.Momentum(e.Momentum),
        ["deltaX"] = e.DeltaX,
        ["deltaY"] = e.DeltaY,
        ["modifiers"] = (int)e.Modifiers,
        ["wheel"] = e.Wheel is WheelSettings w
            ? new JsonObject { ["reverse"] = w.Reverse, ["blockWhilePressed"] = w.BlockWhilePressed, ["smooth"] = w.Smooth, ["speed"] = w.Speed }
            : null,
    };

    private static JsonObject DecisionJson(ScrollDecision decision) => decision switch
    {
        ScrollDecision.Pass => new JsonObject { ["kind"] = "pass" },
        ScrollDecision.Drop => new JsonObject { ["kind"] = "drop" },
        ScrollDecision.Rewrite(ScrollRewrite r) => new JsonObject
        {
            ["kind"] = "rewrite",
            ["phase"] = TraceNames.Phase(r.Phase),
            ["momentum"] = TraceNames.Momentum(r.Momentum),
            ["zeroX"] = r.ZeroX,
            ["zeroY"] = r.ZeroY,
            ["swapAxes"] = r.SwapAxes,
            ["invert"] = r.Invert,
            ["addModifiers"] = (int)r.AddModifiers,
            ["removeModifiers"] = (int)r.RemoveModifiers,
        },
        ScrollDecision.Smooth(double dx, double dy) => new JsonObject { ["kind"] = "smooth", ["dx"] = dx, ["dy"] = dy },
        _ => throw new TraceException($"unknown scroll decision {decision}"),
    };
}

internal static class WheelSmootherTrace
{
    public static TraceRun Run(JsonNode? config, JsonArray steps, bool strict)
    {
        var d = new WheelSmoother();
        double timeConstant = d.TimeConstant;
        double minimumSpeed = d.MinimumSpeed;
        if (config is not null)
        {
            var f = new Fields(config, "config", strict, "timeConstant", "minimumSpeed");
            timeConstant = f.GetDouble("timeConstant", d.TimeConstant);
            minimumSpeed = f.GetDouble("minimumSpeed", d.MinimumSpeed);
        }
        else if (strict)
        {
            throw new TraceException("config: missing");
        }
        var smoother = new WheelSmoother(timeConstant: timeConstant, minimumSpeed: minimumSpeed);
        var stepsOut = new JsonArray();
        var outputs = new JsonArray();
        for (int i = 0; i < steps.Count; i++)
        {
            string context = $"steps[{i}]";
            string op = Fields.Op(steps[i], context);
            JsonNode? result = null;
            switch (op)
            {
                case "add":
                {
                    var f = new Fields(steps[i], context, strict, "op", "dx", "dy");
                    double dx = f.GetDouble("dx", 0);
                    double dy = f.GetDouble("dy", 0);
                    smoother.Add(dx, dy);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["dx"] = dx, ["dy"] = dy });
                    break;
                }
                case "step":
                {
                    var f = new Fields(steps[i], context, strict, "op", "dt");
                    double dt = f.GetDouble("dt", null);
                    if (smoother.Step(dt) is WheelSmoother.StepResult s)
                    {
                        result = new JsonObject { ["x"] = s.X, ["y"] = s.Y };
                    }
                    stepsOut.Add(new JsonObject { ["op"] = op, ["dt"] = dt });
                    break;
                }
                case "cancel":
                {
                    _ = new Fields(steps[i], context, strict, "op");
                    smoother.Cancel();
                    stepsOut.Add(new JsonObject { ["op"] = op });
                    break;
                }
                default:
                    throw new TraceException($"{context}: unknown op \"{op}\" for WheelSmoother");
            }
            (double owedX, double owedY) = smoother.Owed;
            outputs.Add(new JsonObject
            {
                ["step"] = result,
                ["isIdle"] = smoother.IsIdle,
                ["owedX"] = owedX,
                ["owedY"] = owedY,
            });
        }
        var configOut = new JsonObject { ["timeConstant"] = timeConstant, ["minimumSpeed"] = minimumSpeed };
        return new TraceRun(configOut, stepsOut, outputs);
    }
}

internal static class ControllerPointerTrace
{
    private static readonly string[] ConfigKeys =
    [
        "pointerSpeed", "scrollSpeed", "tapToClick", "naturalScrolling", "stickClicks",
        "touchpadAspect", "touchpadGain", "touchpadScrollGain", "tapMaxDuration", "tapMaxTravel",
        "stickDeadZone", "stickMaxSpeed", "stickMaxScroll", "toggleHold", "stickClickSettle",
    ];

    public static TraceRun Run(JsonNode? config, JsonArray steps, bool strict)
    {
        ControllerConfig c = ReadConfig(config, strict);
        var pointer = new ControllerPointer(c);
        var stepsOut = new JsonArray();
        var outputs = new JsonArray();
        for (int i = 0; i < steps.Count; i++)
        {
            string context = $"steps[{i}]";
            string op = Fields.Op(steps[i], context);
            List<PointerAction> actions = [];
            switch (op)
            {
                case "setActive":
                {
                    var f = new Fields(steps[i], context, strict, "op", "active");
                    bool active = f.GetBool("active", null);
                    actions = pointer.SetActive(active);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["active"] = active });
                    break;
                }
                case "button":
                {
                    var f = new Fields(steps[i], context, strict, "op", "button", "pressed", "t");
                    ControllerButton which = TraceNames.ControllerButton(f.GetString("button", null), context);
                    bool pressed = f.GetBool("pressed", null);
                    double t = f.GetDouble("t", null);
                    actions = pointer.Button(which, pressed, t);
                    stepsOut.Add(new JsonObject
                    {
                        ["op"] = op, ["button"] = TraceNames.ControllerButton(which), ["pressed"] = pressed, ["t"] = t,
                    });
                    break;
                }
                case "touchpad":
                {
                    var f = new Fields(steps[i], context, strict, "op", "primary", "secondary", "t");
                    TouchPoint? primary = ReadPoint(f, "primary", strict);
                    TouchPoint? secondary = ReadPoint(f, "secondary", strict);
                    double t = f.GetDouble("t", null);
                    actions = pointer.Touchpad(primary, secondary, t);
                    stepsOut.Add(new JsonObject
                    {
                        ["op"] = op, ["primary"] = PointJson(primary), ["secondary"] = PointJson(secondary), ["t"] = t,
                    });
                    break;
                }
                case "sticks":
                {
                    var f = new Fields(steps[i], context, strict, "op", "left", "right");
                    TouchPoint left = ReadPoint(f, "left", strict) ?? new TouchPoint(x: 0, y: 0);
                    TouchPoint right = ReadPoint(f, "right", strict) ?? new TouchPoint(x: 0, y: 0);
                    pointer.Sticks(left, right);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["left"] = PointJson(left), ["right"] = PointJson(right) });
                    break;
                }
                case "tick":
                {
                    var f = new Fields(steps[i], context, strict, "op", "dt", "t");
                    double dt = f.GetDouble("dt", null);
                    double t = f.GetDouble("t", null);
                    actions = pointer.Tick(dt, t);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["dt"] = dt, ["t"] = t });
                    break;
                }
                default:
                    throw new TraceException($"{context}: unknown op \"{op}\" for ControllerPointer");
            }
            outputs.Add(new JsonObject
            {
                ["actions"] = new JsonArray([.. actions.Select(ActionJson)]),
                ["isActive"] = pointer.IsActive,
                ["needsTicks"] = pointer.NeedsTicks,
                ["leftHeld"] = pointer.IsHeld(PointerButton.Left),
                ["rightHeld"] = pointer.IsHeld(PointerButton.Right),
            });
        }
        return new TraceRun(ConfigJson(c), stepsOut, outputs);
    }

    private static ControllerConfig ReadConfig(JsonNode? value, bool strict)
    {
        var d = new ControllerConfig();
        var c = new ControllerConfig();
        if (value is null)
        {
            return strict ? throw new TraceException("config: missing") : c;
        }
        var f = new Fields(value, "config", strict, ConfigKeys);
        c.PointerSpeed = f.GetDouble("pointerSpeed", d.PointerSpeed);
        c.ScrollSpeed = f.GetDouble("scrollSpeed", d.ScrollSpeed);
        c.TapToClick = f.GetBool("tapToClick", d.TapToClick);
        c.NaturalScrolling = f.GetBool("naturalScrolling", d.NaturalScrolling);
        c.StickClicks = f.GetBool("stickClicks", d.StickClicks);
        c.TouchpadAspect = f.GetDouble("touchpadAspect", d.TouchpadAspect);
        c.TouchpadGain = f.GetDouble("touchpadGain", d.TouchpadGain);
        c.TouchpadScrollGain = f.GetDouble("touchpadScrollGain", d.TouchpadScrollGain);
        c.TapMaxDuration = f.GetDouble("tapMaxDuration", d.TapMaxDuration);
        c.TapMaxTravel = f.GetDouble("tapMaxTravel", d.TapMaxTravel);
        c.StickDeadZone = f.GetDouble("stickDeadZone", d.StickDeadZone);
        c.StickMaxSpeed = f.GetDouble("stickMaxSpeed", d.StickMaxSpeed);
        c.StickMaxScroll = f.GetDouble("stickMaxScroll", d.StickMaxScroll);
        c.ToggleHold = f.GetDouble("toggleHold", d.ToggleHold);
        c.StickClickSettle = f.GetDouble("stickClickSettle", d.StickClickSettle);
        return c;
    }

    private static JsonObject ConfigJson(ControllerConfig c) => new()
    {
        ["pointerSpeed"] = c.PointerSpeed,
        ["scrollSpeed"] = c.ScrollSpeed,
        ["tapToClick"] = c.TapToClick,
        ["naturalScrolling"] = c.NaturalScrolling,
        ["stickClicks"] = c.StickClicks,
        ["touchpadAspect"] = c.TouchpadAspect,
        ["touchpadGain"] = c.TouchpadGain,
        ["touchpadScrollGain"] = c.TouchpadScrollGain,
        ["tapMaxDuration"] = c.TapMaxDuration,
        ["tapMaxTravel"] = c.TapMaxTravel,
        ["stickDeadZone"] = c.StickDeadZone,
        ["stickMaxSpeed"] = c.StickMaxSpeed,
        ["stickMaxScroll"] = c.StickMaxScroll,
        ["toggleHold"] = c.ToggleHold,
        ["stickClickSettle"] = c.StickClickSettle,
    };

    private static TouchPoint? ReadPoint(Fields f, string key, bool strict)
    {
        if (f.Value(key) is not JsonNode node)
        {
            return null;
        }
        var g = new Fields(node, $"{f.Context}.{key}", strict, "x", "y");
        return new TouchPoint(x: g.GetDouble("x", null), y: g.GetDouble("y", null));
    }

    private static JsonObject? PointJson(TouchPoint? p) =>
        p is TouchPoint q ? new JsonObject { ["x"] = q.X, ["y"] = q.Y } : null;

    private static JsonNode? ActionJson(PointerAction action) => action switch
    {
        PointerAction.Move(double dx, double dy) => new JsonObject { ["kind"] = "move", ["dx"] = dx, ["dy"] = dy },
        PointerAction.Scroll(double v, double h) => new JsonObject { ["kind"] = "scroll", ["vertical"] = v, ["horizontal"] = h },
        PointerAction.Press(PointerButton b) => new JsonObject { ["kind"] = "press", ["button"] = TraceNames.PointerButton(b) },
        PointerAction.Release(PointerButton b) => new JsonObject { ["kind"] = "release", ["button"] = TraceNames.PointerButton(b) },
        PointerAction.Click(PointerButton b) => new JsonObject { ["kind"] = "click", ["button"] = TraceNames.PointerButton(b) },
        PointerAction.Toggle => new JsonObject { ["kind"] = "toggle" },
        _ => throw new TraceException($"unknown pointer action {action}"),
    };
}

internal static class TapRecognizerTrace
{
    private static readonly string[] ConfigKeys =
    [
        "enabled", "sensitivity", "maxDuration", "maxMovement", "minPeakSize", "scrollCooldown",
        "buttonCooldown", "scrollActivityThreshold", "tapZoneEnabled", "tapZoneDepth",
        "tapZoneEdgeMargin", "frontIsHighY", "tapRightClick", "rightClickMode", "rightSideStart",
        "doubleTapWindow", "doubleTapMaxDistance", "tapAndDrag", "dragWindow", "dragMaxDistance",
        "dragSwipeCancelDistance", "twoFingerDrag", "twoFingerPairWindow", "twoFingerLongPress",
        "scrollBurstWindow", "scrollBurstCancelThreshold", "pressQuietPeriod", "pairArmTTL",
        "pairRearmCooldown",
    ];

    public static TraceRun Run(JsonNode? config, JsonArray steps, bool strict)
    {
        TapConfig c = ReadConfig(config, strict);
        var recognizer = new TapRecognizer(c);
        var stepsOut = new JsonArray();
        var outputs = new JsonArray();
        for (int i = 0; i < steps.Count; i++)
        {
            string context = $"steps[{i}]";
            string op = Fields.Op(steps[i], context);
            List<TapEvent> events = [];
            switch (op)
            {
                case "physicalButton":
                {
                    var f = new Fields(steps[i], context, strict, "op", "isDown", "t");
                    bool isDown = f.GetBool("isDown", null);
                    double t = f.GetDouble("t", null);
                    events = recognizer.PhysicalButton(isDown, t);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["isDown"] = isDown, ["t"] = t });
                    break;
                }
                case "noteScroll":
                {
                    var f = new Fields(steps[i], context, strict, "op", "deltaMagnitude", "t");
                    double magnitude = f.GetDouble("deltaMagnitude", null);
                    double t = f.GetDouble("t", null);
                    events = recognizer.NoteScroll(magnitude, t);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["deltaMagnitude"] = magnitude, ["t"] = t });
                    break;
                }
                case "handleFrame":
                {
                    var f = new Fields(steps[i], context, strict, "op", "touches", "t", "cursorMoving");
                    JsonArray items = f.GetArray("touches");
                    var touches = new List<TouchSample>();
                    var touchesOut = new JsonArray();
                    for (int j = 0; j < items.Count; j++)
                    {
                        var g = new Fields(items[j], $"{context}.touches[{j}]", strict, "id", "x", "y", "size");
                        var touch = new TouchSample(
                            id: g.GetLong("id", null), x: g.GetDouble("x", null),
                            y: g.GetDouble("y", null), size: g.GetDouble("size", null));
                        touches.Add(touch);
                        touchesOut.Add(new JsonObject { ["id"] = touch.Id, ["x"] = touch.X, ["y"] = touch.Y, ["size"] = touch.Size });
                    }
                    double t = f.GetDouble("t", null);
                    bool cursorMoving = f.GetBool("cursorMoving", false);
                    events = recognizer.HandleFrame(touches, t, cursorMoving);
                    stepsOut.Add(new JsonObject { ["op"] = op, ["touches"] = touchesOut, ["t"] = t, ["cursorMoving"] = cursorMoving });
                    break;
                }
                case "disarmDrag":
                {
                    _ = new Fields(steps[i], context, strict, "op");
                    recognizer.DisarmDrag();
                    stepsOut.Add(new JsonObject { ["op"] = op });
                    break;
                }
                case "reset":
                {
                    _ = new Fields(steps[i], context, strict, "op");
                    recognizer.Reset();
                    stepsOut.Add(new JsonObject { ["op"] = op });
                    break;
                }
                default:
                    throw new TraceException($"{context}: unknown op \"{op}\" for TapRecognizer");
            }
            outputs.Add(new JsonObject
            {
                ["events"] = new JsonArray([.. events.Select(e => (JsonNode?)TraceNames.TapEvent(e))]),
                ["tapCount"] = recognizer.TapCount,
                ["dragActive"] = recognizer.DragActive,
                ["dragArmIsPair"] = recognizer.DragArmIsPair,
                ["lastRejection"] = recognizer.LastRejection is TapRecognizer.Rejection r ? TraceNames.Rejection(r) : null,
            });
        }
        return new TraceRun(ConfigJson(c), stepsOut, outputs);
    }

    /// <summary>
    /// Sensitivity goes first: setting it retunes MaxDuration, MaxMovement and ScrollCooldown,
    /// which the fields after it may then set on their own.
    /// </summary>
    private static TapConfig ReadConfig(JsonNode? value, bool strict)
    {
        var c = new TapConfig();
        if (value is null)
        {
            return strict ? throw new TraceException("config: missing") : c;
        }
        var f = new Fields(value, "config", strict, ConfigKeys);
        c.Sensitivity = f.GetDouble("sensitivity", new TapConfig().Sensitivity);
        TapConfig d = c;
        c.Enabled = f.GetBool("enabled", d.Enabled);
        c.MaxDuration = f.GetDouble("maxDuration", d.MaxDuration);
        c.MaxMovement = f.GetDouble("maxMovement", d.MaxMovement);
        c.MinPeakSize = f.GetDouble("minPeakSize", d.MinPeakSize);
        c.ScrollCooldown = f.GetDouble("scrollCooldown", d.ScrollCooldown);
        c.ButtonCooldown = f.GetDouble("buttonCooldown", d.ButtonCooldown);
        c.ScrollActivityThreshold = f.GetDouble("scrollActivityThreshold", d.ScrollActivityThreshold);
        c.TapZoneEnabled = f.GetBool("tapZoneEnabled", d.TapZoneEnabled);
        c.TapZoneDepth = f.GetDouble("tapZoneDepth", d.TapZoneDepth);
        c.TapZoneEdgeMargin = f.GetDouble("tapZoneEdgeMargin", d.TapZoneEdgeMargin);
        c.FrontIsHighY = f.GetBool("frontIsHighY", d.FrontIsHighY);
        c.TapRightClick = f.GetBool("tapRightClick", d.TapRightClick);
        c.RightClickMode = TraceNames.RightClickMode(
            f.GetString("rightClickMode", TraceNames.RightClickMode(d.RightClickMode)), "config");
        c.RightSideStart = f.GetDouble("rightSideStart", d.RightSideStart);
        c.DoubleTapWindow = f.GetDouble("doubleTapWindow", d.DoubleTapWindow);
        c.DoubleTapMaxDistance = f.GetDouble("doubleTapMaxDistance", d.DoubleTapMaxDistance);
        c.TapAndDrag = f.GetBool("tapAndDrag", d.TapAndDrag);
        c.DragWindow = f.GetDouble("dragWindow", d.DragWindow);
        c.DragMaxDistance = f.GetDouble("dragMaxDistance", d.DragMaxDistance);
        c.DragSwipeCancelDistance = f.GetDouble("dragSwipeCancelDistance", d.DragSwipeCancelDistance);
        c.TwoFingerDrag = f.GetBool("twoFingerDrag", d.TwoFingerDrag);
        c.TwoFingerPairWindow = f.GetDouble("twoFingerPairWindow", d.TwoFingerPairWindow);
        c.TwoFingerLongPress = f.GetDouble("twoFingerLongPress", d.TwoFingerLongPress);
        c.ScrollBurstWindow = f.GetDouble("scrollBurstWindow", d.ScrollBurstWindow);
        c.ScrollBurstCancelThreshold = f.GetDouble("scrollBurstCancelThreshold", d.ScrollBurstCancelThreshold);
        c.PressQuietPeriod = f.GetDouble("pressQuietPeriod", d.PressQuietPeriod);
        c.PairArmTTL = f.GetDouble("pairArmTTL", d.PairArmTTL);
        c.PairRearmCooldown = f.GetDouble("pairRearmCooldown", d.PairRearmCooldown);
        return c;
    }

    private static JsonObject ConfigJson(TapConfig c) => new()
    {
        ["enabled"] = c.Enabled,
        ["sensitivity"] = c.Sensitivity,
        ["maxDuration"] = c.MaxDuration,
        ["maxMovement"] = c.MaxMovement,
        ["minPeakSize"] = c.MinPeakSize,
        ["scrollCooldown"] = c.ScrollCooldown,
        ["buttonCooldown"] = c.ButtonCooldown,
        ["scrollActivityThreshold"] = c.ScrollActivityThreshold,
        ["tapZoneEnabled"] = c.TapZoneEnabled,
        ["tapZoneDepth"] = c.TapZoneDepth,
        ["tapZoneEdgeMargin"] = c.TapZoneEdgeMargin,
        ["frontIsHighY"] = c.FrontIsHighY,
        ["tapRightClick"] = c.TapRightClick,
        ["rightClickMode"] = TraceNames.RightClickMode(c.RightClickMode),
        ["rightSideStart"] = c.RightSideStart,
        ["doubleTapWindow"] = c.DoubleTapWindow,
        ["doubleTapMaxDistance"] = c.DoubleTapMaxDistance,
        ["tapAndDrag"] = c.TapAndDrag,
        ["dragWindow"] = c.DragWindow,
        ["dragMaxDistance"] = c.DragMaxDistance,
        ["dragSwipeCancelDistance"] = c.DragSwipeCancelDistance,
        ["twoFingerDrag"] = c.TwoFingerDrag,
        ["twoFingerPairWindow"] = c.TwoFingerPairWindow,
        ["twoFingerLongPress"] = c.TwoFingerLongPress,
        ["scrollBurstWindow"] = c.ScrollBurstWindow,
        ["scrollBurstCancelThreshold"] = c.ScrollBurstCancelThreshold,
        ["pressQuietPeriod"] = c.PressQuietPeriod,
        ["pairArmTTL"] = c.PairArmTTL,
        ["pairRearmCooldown"] = c.PairRearmCooldown,
    };
}
