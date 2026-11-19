using System.Globalization;
using System.Text.RegularExpressions;

namespace CalmMouse.Win.Input;

/// <summary>
/// Pure helpers for Raw Input device interface paths, such as
/// <c>\\?\HID#VID_3554&amp;PID_FA09&amp;MI_01&amp;Col05#9&amp;1d9c93df&amp;0&amp;0004#{378de44c-…}</c>.
/// </summary>
public static partial class HidPath
{
    /// <summary>
    /// The vendor and product IDs in a device path. Handles USB (<c>VID_05AC&amp;PID_0269</c>) and
    /// Bluetooth (<c>VID&amp;0001004c_PID&amp;0269</c>, <c>VID&amp;0002…</c> for Bluetooth LE) paths.
    /// Null for each one the path doesn't carry (an I2C touchpad's <c>HID#MSFT0001&amp;Col01</c> has neither).
    /// </summary>
    public static (ushort? Vid, ushort? Pid) ParseVidPid(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return (null, null); }
        Match v = VidPattern().Match(path);
        Match p = PidPattern().Match(path);
        ushort? vid = v.Success ? ushort.Parse(v.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
        ushort? pid = p.Success ? ushort.Parse(p.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
        return (vid, pid);
    }

    /// <summary>
    /// The PnP device instance ID inside an interface path (<c>HID\VID_…&amp;Col05\9&amp;1d9c93df&amp;0&amp;0004</c>),
    /// for when Windows won't say it directly. Null if the path isn't in the usual form.
    /// </summary>
    public static string? InstanceIdFromInterfacePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }
        string s = path;
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal) || s.StartsWith(@"\??\", StringComparison.Ordinal)) { s = s[4..]; }
        int guid = s.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid > 0) { s = s[..guid]; }
        if (!s.Contains('#')) { return null; }
        return s.Replace('#', '\\');
    }

    /// <summary>
    /// What a mouse's settings are stored under: vendor ID, product ID and product name, so it
    /// survives reconnects and another USB port (the Raw Input handle doesn't). Two mice of the same
    /// model share it, and so their settings. <c>?</c> stands for an ID the device doesn't give.
    /// </summary>
    public static string StableKey(ushort? vid, ushort? pid, string? name)
    {
        string v = vid is { } x ? x.ToString("X4", CultureInfo.InvariantCulture) : "?";
        string p = pid is { } y ? y.ToString("X4", CultureInfo.InvariantCulture) : "?";
        return $"{v}:{p}:{(name ?? "").Trim()}";
    }

    /// <summary>
    /// Whether a HID collection's hardware IDs say it is a precision touchpad's touch collection
    /// (usage page 0x0D Digitizer, usage 0x05 Touch Pad).
    /// </summary>
    public static bool IsTouchpadHardwareId(IEnumerable<string>? hardwareIds) =>
        DigitizerUsage(hardwareIds) == TouchpadUsage;

    /// <summary>HID usage page 0x0D (Digitizer): 0x05 is a touch pad, 0x04 a touch screen, 0x02 a pen.</summary>
    public const ushort DigitizerPage = 0x0D, TouchpadUsage = 0x05;

    /// <summary>
    /// The digitizer usage a HID collection's hardware IDs carry (<c>HID_DEVICE_UP:000D_U:0005</c> → 5),
    /// or null if it isn't a digitizer collection.
    /// </summary>
    public static ushort? DigitizerUsage(IEnumerable<string>? hardwareIds)
    {
        if (hardwareIds == null) { return null; }
        foreach (string id in hardwareIds)
        {
            if (id == null) { continue; }
            Match m = DigitizerIdPattern().Match(id);
            if (m.Success) { return ushort.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
        }
        return null;
    }

    /// <summary>
    /// What a mouse collection is, from the digitizer usages of the other collections of the same
    /// device: a touch pad among them makes it a touchpad; another digitizer (touch screen, pen)
    /// makes it something that isn't a mouse to set up (Unknown); none makes it a wheel mouse.
    /// </summary>
    public static Core.Routing.DeviceKind KindFromSiblingUsages(IEnumerable<ushort> digitizerUsages)
    {
        bool other = false;
        foreach (ushort usage in digitizerUsages)
        {
            if (usage == TouchpadUsage) { return Core.Routing.DeviceKind.Touchpad; }
            other = true;
        }
        return other ? Core.Routing.DeviceKind.Unknown : Core.Routing.DeviceKind.WheelMouse;
    }

    [GeneratedRegex(@"HID_DEVICE_UP:000D_U:([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DigitizerIdPattern();

    /// <summary>
    /// The part of a HID collection's interface path that its sibling collections share: the path
    /// with <c>&amp;ColNN</c> and the collection number at the end of the instance removed, so
    /// <c>HID#MSFT0001&amp;Col01#4&amp;f9408d4&amp;1&amp;0000</c> and <c>HID#MSFT0001&amp;Col03#4&amp;f9408d4&amp;1&amp;0002</c>
    /// both give <c>HID#MSFT0001#4&amp;f9408d4&amp;1</c>. The fallback when the device tree can't be read.
    /// </summary>
    public static string? SiblingGroupKey(string? path)
    {
        string? id = InstanceIdFromInterfacePath(path);
        if (id == null) { return null; }
        string[] parts = id.Split('\\');
        if (parts.Length < 3) { return null; }
        string hardware = ColPattern().Replace(parts[1], "");
        string instance = parts[2];
        int amp = instance.LastIndexOf('&');
        if (amp > 0) { instance = instance[..amp]; }
        return $"{parts[0]}\\{hardware}\\{instance}".ToUpperInvariant();
    }

    [GeneratedRegex(@"VID[_&](?:000[12])?([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPattern();

    [GeneratedRegex(@"PID[_&]([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PidPattern();

    [GeneratedRegex(@"&Col[0-9A-F]{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColPattern();
}
