// The golden-trace format (shared/fixtures/core/README.md): reading scenarios and traces, running
// their steps through the C# ports, and comparing what comes out with what the Swift core wrote.
// It mirrors Tests/CalmMouseCoreTests/GoldenTraceTests.swift, which writes the traces: field names,
// defaults and outputs must stay the same on both sides.
//
// A scenario (hand-written) may leave fields out; they take the same defaults as in Swift, which
// are the ported types' own defaults. A trace (written by Swift) has every field, and is read
// strictly: a field missing from it is an error, so nothing is silently defaulted on replay.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CalmMouse.Core.Tests;

internal sealed class TraceException(string message) : Exception(message);

/// <summary>What running a scenario gives: the full config used, every step with all its fields, one output per step.</summary>
internal sealed record TraceRun(JsonObject Config, JsonArray Steps, JsonArray Outputs);

internal static class GoldenTraceFormat
{
    public static readonly IReadOnlyList<string> Components =
        ["ScrollBlocker", "WheelSmoother", "ControllerPointer", "TapRecognizer"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>shared/fixtures/core, found by walking up from the test binary's folder.</summary>
    public static string CoreFixturesFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "shared", "fixtures", "core");
            if (Directory.Exists(Path.Combine(candidate, "scenarios")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException(
            $"Couldn't find shared/fixtures/core/scenarios above {AppContext.BaseDirectory}.");
    }

    public static JsonNode? ReadFile(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            throw new TraceException($"{Path.GetFileName(path)} is not valid JSON: {e.Message}");
        }
    }

    public static TraceRun Run(string component, JsonNode? config, JsonArray steps, bool strict) => component switch
    {
        "ScrollBlocker" => ScrollBlockerTrace.Run(config, steps, strict),
        "WheelSmoother" => WheelSmootherTrace.Run(config, steps, strict),
        "ControllerPointer" => ControllerPointerTrace.Run(config, steps, strict),
        "TapRecognizer" => TapRecognizerTrace.Run(config, steps, strict),
        _ => throw new TraceException($"unknown component \"{component}\""),
    };

    /// <summary>
    /// A trace built by the C# ports from a hand-written scenario, in the same shape as the Swift
    /// ones. Only the self-check uses it: the real traces always come from Swift.
    /// </summary>
    public static JsonObject TraceFromScenario(string scenarioPath)
    {
        string name = Path.GetFileNameWithoutExtension(scenarioPath);
        string context = $"scenarios/{name}.json";
        try
        {
            var f = new Fields(ReadFile(scenarioPath), context, strict: false,
                "component", "name", "description", "config", "steps");
            string component = f.GetString("component", null);
            TraceRun run = Run(component, f.Value("config"), f.GetArray("steps"), strict: false);
            return new JsonObject
            {
                ["name"] = name,
                ["component"] = component,
                ["description"] = f.GetString("description", ""),
                ["source"] = $"shared/fixtures/core/scenarios/{name}.json, run by the C# self-check",
                ["config"] = run.Config,
                ["steps"] = run.Steps,
                ["outputs"] = run.Outputs,
            };
        }
        catch (TraceException e)
        {
            throw new TraceException($"{context}: {e.Message}");
        }
    }

    public static void Write(JsonObject trace, string path) =>
        File.WriteAllText(path, trace.ToJsonString(Indented) + "\n");

    /// <summary>
    /// Replays one trace file through the C# ports. Returns the first difference, or null when every
    /// output matches. Pixel differences accepted as last-bit floating-point differences are added
    /// to <paramref name="notes"/>.
    /// </summary>
    public static string? Replay(string path, List<string> notes)
    {
        string file = Path.GetFileName(path);
        try
        {
            var f = new Fields(ReadFile(path), file, strict: true,
                "name", "component", "description", "source", "config", "steps", "outputs");
            string component = f.GetString("component", null);
            JsonArray steps = f.GetArray("steps");
            JsonArray expected = f.GetArray("outputs");
            if (steps.Count != expected.Count)
            {
                return $"{file}: {steps.Count} steps but {expected.Count} outputs";
            }
            TraceRun run = Run(component, f.Value("config"), steps, strict: true);

            // What the C# side read must be what Swift wrote: same config, same steps.
            string? d = TraceCompare.Difference(f.Value("config"), run.Config, "config");
            for (int i = 0; d is null && i < steps.Count; i++)
            {
                d = TraceCompare.Difference(steps[i], run.Steps[i], $"steps[{i}]");
            }
            if (d is not null)
            {
                return $"{file}: read back differently: {d}";
            }

            d = component == "WheelSmoother"
                ? TraceCompare.SmootherOutputs(expected, run.Outputs, steps, file, notes)
                : TraceCompare.Outputs(expected, run.Outputs, steps);
            return d is null ? null : $"{file}: {d}";
        }
        catch (TraceException e)
        {
            return $"{file}: {e.Message}";
        }
    }
}

