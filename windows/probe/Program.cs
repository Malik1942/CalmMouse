using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CalmMouseProbe;

internal static class Program
{
    static volatile bool _aborted;
    static bool Aborted() => _aborted;

    static int Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; Console.InputEncoding = Encoding.UTF8; } catch { }
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _aborted = true; };

        var r = new Report();
        Console.WriteLine("CalmMouse Windows 测试工具 0.1");
        Console.WriteLine("================================");
        Console.WriteLine("它会录几段你的鼠标和手柄操作，回答 docs/specs/windows.md 里 Phase 0 的几个问题，");
        Console.WriteLine("最后在桌面生成一份报告。大约 3 分钟。");
        Console.WriteLine();
        Console.WriteLine("· 它不改任何系统设置，不联网。关掉窗口或按 Ctrl+C，一切立刻恢复原样。");
        Console.WriteLine("· 测试期间请不要开着游戏（有反作弊的游戏可能不喜欢鼠标钩子）。");
        Console.WriteLine("· 第 3 步会短暂接管滚轮 15 秒，期间滚动如果失灵是正常的，那正是要测的。");
        Console.WriteLine();
        Report.Ask("按回车开始：");

        r.Section("环境");
        Environment(r);

        if (OperatingSystem.IsWindows())
        {
            WindowsSteps(r);
        }
        else
        {
            r.Line("不是 Windows：跳过鼠标的几步，只测手柄。");
        }

        if (!_aborted)
        {
            r.Section("第 5 步 · 手柄（SDL3）");
            Console.WriteLine("  连上你的手柄（DualSense 最好：先用 USB 线测一次，有时间再用蓝牙测一次）。");
            GamepadProbe.Run(r, 25, Aborted);
        }

        if (!_aborted && OperatingSystem.IsWindows())
        {
            r.Section("几个问题");
            r.AskAndRecord("这台电脑装了 Magic Utilities 吗？如果装了，它的设置里有没有类似「点按时不滚动」的选项？（直接打字回答，没有就回车）：");
            r.AskAndRecord("还有什么想补充的？（没有就回车）：");
        }

        if (_aborted) r.Line("（中途按了 Ctrl+C，后面的步骤没做）");
        var path = r.Save();
        Console.WriteLine();
        Console.WriteLine($"报告已保存：{path}");
        // Input redirected = CI smoke run: no clipboard, no Notepad.
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
        {
            if (Platform.CopyToClipboard(r.Text)) Console.WriteLine("报告内容也已经复制到剪贴板，直接粘贴给 Claude 就行。");
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
        }
        Report.Ask("按回车退出：");
        return 0;
    }

    static void Environment(Report r)
    {
        r.Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        r.Line($"系统：{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · 本程序 {RuntimeInformation.ProcessArchitecture}");
        r.Line($"管理员权限运行：{(System.Environment.IsPrivilegedProcess ? "是" : "否")}");
        if (!OperatingSystem.IsWindows()) return;
        r.Line($"相关的驱动 / 服务：{Join(Platform.MatchingServices())}");
        r.Line($"相关的正在运行的程序：{Join(Platform.MatchingProcesses())}");
    }

    static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "无" : string.Join("、", list);
    }

    [SupportedOSPlatform("windows")]
    static void WindowsSteps(Report r)
    {
        r.Section("第 1 步 · 鼠标设备");
        var mice = Devices.Mice();
        foreach (var m in mice) r.Line($"- {m.Label}");
        if (mice.Count == 0) r.Line("（Raw Input 没列出任何鼠标）");
        var names = new DeviceNames(mice);
        bool hasMagicMouse = mice.Any(m => m.IsMagicMouse);
        if (!hasMagicMouse)
        {
            r.Line("没认出 Magic Mouse。（如果它其实连着，上面的列表能帮我找到原因。）");
        }

        if (_aborted) return;
        r.Section("第 2 步 · 只看不动：谁发的滚动");
        Console.WriteLine("  接下来 15 秒：用每一只鼠标（包括 Magic Mouse）都上下滚一滚、左右滚一滚、点几下。");
        Console.WriteLine("  在哪个窗口里滚都行，比如浏览器。");
        Report.Ask("准备好了按回车开始：");
        var observe = MouseSession.Record(HookMode.Observe, 15, Aborted);
        Analysis.Observe(r, observe, names);

        if (_aborted) return;
        r.Section("第 3 步 · 拦下来再重发（CalmMouse 的做法）");
        Console.WriteLine("  接下来 15 秒，测试工具会拦下所有滚动，再按设备重新发出去。");
        Console.WriteLine("  请像平常一样在浏览器里滚动几次，感受一下：滚动还正常吗？有没有变慢、变卡、失灵？");
        Report.Ask("准备好了按回车开始：");
        var resend = MouseSession.Record(HookMode.SwallowAndResend, 15, Aborted);
        Analysis.Resend(r, resend);
        r.AskAndRecord("刚才 15 秒里滚动感觉正常吗？（正常 / 有点卡 / 失灵，或者直接描述）：");

        if (_aborted) return;
        r.Section("第 4 步 · 点按时页面会不会跟着滚");
        if (!hasMagicMouse)
        {
            r.Line("没有 Magic Mouse，跳过。");
            return;
        }
        Console.WriteLine("  打开一个很长的网页（比如维基百科的长条目）。接下来 20 秒：");
        Console.WriteLine("  用 Magic Mouse 在页面空白处正常地点 15–20 下，像平时点东西那样，不要故意滚动。");
        Console.WriteLine("  （如果 Magic Mouse 在 Windows 上本来就不能滚动，这一步会记录到 0 次滚动，也是有用的结果。）");
        Report.Ask("准备好了按回车开始：");
        var clicks = MouseSession.Record(HookMode.Observe, 20, Aborted);
        Analysis.ClickLeak(r, clicks, names, mice.Where(m => m.IsMagicMouse).Select(m => m.Handle).ToHashSet());
        r.AskAndRecord("刚才点的时候，你自己有没有看到页面跟着动？（有 / 没有 / 不确定）：");
    }
}

