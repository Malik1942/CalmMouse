// `CalmMouse.Win.exe --engine-test [--seconds N]`: a temporary developer switch for the hands-on
// check on a real PC (plan task 16). It runs the input thread with every feature off (the
// in-memory settings: no smoothing, no reverse, no blocking), so every wheel event passes
// untouched, and logs what the engine sees to %TEMP%\CalmMouse-engine-test-<time>.log: the
// mice found, each wheel event with the window it scrolls and the router's answer, each Raw
// Input wheel with its device, and the counters every 5 seconds. A small window shows the log's
// path; closing it (or --seconds running out) stops the engine and exits.
// English only on purpose: this is not for users and goes away with the tray app.

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;

namespace CalmMouse.Win;

internal sealed class EngineTest
{
    public const string Switch = "--engine-test";
    private const double StatsInterval = 5.0;

    private readonly InputThread _engine = new(new InputThreadOptions { RecordWheelLog = true });
    private readonly StreamWriter _log;
    private readonly Lock _logLock = new();
    private readonly double _clockStart = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private readonly DateTime _wallStart = DateTime.Now;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _writer;

    public string LogPath { get; }

    private EngineTest(string logPath)
    {
        LogPath = logPath;
        _log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Run(Application app, string[] args)
    {
        int? seconds = null;
        int at = Array.FindIndex(args, a => a.Equals("--seconds", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], CultureInfo.InvariantCulture, out int s) && s > 0) { seconds = s; }

        string path = Path.Combine(Path.GetTempPath(), $"CalmMouse-engine-test-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var test = new EngineTest(path);
        if (!test.Start())
        {
            MessageBox.Show($"The engine couldn't start. Details are in\n{path}", "CalmMouse engine test");
            app.Shutdown(1);
            return;
        }

        var window = new Window
        {
            Title = "CalmMouse engine test",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.CanMinimize,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = "The scroll engine is running with every feature off: wheel events should pass untouched. " +
                               "Close this window to stop it." + (seconds is { } n ? $" (It stops by itself after {n} s.)" : ""),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8),
                    },
                    new TextBox { Text = path, IsReadOnly = true },
                },
            },
        };
        window.Closed += (_, _) => test.Stop();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.MainWindow = window;
        window.Show();

        if (seconds is { } limit)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(limit) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.Close();
            };
            timer.Start();
        }
    }

    private bool Start()
    {
        Write($"CalmMouse engine test, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, pid {Environment.ProcessId}, {RuntimeInfo()}");
        Write("Every feature is off (smoothing, reverse, blocking): the router should pass every wheel event.");
        _engine.Faulted += (_, ex) => Write($"FAULTED: {ex}");
        try
        {
            _engine.Start();
        }
        catch (Exception ex)
        {
            Write($"start failed: {ex}");
            _log.Dispose();
            return false;
        }
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "CalmMouse engine test log" };
        _writer.Start();
        return true;
    }

    private void Stop()
    {
        if (_stop.IsCancellationRequested) { return; }
        Write("stopping");
        _engine.Stop();
        _stop.Cancel();
        _writer?.Join(TimeSpan.FromSeconds(2));
        Flush();
        WriteStats();
        EngineStatus status = _engine.Status;
        Write($"engine stopped: running={Yes(status.Running)}, hook installed={Yes(status.HookInstalled)}. Exiting.");
        _engine.Dispose();
        lock (_logLock) { _log.Dispose(); }
    }

    private void WriterLoop()
    {
        double nextStats = Now() + StatsInterval;
        while (!_stop.Token.WaitHandle.WaitOne(200))
        {
            Flush();
            if (Now() >= nextStats)
            {
                nextStats += StatsInterval;
                WriteStats();
            }
        }
    }

    private void Flush()
    {
        // Engine messages start with their own "HH:mm:ss.fff " time.
        foreach (string message in _engine.TakeMessages())
        {
            lock (_logLock)
            {
                try { _log.WriteLine(message.Length > 13 ? $"{message[..12]} engine: {message[13..]}" : message); }
                catch (ObjectDisposedException) { /* after Stop */ }
            }
        }
        if (_engine.WheelLog is not { } wheels) { return; }
        Dictionary<long, string> names = _engine.Devices.ToDictionary(d => d.DeviceId, d => $"{d.DisplayName}, {d.Kind}");
        while (wheels.TryRead(out WheelLogEntry e)) { Write(Format(e, names), Wall(e.Time)); }
    }

    private static string Format(in WheelLogEntry e, Dictionary<long, string> names)
    {
        string axis = e.Horizontal ? "h" : "v";
        string delta = e.Delta.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        string extra = e.ExtraInfo == 0 ? "" : $" extra=0x{e.ExtraInfo:X}{(e.ExtraInfo == ScrollRouter.ResendMarker ? " (our resend)" : "")}";
        return e.Kind switch
        {
            WheelLogKind.HookWheel =>
                $"hook  wheel {axis} {delta,5} -> {(e.Swallowed ? "SWALLOW" : "pass")}  target={e.Target.ExeName ?? "?"}{(e.Target.Elevated ? " (elevated: left alone)" : "")}",
            WheelLogKind.InjectedWheel => $"hook  injected {axis} {delta,5} -> pass{extra}",
            WheelLogKind.RawWheel =>
                $"raw   wheel {axis} {delta,5}  device=0x{e.DeviceId:X}{(names.TryGetValue(e.DeviceId, out string? n) ? $" ({n})" : e.DeviceId == 0 ? " (none: injected)" : "")}{extra}",
            WheelLogKind.Sent => $"send  wheel {axis} {delta,5}{(e.Swallowed ? "  FAILED" : "")}",
            _ => e.ToString(),
        };
    }

    private void WriteStats()
    {
        EngineStatus s = _engine.Status;
        RouterStats r = s.Router;
        EngineCounters c = s.Counters;
        Write($"stats: hook installed={Yes(s.HookInstalled)} paused={Yes(s.Paused)} glide={s.GlideHz:0.#} Hz routing={s.Routing} | " +
              $"hook calls={c.HookCalls} physical wheels={c.PhysicalWheels} injected wheels={c.InjectedWheels} raw wheels={c.RawWheels} | " +
              $"router passed={r.HooksPassed} swallowed={r.HooksSwallowed} unclaimed resends={r.UnclaimedResends} unmatched raw={r.UnmatchedRaws} " +
              $"ignored raw={r.IgnoredRaws} inexact pairs={r.InexactPairs} stuck releases={r.StuckWheelReleases} faults={r.Faults} | " +
              $"sends={c.Sends} send failures={c.SendFailures} resend echoes={c.ResendEchoes} breaker trips={c.BreakerTrips} breaker open={Yes(s.BreakerOpen)} " +
              $"hook reinstalls={c.HookReinstalls} " +
              $"log dropped={_engine.WheelLog?.Dropped ?? 0}{(s.LastRouterFault is { } f ? $" last router fault: {f}" : "")}");
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string RuntimeInfo() =>
        $"Windows {Environment.OSVersion.Version}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}";

    private double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private DateTime Wall(double engineSeconds) => _wallStart.AddSeconds(engineSeconds - _clockStart);

    private void Write(string line, DateTime? at = null)
    {
        lock (_logLock)
        {
            try { _log.WriteLine($"{at ?? DateTime.Now:HH:mm:ss.fff} {line}"); }
            catch (ObjectDisposedException) { /* after Stop */ }
        }
    }
}
