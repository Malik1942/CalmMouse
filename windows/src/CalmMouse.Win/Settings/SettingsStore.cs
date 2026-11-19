// The settings file: %AppData%\CalmMouse\settings.json (see AppSettings.cs for its shape).
//
// - Read once at start. No file: defaults, and this is the first launch. A file that isn't JSON:
//   it is kept next to it as settings.json.bak and CalmMouse starts from defaults.
// - Every change is written a moment later (several changes in a row make one write), atomically:
//   a temp file next to it, flushed to disk, then swapped in with File.Replace, so a crash or
//   power cut leaves either the old file or the new one, never half of one. Flush() writes now.
// - It is the engine's IWheelSettingsProvider: the input thread reads each mouse's settings and
//   the app rules from here, and Changed tells it to read them again.

using System.IO;
using CalmMouse.Core;
using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;

namespace CalmMouse.Win.Settings;

public sealed class SettingsStore : IWheelSettingsProvider, IDisposable
{
    public const string FileName = "settings.json";
    public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private readonly Lock _writeLock = new();
    private readonly TimeSpan _saveDelay;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private readonly Timer _saveTimer;
    private AppSettings _settings;
    private bool _dirty;
    private bool _disposed;

    /// <summary>%AppData%\CalmMouse\settings.json.</summary>
    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CalmMouse", FileName);

    /// <summary>Reads the file at <paramref name="path"/> (see the top of this file for what happens when it's missing or corrupt).</summary>
    public SettingsStore(string path, TimeSpan? saveDelay = null, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        Path = path;
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _log = log;
        _saveTimer = new Timer(_ => SaveIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
        _settings = Load();
    }

    public string Path { get; }

    /// <summary>There was no settings file: CalmMouse has never run for this user.</summary>
    public bool IsFirstLaunch { get; private set; }

    /// <summary>Where a corrupt settings file was kept, if there was one.</summary>
    public string? CorruptBackupPath { get; private set; }

    /// <summary>The settings now. Immutable: change them through the methods below.</summary>
    public AppSettings Current
    {
        get { lock (_lock) { return _settings; } }
    }

    /// <summary>A mouse's settings or the app rules changed (the engine reads them again). Raised on the changing thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Anything changed. Raised on the changing thread, after <see cref="Changed"/>.</summary>
    public event EventHandler? SettingsChanged;

    // MARK: Engine (any thread)

    public WheelSettings SettingsFor(string stableKey) => SettingsFor(stableKey, "");

    /// <summary>The mouse's own settings, else a same-named mouse's, else everything off.</summary>
    public WheelSettings SettingsFor(string stableKey, string displayName) =>
        Current.MouseFor(stableKey, displayName)?.Wheel ?? new WheelSettings();

    public IReadOnlyList<WheelAppRule> AppRules => Current.AppRules;

    // MARK: Changes

    public void SetPaused(bool paused) => Update(s => s with { Paused = paused }, engine: false);

    public void SetLaunchAtLogin(bool enabled) => Update(s => s with { LaunchAtLogin = enabled }, engine: false);

    public void SetLanguage(AppLanguage language) => Update(s => s with { Language = language }, engine: false);

    /// <summary>
    /// Changes one mouse's settings. Its first change starts from what it was using (a same-named
    /// mouse's, or everything off), so what <paramref name="change"/> doesn't touch stays as it
    /// was. The speed is kept within the Mac's range.
    /// </summary>
    public void UpdateMouse(string stableKey, string displayName, Func<WheelSettings, WheelSettings> change) => Update(s =>
    {
        WheelSettings wheel = change(s.MouseFor(stableKey, displayName)?.Wheel ?? new WheelSettings());
        wheel.Speed = double.IsFinite(wheel.Speed)
            ? Math.Clamp(wheel.Speed, ScrollRouter.MinimumSpeed, ScrollRouter.MaximumSpeed)
            : ScrollRouter.DefaultSpeed;
        var mice = new Dictionary<string, MouseSettings>(s.Mice, StringComparer.Ordinal)
        {
            [stableKey] = new MouseSettings { DisplayName = displayName, Wheel = wheel, UpdatedAt = _clock() },
        };
        return s with { Mice = mice };
    }, engine: true);

    public void SetAppRules(IEnumerable<WheelAppRule> rules)
    {
        WheelAppRule[] list = rules.ToArray();
        Update(s => s with { AppRules = list }, engine: true);
    }

    private void Update(Func<AppSettings, AppSettings> change, bool engine)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AppSettings next = change(_settings);
            if (next == _settings) { return; }
            _settings = next;
            _dirty = true;
            _saveTimer.Change(_saveDelay, Timeout.InfiniteTimeSpan);
        }
        if (engine) { Changed?.Invoke(this, EventArgs.Empty); }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // MARK: Reading and writing

    private AppSettings Load()
    {
        string text;
        try
        {
            if (!File.Exists(Path))
            {
                IsFirstLaunch = true;
                return new AppSettings();
            }
            text = File.ReadAllText(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Can't be read right now (locked by a backup tool?): use defaults, and don't write
            // over the file unless something is changed.
            _log?.Invoke($"settings: couldn't read {Path}: {ex.Message}");
            return new AppSettings();
        }

        if (SettingsJson.Read(text) is { } settings) { return settings; }

        string backup = Path + ".bak";
        try
        {
            File.Copy(Path, backup, overwrite: true);
            CorruptBackupPath = backup;
            _log?.Invoke($"settings: {Path} isn't valid JSON; kept it as {backup} and started from defaults");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"settings: {Path} isn't valid JSON and couldn't be kept as {backup}: {ex.Message}");
        }
        _dirty = true;   // replace the corrupt file with defaults at the first save
        return new AppSettings();
    }

    /// <summary>Writes the settings now if anything is waiting to be written (and on first launch, so the file exists).</summary>
    public void Flush() => SaveIfDirty();

    /// <summary>Marks the settings as needing a write, then writes them now.</summary>
    public void SaveNow()
    {
        lock (_lock) { _dirty = true; }
        SaveIfDirty();
    }

    private void SaveIfDirty()
    {
        lock (_writeLock)
        {
            string json;
            lock (_lock)
            {
                if (!_dirty) { return; }
                json = SettingsJson.Write(_settings);
                _dirty = false;
            }
            try
            {
                WriteAtomically(Path, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"settings: couldn't write {Path}: {ex.Message}");
                lock (_lock)
                {
                    _dirty = true;   // try again a little later
                    if (!_disposed) { _saveTimer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan); }
                }
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> so that a crash at any moment
    /// leaves either the old file or the new one: a temp file in the same folder, flushed to disk,
    /// then swapped in (File.Replace, or a move when there's nothing to replace).
    /// </summary>
    public static void WriteAtomically(string path, string contents)
    {
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        string temp = System.IO.Path.Combine(folder, $".{System.IO.Path.GetFileName(path)}.{Environment.ProcessId}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    return;
                }
                catch (IOException)
                {
                    // Some file systems (network shares, some sync folders) can't replace; a move still swaps it in one step.
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) { File.Delete(temp); } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* left for the next write to overwrite */ }
        }
    }

    /// <summary>Writes anything pending and stops the save timer.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) { return; }
            _disposed = true;
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        SaveIfDirty();
        _saveTimer.Dispose();
    }
}
