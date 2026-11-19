// CalmMouse's own log: %LocalAppData%\CalmMouse\CalmMouse.log. What the engine reports (hook
// installed, mice found, reinstalls, the resend breaker, faults) and what the app did (started,
// paused, settings problems, quit), one line each with the time. Plain English on purpose: it's
// read by whoever fixes a bug. Whenever it passes 1 MB (at start or while running), it becomes
// CalmMouse.old.log and a new one starts, so the two never take more than about 2 MB.

using System.IO;
using System.Text;

namespace CalmMouse.Win;

internal sealed class AppLog : IDisposable
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly long _maxBytes;
    private StreamWriter? _writer;
    private bool _disposed;

    public string Path { get; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmMouse", "CalmMouse.log");

    public string OldPath => System.IO.Path.ChangeExtension(Path, ".old.log");

    public AppLog(string path, long maxBytes = DefaultMaxBytes)
    {
        Path = path;
        _maxBytes = maxBytes;
        lock (_lock) { Open(); }
    }

    private void Open()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var info = new FileInfo(Path);
            if (info.Exists && info.Length >= _maxBytes) { File.Move(Path, OldPath, overwrite: true); }
            var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
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
            if (_disposed) { return; }
            try
            {
                if (_writer is { } writer && writer.BaseStream.Length >= _maxBytes)
                {
                    writer.Dispose();
                    _writer = null;
                    Open();
                }
                _writer?.WriteLine(text);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException) { /* keep running */ }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
