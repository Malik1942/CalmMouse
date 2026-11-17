using System.Runtime.Versioning;
using static CalmMouseProbe.Win32;

namespace CalmMouseProbe;

/// Turns the recordings into the plain numbers the spec's Phase 0 table asks for.
[SupportedOSPlatform("windows")]
internal static class Analysis
{
    static void Errors(Report r, MouseSession s)
    {
        foreach (var e in s.Errors) r.Line($"出错：{e}");
    }

    static string Ms(double v) => $"{v:0.0} ms";

    static double Percentile(List<double> sorted, double q) =>
        sorted.Count == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Count) - 1, 0, sorted.Count - 1)];

    static string Stats(List<double> values)
    {
        if (values.Count == 0) return "无数据";
        var sorted = values.OrderBy(v => v).ToList();
        return $"中位数 {Ms(Percentile(sorted, 0.5))}，95% {Ms(Percentile(sorted, 0.95))}，最大 {Ms(sorted[^1])}（{sorted.Count} 个）";
    }

    /// S1 / S2, first half: which device each wheel event came from, and how the hook and
    /// Raw Input line up in time.
    public static void Observe(Report r, MouseSession s, DeviceNames names)
    {
        Errors(r, s);
        var physical = s.Hook.Where(e => e.IsWheel && !e.Injected).ToList();
        var injected = s.Hook.Where(e => e.IsWheel && e.Injected && !e.Ours).ToList();
        r.Line($"钩子看到的滚动：来自硬件 {physical.Count} 次，来自其他程序注入 {injected.Count} 次");

        var rawWheels = s.Raw.Where(e => e.IsWheel).ToList();
        foreach (var g in rawWheels.GroupBy(e => e.Device))
        {
            var deltas = g.Select(e => (int)e.Delta).ToList();
            var common = deltas.GroupBy(d => d).OrderByDescending(x => x.Count()).Take(5)
                .Select(x => $"{x.Key}×{x.Count()}");
            int vertical = g.Count(e => (e.ButtonFlags & RI_MOUSE_WHEEL) != 0);
            r.Line($"Raw Input 滚动 · {names.Of(g.Key)}：竖向 {vertical} 次，横向 {g.Count() - vertical} 次；" +
                   $"最小幅度 {deltas.Where(d => d != 0).Select(Math.Abs).DefaultIfEmpty(0).Min()}；常见数值 {string.Join(" ", common)}");
        }
        foreach (var g in s.Raw.Where(e => (e.ButtonFlags & (RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_DOWN | RI_MOUSE_MIDDLE_BUTTON_DOWN)) != 0)
                     .GroupBy(e => e.Device))
            r.Line($"Raw Input 按下 · {names.Of(g.Key)}：{g.Count()} 次");

        if (physical.Count > 0)
        {
            var pending = physical.Count(e => e.RawPending);
            r.Line($"钩子运行时，对应的 WM_INPUT 已经在队列里的比例：{pending}/{physical.Count}");
        }
        var rawReal = rawWheels.Where(e => e.Device != IntPtr.Zero).ToList();
        int n = Math.Min(physical.Count, rawReal.Count);
        if (n > 0)
        {
            var diffs = Enumerable.Range(0, n).Select(i => rawReal[i].T - physical[i].T).ToList();
            r.Line($"按顺序配对（钩子 {physical.Count} 个，Raw Input {rawReal.Count} 个）：Raw Input 比钩子晚 {Stats(diffs)}；" +
                   $"Raw Input 反而更早的 {diffs.Count(d => d < 0)} 个");
        }
    }

    /// S1, second half: with the hook dropping physical wheel events, does Raw Input still
    /// see them, and how late does the re-sent copy arrive?
    public static void Resend(Report r, MouseSession s)
    {
        Errors(r, s);
        var swallowed = s.Hook.Where(e => e.IsWheel && !e.Injected).ToList();
        var rawReal = s.Raw.Where(e => e.IsWheel && e.Device != IntPtr.Zero).ToList();
        var oursSeen = s.Hook.Where(e => e.IsWheel && e.Ours).ToList();
        r.Line($"钩子拦下的硬件滚动：{swallowed.Count} 次");
        r.Line($"拦下之后 Raw Input 仍然收到的：{rawReal.Count} 次" +
               (swallowed.Count > 0 ? $"（{100.0 * rawReal.Count / swallowed.Count:0}%）" : ""));
        r.Line($"重新发出：{s.Resent.Count} 次；钩子看到重发的：{oursSeen.Count} 次");
        int n = Math.Min(swallowed.Count, oursSeen.Count);
        if (n > 0)
            r.Line($"从硬件滚动到重发到达的延迟：{Stats(Enumerable.Range(0, n).Select(i => oursSeen[i].T - swallowed[i].T).ToList())}");
        int unknown = s.Raw.Count(e => e.IsWheel && e.Device == IntPtr.Zero);
        r.Line($"没有设备的 Raw Input 滚动（应当就是我们重发的）：{unknown} 次");
    }

    /// S2: while a Magic Mouse button is down (plus a short tail), how much scrolling leaks
    /// out, and from which source.
    public static void ClickLeak(Report r, MouseSession s, DeviceNames names, HashSet<IntPtr> magicMice)
    {
        Errors(r, s);
        var clicks = new List<(IntPtr Device, double Down, double Up)>();
        var downAt = new Dictionary<(IntPtr, int), double>();
        foreach (var e in s.Raw.Where(e => magicMice.Contains(e.Device)))
        {
            (ushort down, ushort up, int id)[] buttons =
            [
                (RI_MOUSE_LEFT_BUTTON_DOWN, RI_MOUSE_LEFT_BUTTON_UP, 0),
                (RI_MOUSE_RIGHT_BUTTON_DOWN, RI_MOUSE_RIGHT_BUTTON_UP, 1),
            ];
            foreach (var (down, up, id) in buttons)
            {
                if ((e.ButtonFlags & down) != 0) downAt[(e.Device, id)] = e.T;
                if ((e.ButtonFlags & up) != 0 && downAt.Remove((e.Device, id), out var t)) clicks.Add((e.Device, t, e.T));
            }
        }
        r.Line($"Magic Mouse 点按：{clicks.Count} 次");
        if (clicks.Count == 0) return;
        r.Line($"按住时长：{Stats(clicks.Select(c => c.Up - c.Down).ToList())}");

        const double before = 100, after = 300;
        int leaking = 0, fromMouse = 0, fromOtherDevice = 0, fromInjected = 0;
        int sumMouse = 0;
        foreach (var c in clicks)
        {
            bool In(double t) => t >= c.Down - before && t <= c.Up + after;
            var same = s.Raw.Where(e => e.IsWheel && e.Device == c.Device && In(e.T)).ToList();
            var other = s.Raw.Where(e => e.IsWheel && e.Device != c.Device && In(e.T)).ToList();
            var inj = s.Hook.Where(e => e.IsWheel && e.Injected && !e.Ours && In(e.T)).ToList();
            if (same.Count + other.Count + inj.Count > 0) leaking++;
            fromMouse += same.Count;
            sumMouse += same.Sum(e => Math.Abs((int)e.Delta));
            fromOtherDevice += other.Count;
            fromInjected += inj.Count;
        }
        r.Line($"点按期间（按下前 {before:0} ms 到松开后 {after:0} ms）出现滚动的点按：{leaking}/{clicks.Count}");
        r.Line($"  其中滚动来自 Magic Mouse 本身：{fromMouse} 次（总幅度 {sumMouse}）；来自别的设备 / 无设备：{fromOtherDevice} 次；来自程序注入：{fromInjected} 次");
        var allMouseWheels = s.Raw.Where(e => e.IsWheel && magicMice.Contains(e.Device)).ToList();
        r.Line($"整段录制里 Magic Mouse 的滚动一共：{allMouseWheels.Count} 次");
        foreach (var d in magicMice) r.Line($"  设备：{names.Of(d)}");
    }
}