/// <summary>Comparing outputs: numbers within 1e-9 (absolute or relative), everything else exactly.</summary>
internal static class TraceCompare
{
    public static string Inline(JsonNode? node) => node?.ToJsonString() ?? "null";

    public static string Show(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    public static double Number(JsonValue value) =>
        double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    public static bool CloseEnough(double a, double b)
    {
        if (a == b)
        {
            return true;
        }
        double diff = Math.Abs(a - b);
        return diff <= 1e-9 || diff <= 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));
    }

    public static string? Outputs(JsonArray expected, JsonArray actual, JsonArray steps)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            string? d = Difference(expected[i], actual[i], $"outputs[{i}]");
            if (d is not null)
            {
                return $"step {i} {Inline(steps[i])}: {d}";
            }
        }
        return null;
    }

    /// <summary>"Swift" is the trace, "C#" what the port did.</summary>
    public static string? Difference(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null ? null : $"{path}: Swift {Inline(expected)}, C# {Inline(actual)}";
        }
        switch (expected)
        {
            case JsonObject eo when actual is JsonObject ao:
                foreach (string key in eo.Select(p => p.Key).Union(ao.Select(p => p.Key)).Order(StringComparer.Ordinal))
                {
                    if (!eo.TryGetPropertyValue(key, out JsonNode? e))
                    {
                        return $"{path}.{key}: produced by C#, but not in the trace";
                    }
                    if (!ao.TryGetPropertyValue(key, out JsonNode? a))
                    {
                        return $"{path}.{key}: in the trace, but not produced by C#";
                    }
                    string? d = Difference(e, a, $"{path}.{key}");
                    if (d is not null)
                    {
                        return d;
                    }
                }
                return null;

            case JsonArray ea when actual is JsonArray aa:
                if (ea.Count != aa.Count)
                {
                    return $"{path}: Swift has {ea.Count} items, C# {aa.Count}: Swift {Inline(ea)}, C# {Inline(aa)}";
                }
                for (int i = 0; i < ea.Count; i++)
                {
                    string? d = Difference(ea[i], aa[i], $"{path}[{i}]");
                    if (d is not null)
                    {
                        return d;
                    }
                }
                return null;

            case JsonValue ev when actual is JsonValue av:
                JsonValueKind ek = ev.GetValueKind();
                JsonValueKind ak = av.GetValueKind();
                if (ek == JsonValueKind.Number && ak == JsonValueKind.Number)
                {
                    double x = Number(ev);
                    double y = Number(av);
                    return CloseEnough(x, y) ? null : $"{path}: Swift {Show(x)}, C# {Show(y)}";
                }
                bool same = ek == ak && (ek != JsonValueKind.String || ev.GetValue<string>() == av.GetValue<string>());
                return same ? null : $"{path}: Swift {Inline(ev)}, C# {Inline(av)}";

            default:
                return $"{path}: Swift {Inline(expected)}, C# {Inline(actual)}";
        }
    }

    /// <summary>
    /// The wheel smoother's outputs. Its whole pixels come out of exp(), and the C runtime's exp()
    /// on Windows or Linux may differ from macOS's in the last bit. Right at a rounding edge that
    /// can move one pixel between this frame and the next: it is then still owed instead of paid
    /// (or the other way round). Such a difference of 1 is accepted, and noted, only when the
    /// pixels paid so far plus what is still owed come to the same distance on both sides, and
    /// the pixels paid on that axis are equal again by the axis's next frame (and by the end of the
    /// trace). A drift that lasts, even one that keeps the distance, is a real difference.
    /// Everything else must match as usual.
    /// </summary>
    public static string? SmootherOutputs(JsonArray expected, JsonArray actual, JsonArray steps, string file, List<string> notes)
    {
        var x = new PixelAxis("x");
        var y = new PixelAxis("y");
        for (int i = 0; i < expected.Count; i++)
        {
            string where = $"step {i} {Inline(steps[i])}";
            var e = new Fields(expected[i], $"outputs[{i}]", strict: true, "step", "isIdle", "owedX", "owedY");
            var a = new Fields(actual[i], $"outputs[{i}] (C#)", strict: true, "step", "isIdle", "owedX", "owedY");
            if (e.GetBool("isIdle", null) != a.GetBool("isIdle", null))
            {
                return $"{where}: outputs[{i}].isIdle: Swift {Inline(e.Value("isIdle"))}, C# {Inline(a.Value("isIdle"))}";
            }
            JsonNode? es = e.Value("step");
            JsonNode? @as = a.Value("step");
            if ((es is null) != (@as is null))
            {
                return $"{where}: outputs[{i}].step: Swift {Inline(es)}, C# {Inline(@as)}";
            }
            double owedX = e.GetDouble("owedX", null), owedY = e.GetDouble("owedY", null);
            double owedByCSharpX = a.GetDouble("owedX", null), owedByCSharpY = a.GetDouble("owedY", null);
            if (es is not null && @as is not null)
            {
                var ef = new Fields(es, $"outputs[{i}].step", strict: true, "x", "y");
                var af = new Fields(@as, $"outputs[{i}].step (C#)", strict: true, "x", "y");
                // Both axes every frame, so neither axis's running totals skip one.
                string? dx = x.Frame(ef.GetLong("x", null), af.GetLong("x", null), owedX, owedByCSharpX, where, out bool acceptedX);
                string? dy = y.Frame(ef.GetLong("y", null), af.GetLong("y", null), owedY, owedByCSharpY, where, out bool acceptedY);
                if ((dx ?? dy) is string d)
                {
                    return $"{where}: outputs[{i}].{d}";
                }
                if (acceptedX || acceptedY)
                {
                    notes.Add($"{file}, {where}: a pixel came out one frame apart in C# and Swift "
                        + $"(Swift {Inline(es)}, C# {Inline(@as)}); accepted as a last-bit difference in exp(): "
                        + "the distance adds up and the next frame evens it out.");
                }
            }
            if (!x.Owed(owedX, owedByCSharpX))
            {
                return $"{where}: outputs[{i}].owedX: Swift {Show(owedX)}, C# {Show(owedByCSharpX)}";
            }
            if (!y.Owed(owedY, owedByCSharpY))
            {
                return $"{where}: outputs[{i}].owedY: Swift {Show(owedY)}, C# {Show(owedByCSharpY)}";
            }
        }
        return x.Unsettled() ?? y.Unsettled();
    }

    /// <summary>One axis of the wheel smoother: pixels paid so far on each side, and an accepted off-by-one waiting to even out.</summary>
    private sealed class PixelAxis(string name)
    {
        private long _paid;
        private long _paidByCSharp;
        private string? _acceptedAt;

        /// <summary>
        /// One frame on this axis. Null when it matches (or is accepted, then <paramref name="accepted"/>
        /// is true); otherwise the problem.
        /// </summary>
        public string? Frame(long swift, long csharp, double owed, double owedByCSharp, string where, out bool accepted)
        {
            accepted = false;
            _paid += swift;
            _paidByCSharp += csharp;
            if (_acceptedAt is not null)
            {
                // The frame after an accepted difference must even the paid pixels out again.
                if (_paid != _paidByCSharp)
                {
                    return $"step.{name}: Swift {swift}, C# {csharp}. The pixel accepted at {_acceptedAt} "
                        + $"was not made up by this frame (paid so far: Swift {_paid}, C# {_paidByCSharp}).";
                }
                _acceptedAt = null;
                accepted = swift != csharp;
                return null;
            }
            if (swift == csharp)
            {
                return null;
            }
            // Before this frame the totals were equal, so they now differ by exactly swift - csharp.
            bool explained = Math.Abs(swift - csharp) == 1
                && Math.Abs(_paid + owed - (_paidByCSharp + owedByCSharp)) <= 1e-6;
            if (!explained)
            {
                return $"step.{name}: Swift {swift}, C# {csharp}";
            }
            _acceptedAt = where;
            accepted = true;
            return null;
        }

        /// <summary>What is still owed matches, or, while an accepted pixel is pending, adds up with what was paid.</summary>
        public bool Owed(double owed, double owedByCSharp) =>
            _paid == _paidByCSharp
                ? CloseEnough(owed, owedByCSharp)
                : Math.Abs(_paid + owed - (_paidByCSharp + owedByCSharp)) <= 1e-6;

        /// <summary>At the end of the trace: null when no accepted pixel is still waiting to even out.</summary>
        public string? Unsettled() => _acceptedAt is null
            ? null
            : $"the {name} pixel accepted at {_acceptedAt} was never made up (paid in all: Swift {_paid}, C# {_paidByCSharp})";
    }
}

