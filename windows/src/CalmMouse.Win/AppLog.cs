// CalmMouse's own log: %LocalAppData%\CalmMouse\CalmMouse.log. What the engine reports (hook
// installed, mice found, reinstalls, the resend breaker, faults) and what the app did (started,
// paused, settings problems, quit), one line each with the time. Plain English on purpose: it's
// read by whoever fixes a bug. Over 1 MB at start, the old log becomes CalmMouse.old.log.

using System.IO;
using System.Text;

namespace CalmMouse.Win;

internal sealed class AppLog : IDisposable
{
    private const long MaxBytes = 1024 * 1024;

    private readonly Lock _lock = new();
    private StreamWriter? _writer;

    public string Path { get; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmMouse", "CalmMouse.log");

    public AppLog(string path)
    {
        Path = path;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(path, System.IO.Path.ChangeExtension(path, ".old.log"), overwrite: true);
            }
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;   // no log is no reason not to run
        }
    }

    /// <summary>A line stamped with today's date and the time.</summary>
    public void Write(string line) => WriteRaw($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");

    /// <summary>An engine line, which starts with its own "HH:mm:ss.fff " time.</summary>
    public void WriteEngine(string line) =>
        WriteRaw(line.Length > 13 ? $"{DateTime.Now:yyyy-MM-dd} {line[..12]} engine: {line[13..]}" : line);

    private void WriteRaw(string text)
    {
        lock (_lock)
        {
            try { _writer?.WriteLine(text); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* keep running */ }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
