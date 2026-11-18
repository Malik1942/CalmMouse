// The C# side of the golden traces (spec section 2.3, plan tasks 8 and 9). The Swift test
// Tests/CalmMouseCoreTests/GoldenTraceTests.swift runs scenarios through the real Swift core and
// writes every input and output to shared/fixtures/core/traces; this replays each trace through
// the C# ports and checks it gets the same outputs. The format is in shared/fixtures/core/README.md
// and the reading, replaying and comparing in GoldenTraces/.

using System.Text.Json.Nodes;

namespace CalmMouse.Core.Tests;

/// <summary>Same class and test name as the Swift test, so the parity guard pairs them.</summary>
public sealed class GoldenTraceTests(ITestOutputHelper output)
{
    [Fact]
    public void testGoldenTraces()
    {
        string core = GoldenTraceFormat.CoreFixturesFolder();
        string traces = Path.Combine(core, "traces");

        // Only until the first traces are committed: they are written by Swift, which this PC
        // doesn't have, so they come from the macOS CI job. A missing folder skips (rather than
        // fails) so CI isn't red in the meantime; GoldenTraceSelfCheck below shows the replayer
        // itself works. An empty or incomplete folder still fails. Remove this skip, leaving the
        // failure, once shared/fixtures/core/traces is in the repo.
        if (!Directory.Exists(traces))
        {
            Assert.Skip("No golden traces yet: shared/fixtures/core/traces doesn't exist. Run the macOS CI job "
                + "(it uploads them as the golden-traces artifact) or, on a Mac, "
                + "CALMMOUSE_WRITE_TRACES=1 swift test --filter GoldenTraceTests, and commit them.");
        }

        string[] files = [.. Directory.GetFiles(traces, "*.json").Order(StringComparer.Ordinal)];
        Assert.True(files.Length > 0,
            $"{traces} has no traces. Run the macOS CI job (golden-traces artifact) or, on a Mac, "
            + "CALMMOUSE_WRITE_TRACES=1 swift test --filter GoldenTraceTests, and commit them.");

        var problems = new List<string>();
        var notes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (GoldenTraceFormat.Replay(file, notes) is string problem)
            {
                problems.Add(problem);
            }
            if (GoldenTraceFormat.ReadFile(file) is JsonObject trace
                && trace.TryGetPropertyValue("component", out JsonNode? component) && component is not null)
            {
                seen.Add(component.GetValue<string>());
            }
        }

        // Every hand-written scenario has its trace: a scenario added without rerunning the Swift
        // writer would otherwise never be checked here.
        foreach (string scenario in Directory.GetFiles(Path.Combine(core, "scenarios"), "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(scenario);
            if (!File.Exists(Path.Combine(traces, name)))
            {
                problems.Add($"{name}: scenarios/{name} has no trace. Rerun the Swift writer.");
            }
        }
        foreach (string component in GoldenTraceFormat.Components.Where(c => !seen.Contains(c)))
        {
            problems.Add($"No trace covers {component}.");
        }

        foreach (string note in notes)
        {
            output.WriteLine(note);
        }
        if (problems.Count > 0)
        {
            Assert.Fail($"The C# ports don't do what the Swift core did ({problems.Count} of {files.Length} traces):\n"
                + string.Join("\n", problems));
        }
    }
}