/// <summary>
/// Typed reads from a JSON object. Unknown fields are an error. Strict (a trace): every known
/// field must be present. Not strict (a hand-written scenario): a missing field takes its fallback
/// when there is one.
/// </summary>
internal sealed class Fields
{
    private readonly JsonObject _values;
    private readonly bool _strict;

    public string Context { get; }

    public Fields(JsonNode? node, string context, bool strict, params string[] allowed)
    {
        if (node is not JsonObject values)
        {
            throw new TraceException($"{context}: expected a JSON object");
        }
        foreach (KeyValuePair<string, JsonNode?> pair in values)
        {
            if (Array.IndexOf(allowed, pair.Key) < 0)
            {
                throw new TraceException($"{context}: unknown field \"{pair.Key}\" (known: {string.Join(", ", allowed)})");
            }
        }
        if (strict)
        {
            foreach (string key in allowed)
            {
                if (!values.ContainsKey(key))
                {
                    throw new TraceException($"{context}: missing \"{key}\"");
                }
            }
        }
        _values = values;
        _strict = strict;
        Context = context;
    }

    public static string Op(JsonNode? step, string context) =>
        step is JsonObject o && o.TryGetPropertyValue("op", out JsonNode? op)
            && op is JsonValue v && v.GetValueKind() == JsonValueKind.String
            ? v.GetValue<string>()
            : throw new TraceException($"{context}: every step needs an \"op\" string");

