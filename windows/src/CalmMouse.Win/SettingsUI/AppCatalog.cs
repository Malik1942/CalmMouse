// Apps to offer in "Add an app…": the ones open now with a window you can see (as Alt+Tab would
// list them), each with its name and icon, plus a program picked from a file. Rules are keyed by
// exe name (spec 1.3), so that's what each one comes down to.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CalmMouse.Win.SettingsUI;

/// <summary>An app that a rule can be made for.</summary>
/// <param name="ExeName">The process file name, lower case (for example <c>figma.exe</c>): what the rule matches.</param>
/// <param name="DisplayName">Its name as people know it (the exe's description), else the file name.</param>
/// <param name="Path">Where the exe is, if known (for its icon).</param>
public sealed record AppCandidate(string ExeName, string DisplayName, string? Path);

/// <summary>Where the settings window finds apps and their icons. The real one asks Windows; tests use a fake.</summary>
public interface IAppCatalog
{
    /// <summary>Apps with a visible window now, one per exe, CalmMouse itself left out, by name.</summary>
    IReadOnlyList<AppCandidate> RunningApps();

    /// <summary>The app in an .exe file the person picked.</summary>
    AppCandidate Describe(string exePath);

    /// <summary>The icon for an exe (from its path if known, else a running copy of it), or null.</summary>
    ImageSource? IconFor(string exeName, string? path);
}

public sealed unsafe class WindowsAppCatalog : IAppCatalog
{
    /// <summary>Icons are drawn at 24 px; 48 stays sharp up to 200 % scaling.</summary>
    private const int IconPixels = 48;

    private static readonly Dictionary<string, string> s_knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> s_icons = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AppCandidate> RunningApps()
    {
        var windows = new List<nint>();
        GCHandle handle = GCHandle.Alloc(windows);
        try { EnumWindows(&CollectWindow, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }

        uint self = (uint)Environment.ProcessId;
        var apps = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (nint hwnd in windows)
        {
            if (!IsAppWindow(hwnd)) { continue; }
            uint pid = PidOf(hwnd);
            string? path = PathOf(pid);
            if (path != null && System.IO.Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                // A Store app: its own window is a child from another process.
                pid = HostedPid(hwnd, pid);
                path = pid == 0 ? null : PathOf(pid);
            }
            if (pid == 0 || pid == self || path == null) { continue; }
            AppCandidate app = Describe(path);
            apps.TryAdd(app.ExeName, app);
        }
        return apps.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public AppCandidate Describe(string exePath)
    {
        string exe = System.IO.Path.GetFileName(exePath).ToLowerInvariant();
        lock (s_knownPaths) { s_knownPaths[exe] = exePath; }
        string name = "";
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
            name = FirstName(info.FileDescription, info.ProductName) ?? "";
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException) { }
        if (name.Length == 0) { name = System.IO.Path.GetFileNameWithoutExtension(exePath); }
        return new AppCandidate(exe, name, exePath);
    }

    public ImageSource? IconFor(string exeName, string? path)
    {
        lock (s_icons)
        {
            if (s_icons.TryGetValue(exeName, out ImageSource? cached)) { return cached; }
        }
        path ??= KnownPath(exeName);
        ImageSource? icon = path != null ? LoadIcon(path) : null;
        // Only a found icon is kept: an app that isn't running now may be later.
        if (icon != null) { lock (s_icons) { s_icons[exeName] = icon; } }
        return icon;
    }

    private static string? KnownPath(string exeName)
    {
        lock (s_knownPaths)
        {
            if (s_knownPaths.TryGetValue(exeName, out string? known)) { return known; }
        }
        Process[] processes;
        try { processes = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(exeName)); }
        catch (InvalidOperationException) { return null; }
        string? found = null;
        foreach (Process process in processes)
        {
            try { found ??= PathOf((uint)process.Id); }
            catch (InvalidOperationException) { /* exited meanwhile */ }
            finally { process.Dispose(); }
        }
        if (found != null) { lock (s_knownPaths) { s_knownPaths[exeName] = found; } }
        return found;
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractIcon(path, 0, IconPixels) ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) { return null; }
            BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FirstName(params string?[] names) =>
        names.Select(n => n?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));

    // MARK: Windows

    /// <summary>A window Alt+Tab would show: visible, titled, not owned, not a tool window, not cloaked, not the desktop.</summary>
    private static bool IsAppWindow(nint hwnd)
    {
        if (IsWindowVisible(hwnd) == 0 || GetWindowTextLengthW(hwnd) == 0) { return false; }
        if (GetWindow(hwnd, GW_OWNER) != 0) { return false; }
        long exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) { return false; }
        int cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0) { return false; }
        char* className = stackalloc char[64];
        int length = GetClassNameW(hwnd, className, 64);
        string cls = new(className, 0, Math.Max(0, length));
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint lParam)
    {
        ((List<nint>)GCHandle.FromIntPtr(lParam).Target!).Add(hwnd);
        return 1;
    }

    private struct HostSearch
    {
        public uint HostPid;
        public uint Found;
    }

    private static uint HostedPid(nint frame, uint hostPid)
    {
        var search = new HostSearch { HostPid = hostPid };
        EnumChildWindows(frame, &FindForeignChild, (nint)(&search));
        return search.Found;
    }

    [UnmanagedCallersOnly]
    private static int FindForeignChild(nint hwnd, nint lParam)
    {
        var search = (HostSearch*)lParam;
        uint pid = PidOf(hwnd);
        if (pid != 0 && pid != search->HostPid)
        {
            search->Found = pid;
            return 0;
        }
        return 1;
    }

    private static uint PidOf(nint hwnd)
    {
        uint pid = 0;
        GetWindowThreadProcessId(hwnd, &pid);
        return pid;
    }

    /// <summary>The process's exe path, or null if it can't be read (a protected process).</summary>
    private static string? PathOf(uint pid)
    {
        if (pid == 0) { return null; }
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if (process == 0) { return null; }
        try
        {
            char* buffer = stackalloc char[1024];
            uint length = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, &length) != 0 ? new string(buffer, 0, (int)length) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const uint DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern int EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern int EnumChildWindows(nint hWndParent, delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern int IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, uint* lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, void* pvAttribute, int cbAttribute);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, uint* lpdwSize);
}