internal sealed class Report
{
    readonly StringBuilder _sb = new();
    public string Text => _sb.ToString();

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
        _sb.AppendLine().AppendLine($"== {title} ==");
    }

    public void Line(string text)
    {
        Console.WriteLine("  " + text);
        _sb.AppendLine(text);
    }

    /// True once the console input has closed; prompts that loop must stop then.
    public static bool EndOfInput { get; private set; }

    public static string Ask(string prompt)
    {
        Console.Write("  " + prompt);
        var line = Console.ReadLine();
        if (line == null) EndOfInput = true;
        return line ?? "";
    }

    public void AskAndRecord(string prompt)
    {
        var answer = Ask(prompt).Trim();
        _sb.AppendLine($"问：{prompt.TrimEnd('：', ' ')}");
        _sb.AppendLine($"答：{(answer.Length == 0 ? "（空）" : answer)}");
    }

    public string Save()
    {
        var dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, $"CalmMouseProbe-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, "CalmMouse Windows 测试工具 0.1 报告" + System.Environment.NewLine + Text, new UTF8Encoding(true));
        return path;
    }
}

internal sealed class DeviceNames(IEnumerable<MouseDevice> known)
{
    readonly Dictionary<IntPtr, MouseDevice> _known = known.ToDictionary(m => m.Handle);

    [SupportedOSPlatform("windows")]
    public string Of(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "（没有设备：程序注入的或系统合成的）";
        if (!_known.TryGetValue(handle, out var m))
        {
            m = Devices.Describe(handle);
            _known[handle] = m;
        }
        return m.Label;
    }
}

internal static unsafe class Platform
{
    public static IntPtr ForegroundWindow() => OperatingSystem.IsWindows() ? Win32.GetForegroundWindow() : IntPtr.Zero;

    static readonly string[] ProcessKeywords =
    [
        "magic", "logi", "lghub", "xmouse", "smoothscroll", "ds4", "dsx", "steam", "autohotkey", "ahk",
        "razer", "synapse", "inputmapper", "rewasd", "hidhide", "bootcamp", "apple", "joytokey", "powertoys",
    ];

    static readonly string[] ServiceKeywords = ["apple", "magic", "bootcamp", "hidhide", "vigem", "rewasd", "interception", "mouse"];

    public static IEnumerable<string> MatchingProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (ProcessKeywords.Any(k => p.ProcessName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    names.Add(p.ProcessName);
            }
            catch { }
        }
        return names;
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<string> MatchingServices()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            return key?.GetSubKeyNames()
                .Where(n => ServiceKeywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n).ToList() ?? [];
        }
        catch (Exception e) { return [$"（读不了：{e.Message}）"]; }
    }

    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr hMem);

    [SupportedOSPlatform("windows")]
    public static bool CopyToClipboard(string text)
    {
        const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var h = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (h == IntPtr.Zero) return false;
            var p = (char*)GlobalLock(h);
            fixed (char* src = text) Buffer.MemoryCopy(src, p, bytes, text.Length * 2);
            p[text.Length] = '\0';
            GlobalUnlock(h);
            return SetClipboardData(CF_UNICODETEXT, h) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }
}