/// <summary>
/// Shows the replayer works before any Swift-written trace exists: the C# ports run the
/// hand-written scenarios, the results are written in the trace format to a temporary folder,
/// and the same replay the real test uses reads them back. That proves the reading, the
/// strictness (every field present) and the comparison, not the ports themselves, which only the
/// Swift traces can. Not a translated Swift test: no Swift class has this name, so the parity
/// guard leaves it alone.
/// </summary>
public sealed class GoldenTraceSelfCheck : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "calmmouse-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private Dictionary<string, JsonObject> WriteTracesFromScenarios()
    {
        Directory.CreateDirectory(_folder);
        var traces = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        string scenarios = Path.Combine(GoldenTraceFormat.CoreFixturesFolder(), "scenarios");
        foreach (string scenario in Directory.GetFiles(scenarios, "*.json").Order(StringComparer.Ordinal))
        {
            JsonObject trace = GoldenTraceFormat.TraceFromScenario(scenario);
            string path = Path.Combine(_folder, Path.GetFileName(scenario));
            GoldenTraceFormat.Write(trace, path);
            traces[path] = trace;
        }
        return traces;
    }

    [Fact]
    public void ReplaysTracesWrittenFromTheScenarios()
    {
        Dictionary<string, JsonObject> traces = WriteTracesFromScenarios();
        Assert.True(traces.Count > 0);
        foreach (string component in GoldenTraceFormat.Components)
        {
            Assert.Contains(traces.Values, t => t["component"]!.GetValue<string>() == component);
        }

        var notes = new List<string>();
        List<string> problems = [.. traces.Keys.Select(path => GoldenTraceFormat.Replay(path, notes)).OfType<string>()];
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Empty(notes);
    }

    [Fact]
    public void NoticesAChangedOutputInEveryComponent()
    {
        Dictionary<string, JsonObject> traces = WriteTracesFromScenarios();
        var changes = new (string Component, string Field)[]
        {
            ("ScrollBlocker", "anyButtonDown"),
            ("WheelSmoother", "isIdle"),
            ("ControllerPointer", "isActive"),
            ("TapRecognizer", "dragActive"),
        };
        foreach ((string component, string field) in changes)
        {
            (string path, JsonObject trace) = traces.First(t => t.Value["component"]!.GetValue<string>() == component);
            JsonObject output = trace["outputs"]!.AsArray()[^1]!.AsObject();
            output[field] = !output[field]!.GetValue<bool>();
            GoldenTraceFormat.Write(trace, path);

            string? problem = GoldenTraceFormat.Replay(path, []);
            Assert.True(problem is not null && problem.Contains(field, StringComparison.Ordinal),
                $"Changing {field} in {Path.GetFileName(path)} went unnoticed: {problem ?? "no problem reported"}");
        }
    }

    [Fact]
    public void AcceptsNoWheelSmootherPixelDifferenceThatDoesNotAddUp()
    {
        Dictionary<string, JsonObject> traces = WriteTracesFromScenarios();
        string path = Path.Combine(_folder, "smoother-one-notch-glides-and-settles.json");
        JsonObject trace = traces[path];
        JsonObject step = trace["outputs"]!.AsArray()
            .Select(o => o!["step"])
            .OfType<JsonObject>()
            .First();
        // Off by one pixel, with nothing owed to make up for it: a real difference, not a last-bit one.
        step["y"] = step["y"]!.GetValue<long>() + 1;
        GoldenTraceFormat.Write(trace, path);

        var notes = new List<string>();
        string? problem = GoldenTraceFormat.Replay(path, notes);
        Assert.True(problem is not null && problem.Contains("step.y", StringComparison.Ordinal),
            $"An unexplained pixel difference went unnoticed: {problem ?? "no problem reported"}");
        Assert.Empty(notes);
    }

    [Fact]
    public void RejectsATraceWithAMissingField()
    {
        Dictionary<string, JsonObject> traces = WriteTracesFromScenarios();
        (string path, JsonObject trace) = traces.First(t => t.Value["component"]!.GetValue<string>() == "ScrollBlocker");
        trace["config"]!["base"]!.AsObject().Remove("reverseMagicMouseScroll");
        GoldenTraceFormat.Write(trace, path);

        string? problem = GoldenTraceFormat.Replay(path, []);
        Assert.True(problem is not null && problem.Contains("missing \"reverseMagicMouseScroll\"", StringComparison.Ordinal),
            $"A trace without reverseMagicMouseScroll was accepted: {problem ?? "no problem reported"}");
    }
}