    /// <summary>The field's value; null when it is missing or JSON null.</summary>
    public JsonNode? Value(string key) => _values.TryGetPropertyValue(key, out JsonNode? v) ? v : null;

    private JsonValue? Present(string key, bool hasFallback)
    {
        if (!_values.TryGetPropertyValue(key, out JsonNode? node))
        {
            if (hasFallback && !_strict)
            {
                return null;
            }
            throw new TraceException($"{Context}: missing \"{key}\"");
        }
        return node as JsonValue ?? throw new TraceException($"{Context}: \"{key}\" must be a plain value, not {TraceCompare.Inline(node)}");
    }

    public double GetDouble(string key, double? fallback)
    {
        JsonValue? v = Present(key, fallback.HasValue);
        if (v is null)
        {
            return fallback!.Value;
        }
        return v.GetValueKind() == JsonValueKind.Number
            ? TraceCompare.Number(v)
            : throw new TraceException($"{Context}: \"{key}\" must be a number");
    }

    public long GetLong(string key, long? fallback)
    {
        double d = GetDouble(key, fallback);
        return Math.Round(d) == d && Math.Abs(d) <= 9_007_199_254_740_992
            ? (long)d
            : throw new TraceException($"{Context}: \"{key}\" must be a whole number");
    }

    public bool GetBool(string key, bool? fallback)
    {
        JsonValue? v = Present(key, fallback.HasValue);
        if (v is null)
        {
            return fallback!.Value;
        }
        return v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new TraceException($"{Context}: \"{key}\" must be true or false"),
        };
    }

    public string GetString(string key, string? fallback)
    {
        JsonValue? v = Present(key, fallback is not null);
        if (v is null)
        {
            return fallback!;
        }
        return v.GetValueKind() == JsonValueKind.String
            ? v.GetValue<string>()
            : throw new TraceException($"{Context}: \"{key}\" must be a string");
    }

    public bool? GetOptionalBool(string key) => Value(key) is null ? null : GetBool(key, null);

    public double? GetOptionalDouble(string key) => Value(key) is null ? null : GetDouble(key, null);

    public string? GetOptionalString(string key) => Value(key) is null ? null : GetString(key, null);

    public JsonArray GetArray(string key) => _values.TryGetPropertyValue(key, out JsonNode? node)
        ? node as JsonArray ?? throw new TraceException($"{Context}: \"{key}\" must be an array")
        : throw new TraceException($"{Context}: missing \"{key}\"");
}

/// <summary>The names the files use for enum cases (the Swift case names).</summary>
internal static class TraceNames
{
    public static string Phase(ScrollPhase p) => p switch
    {
        ScrollPhase.None => "none",
        ScrollPhase.MayBegin => "mayBegin",
        ScrollPhase.Began => "began",
        ScrollPhase.Changed => "changed",
        ScrollPhase.Ended => "ended",
        ScrollPhase.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    public static ScrollPhase Phase(string name, string context) => name switch
    {
        "none" => ScrollPhase.None,
        "mayBegin" => ScrollPhase.MayBegin,
        "began" => ScrollPhase.Began,
        "changed" => ScrollPhase.Changed,
        "ended" => ScrollPhase.Ended,
        "cancelled" => ScrollPhase.Cancelled,
        _ => throw new TraceException($"{context}: unknown phase \"{name}\""),
    };

    public static string Momentum(MomentumPhase m) => m switch
    {
        MomentumPhase.None => "none",
        MomentumPhase.Began => "began",
        MomentumPhase.Continued => "continued",
        MomentumPhase.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static MomentumPhase Momentum(string name, string context) => name switch
    {
        "none" => MomentumPhase.None,
        "began" => MomentumPhase.Began,
        "continued" => MomentumPhase.Continued,
        "ended" => MomentumPhase.Ended,
        _ => throw new TraceException($"{context}: unknown momentum phase \"{name}\""),
    };

    public static string Action(ScrollAction a) => a switch
    {
        ScrollAction.Normal => "normal",
        ScrollAction.Horizontal => "horizontal",
        ScrollAction.Invert => "invert",
        ScrollAction.ZoomCommand => "zoomCommand",
        ScrollAction.ZoomControl => "zoomControl",
        ScrollAction.Block => "block",
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    public static ScrollAction Action(string name, string context) => name switch
    {
        "normal" => ScrollAction.Normal,
        "horizontal" => ScrollAction.Horizontal,
        "invert" => ScrollAction.Invert,
        "zoomCommand" => ScrollAction.ZoomCommand,
        "zoomControl" => ScrollAction.ZoomControl,
        "block" => ScrollAction.Block,
        _ => throw new TraceException($"{context}: unknown action \"{name}\""),
    };

    public static string ControllerButton(ControllerButton b) => b switch
    {
        Core.ControllerButton.South => "south",
        Core.ControllerButton.East => "east",
        Core.ControllerButton.Touchpad => "touchpad",
        Core.ControllerButton.Menu => "menu",
        Core.ControllerButton.Options => "options",
        Core.ControllerButton.LeftStick => "leftStick",
        Core.ControllerButton.RightStick => "rightStick",
        _ => throw new ArgumentOutOfRangeException(nameof(b)),
    };

    public static ControllerButton ControllerButton(string name, string context) => name switch
    {
        "south" => Core.ControllerButton.South,
        "east" => Core.ControllerButton.East,
        "touchpad" => Core.ControllerButton.Touchpad,
        "menu" => Core.ControllerButton.Menu,
        "options" => Core.ControllerButton.Options,
        "leftStick" => Core.ControllerButton.LeftStick,
        "rightStick" => Core.ControllerButton.RightStick,
        _ => throw new TraceException($"{context}: unknown controller button \"{name}\""),
    };

    public static string PointerButton(PointerButton b) => b switch
    {
        Core.PointerButton.Left => "left",
        Core.PointerButton.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(b)),
    };

    public static string TapEvent(TapEvent e) => e switch
    {
        Core.TapEvent.Tap => "tap",
        Core.TapEvent.RightTap => "rightTap",
        Core.TapEvent.DragBegan => "dragBegan",
        Core.TapEvent.DragEnded => "dragEnded",
        Core.TapEvent.DragSwipeCancelled => "dragSwipeCancelled",
        Core.TapEvent.DragCancelled => "dragCancelled",
        Core.TapEvent.DragPressed => "dragPressed",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string Rejection(TapRecognizer.Rejection r) => r switch
    {
        TapRecognizer.Rejection.Poisoned => "poisoned",
        TapRecognizer.Rejection.ButtonDown => "buttonDown",
        TapRecognizer.Rejection.TooLong => "tooLong",
        TapRecognizer.Rejection.MovedTooMuch => "movedTooMuch",
        TapRecognizer.Rejection.TooSmall => "tooSmall",
        TapRecognizer.Rejection.ButtonCooldown => "buttonCooldown",
        TapRecognizer.Rejection.ScrollCooldown => "scrollCooldown",
        TapRecognizer.Rejection.OutsideZone => "outsideZone",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    public static string RightClickMode(RightClickMode m) => m switch
    {
        Core.RightClickMode.RightSide => "rightSide",
        Core.RightClickMode.DoubleTap => "doubleTap",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static RightClickMode RightClickMode(string name, string context) => name switch
    {
        "rightSide" => Core.RightClickMode.RightSide,
        "doubleTap" => Core.RightClickMode.DoubleTap,
        _ => throw new TraceException($"{context}: unknown rightClickMode \"{name}\""),
    };
}
