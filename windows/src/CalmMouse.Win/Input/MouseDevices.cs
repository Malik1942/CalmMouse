// The mice Raw Input knows about: what each one is called, how its settings are keyed, and
// whether it is a laptop touchpad (which CalmMouse leaves alone).
//
// A precision touchpad is one HID device with several top-level collections: a plain mouse
// collection (the one Raw Input lists as a mouse, and the one whose wheel events we see) and a
// digitizer collection with usage page 0x0D, usage 0x05 (Touch Pad). They are siblings under the
// same parent in the device tree, e.g. on the Phase 0 laptop HID\MSFT0001&COL01 (mouse) and
// HID\MSFT0001&COL03 (touch pad) under ACPI\MSFT0001 ("I2C HID Device"). So a mouse is a touchpad
// when a sibling of its collection has HID_DEVICE_UP:000D_U:0005 among its hardware IDs. If the
// device tree can't be read, the fallback compares interface paths with Raw Input's own list of
// HID collections.

using System.Runtime.InteropServices;
using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Input;

/// <summary>A mouse as the settings window shows it.</summary>
/// <param name="DeviceId">The Raw Input handle (hDevice); changes on every reconnect.</param>
/// <param name="StableKey">
/// What its settings are stored under (<see cref="HidPath.StableKey"/>): vendor and product ID (from
/// the device path, else the HID attributes Windows keeps) and the parent device's bus-reported
/// description from the device tree, else empty. The same mouse gets the same key every time, even
/// when asking the device fails or the display language changes.
/// </param>
/// <param name="DisplayName">Its product name, or the best name Windows has for it. May differ from the key's name.</param>
public sealed record MouseDeviceInfo(long DeviceId, DeviceKind Kind, string StableKey, string DisplayName)
{
    public ushort? Vid { get; init; }
    public ushort? Pid { get; init; }
    /// <summary>The Raw Input device interface path.</summary>
    public string Path { get; init; } = "";
    public int Buttons { get; init; }
    public bool HasHorizontalWheel { get; init; }
    /// <summary>How the kind was decided, for the log ("device tree", "path match", "no digitizer sibling").</summary>
    public string KindSource { get; init; } = "";

    public override string ToString()
    {
        string ids = Vid is { } v && Pid is { } p ? $"VID {v:X4} PID {p:X4}" : "no VID/PID";
        return $"{Kind} \"{DisplayName}\" [{ids}] key={StableKey} handle=0x{DeviceId:X} ({KindSource})";
    }
}

internal static unsafe class MouseDevices
{
    private readonly record struct HidCollection(string Path, ushort UsagePage, ushort Usage);

    /// <summary>Every mouse Raw Input lists right now. Slow-ish (opens each device): not for the hook path.</summary>
    public static List<MouseDeviceInfo> Enumerate()
    {
        var result = new List<MouseDeviceInfo>();
        List<HidCollection>? hid = null;
        foreach ((nint handle, uint type) in DeviceList())
        {
            if (type != Native.RIM_TYPEMOUSE) { continue; }
            hid ??= HidCollections();
            MouseDeviceInfo? info = Describe(handle, hid);
            if (info != null) { result.Add(info); }
        }
        return result;
    }

    /// <summary>One mouse by its Raw Input handle; null if it is gone or isn't a mouse.</summary>
    public static MouseDeviceInfo? Describe(nint handle) => Describe(handle, null);

    private static MouseDeviceInfo? Describe(nint handle, List<HidCollection>? hid)
    {
        try
        {
            Native.RID_DEVICE_INFO info = default;
            info.cbSize = (uint)sizeof(Native.RID_DEVICE_INFO);
            uint size = info.cbSize;
            if ((int)Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICEINFO, &info, &size) <= 0) { return null; }
            if (info.dwType != Native.RIM_TYPEMOUSE) { return null; }

            string path = DeviceName(handle) ?? "";
            (ushort? vid, ushort? pid) = HidPath.ParseVidPid(path);
            string? product = null;
            if (path.Length > 0) { ReadHid(path, ref vid, ref pid, out product); }

            string? instanceId = InterfaceInstanceId(path) ?? HidPath.InstanceIdFromInterfacePath(path);
            uint parent = 0;
            uint self = instanceId != null ? Locate(instanceId) : 0;
            if (self != 0 && Native.CM_Get_Parent(&parent, self, 0) != Native.CR_SUCCESS) { parent = 0; }

            DeviceKind kind;
            string source;
            if (parent != 0)
            {
                kind = HidPath.KindFromSiblingUsages(SiblingDigitizerUsages(parent));
                source = "device tree";
            }
            else
            {
                hid ??= HidCollections();
                string? group = HidPath.SiblingGroupKey(path);
                var usages = hid.Where(h => h.UsagePage == HidPath.DigitizerPage && group != null && HidPath.SiblingGroupKey(h.Path) == group)
                                .Select(h => h.Usage);
                kind = HidPath.KindFromSiblingUsages(usages);
                source = "path match";
            }

            string? parentBus = parent != 0 ? DevNodeString(parent, Native.DEVPKEY_Device_BusReportedDeviceDesc) : null;
            string? parentDesc = parent != 0 ? DevNodeString(parent, Native.DEVPKEY_Device_DeviceDesc) : null;

            // The settings key's name is the bus-reported description Windows keeps in the device
            // tree (the product string from the device's USB or Bluetooth descriptor, read once when
            // it was connected), else nothing. Never the HID product string, which is a request to the
            // device that a sleepy Bluetooth or I2C device can fail now and then, and never a
            // driver's description, which is translated into the Windows display language.
            // See HidPath.StableKey.
            string keyName = FirstName(parentBus) ?? "";
            string name = FirstName(
                product,
                parentBus,
                self != 0 ? DevNodeString(self, Native.DEVPKEY_Device_BusReportedDeviceDesc) : null,
                parent != 0 ? DevNodeString(parent, Native.DEVPKEY_Device_FriendlyName) : null,
                self != 0 ? DevNodeString(self, Native.DEVPKEY_Device_FriendlyName) : null,
                parentDesc)
                ?? (kind == DeviceKind.Touchpad ? "Touchpad" : "Mouse");

            return new MouseDeviceInfo((long)handle, kind, HidPath.StableKey(vid, pid, keyName), name)
            {
                Vid = vid,
                Pid = pid,
                Path = path,
                Buttons = (int)info.mouse_dwNumberOfButtons,
                HasHorizontalWheel = info.mouse_fHasHorizontalWheel != 0,
                KindSource = source,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstName(params string?[] names) =>
        names.Select(n => n?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));

    private static List<(nint Handle, uint Type)> DeviceList()
    {
        uint count = 0;
        uint itemSize = (uint)sizeof(Native.RAWINPUTDEVICELIST);
        var result = new List<(nint, uint)>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (Native.GetRawInputDeviceList(null, &count, itemSize) == uint.MaxValue || count == 0) { return result; }
            var items = new Native.RAWINPUTDEVICELIST[count + 4];   // room for one arriving meanwhile
            uint capacity = (uint)items.Length;
            uint got;
            fixed (Native.RAWINPUTDEVICELIST* p = items) { got = Native.GetRawInputDeviceList(p, &capacity, itemSize); }
            if (got == uint.MaxValue) { count = capacity; continue; }   // grew in between: ask again
            for (int i = 0; i < got; i++) { result.Add((items[i].hDevice, items[i].dwType)); }
            return result;
        }
        return result;
    }

    private static List<HidCollection> HidCollections()
    {
        var result = new List<HidCollection>();
        foreach ((nint handle, uint type) in DeviceList())
        {
            if (type != Native.RIM_TYPEHID) { continue; }
            Native.RID_DEVICE_INFO info = default;
            info.cbSize = (uint)sizeof(Native.RID_DEVICE_INFO);
            uint size = info.cbSize;
            if ((int)Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICEINFO, &info, &size) <= 0) { continue; }
            string? path = DeviceName(handle);
            if (path != null) { result.Add(new HidCollection(path, info.hid_usUsagePage, info.hid_usUsage)); }
        }
        return result;
    }

    private static string? DeviceName(nint handle)
    {
        uint chars = 0;
        Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICENAME, null, &chars);
        if (chars == 0 || chars > 4096) { return null; }
        char* buffer = stackalloc char[(int)chars + 1];
        if ((int)Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICENAME, buffer, &chars) <= 0) { return null; }
        buffer[chars] = '\0';
        return new string(buffer);
    }

    /// <summary>
    /// The HID attributes and product string. Windows opens mice exclusively, but a handle with no
    /// access at all still answers these. Fails quietly for devices that aren't HID. The attributes
    /// (vendor and product ID) are kept by Windows and always answer; the product string is asked
    /// of the device, so it is only used for the display name.
    /// </summary>
    private static void ReadHid(string path, ref ushort? vid, ref ushort? pid, out string? product)
    {
        product = null;
        nint file;
        fixed (char* p = path)
        {
            file = Native.CreateFileW(p, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0, Native.OPEN_EXISTING, 0, 0);
        }
        if (file == Native.INVALID_HANDLE_VALUE || file == 0) { return; }
        try
        {
            var attributes = new Native.HIDD_ATTRIBUTES { Size = (uint)sizeof(Native.HIDD_ATTRIBUTES) };
            if (Native.HidD_GetAttributes(file, &attributes) != 0)
            {
                vid ??= attributes.VendorID;
                pid ??= attributes.ProductID;
            }
            char* buffer = stackalloc char[256];
            if (Native.HidD_GetProductString(file, buffer, 256 * sizeof(char)) != 0)
            {
                buffer[255] = '\0';
                product = new string(buffer).Trim();
            }
        }
        finally
        {
            Native.CloseHandle(file);
        }
    }

    private static string? InterfaceInstanceId(string path)
    {
        if (path.Length == 0) { return null; }
        Native.DEVPROPKEY key = Native.DEVPKEY_Device_InstanceId;
        byte* buffer = stackalloc byte[1024];
        uint size = 1024, type;
        uint result;
        fixed (char* p = path) { result = Native.CM_Get_Device_Interface_PropertyW(p, &key, &type, buffer, &size, 0); }
        if (result != Native.CR_SUCCESS || type != Native.DEVPROP_TYPE_STRING) { return null; }
        return new string((char*)buffer).Trim();
    }

    private static uint Locate(string instanceId)
    {
        uint devinst = 0;
        fixed (char* p = instanceId)
        {
            return Native.CM_Locate_DevNodeW(&devinst, p, 0) == Native.CR_SUCCESS ? devinst : 0;
        }
    }

    /// <summary>The digitizer usages among the children of <paramref name="parent"/> (the collection's siblings and itself).</summary>
    private static List<ushort> SiblingDigitizerUsages(uint parent)
    {
        var usages = new List<ushort>();
        uint child = 0;
        if (Native.CM_Get_Child(&child, parent, 0) != Native.CR_SUCCESS) { return usages; }
        for (int guard = 0; guard < 64 && child != 0; guard++)
        {
            if (HidPath.DigitizerUsage(DevNodeStrings(child, Native.DEVPKEY_Device_HardwareIds)) is { } usage) { usages.Add(usage); }
            uint next = 0;
            if (Native.CM_Get_Sibling(&next, child, 0) != Native.CR_SUCCESS) { break; }
            child = next;
        }
        return usages;
    }

    private static string? DevNodeString(uint devinst, Native.DEVPROPKEY key)
    {
        byte[]? data = DevNodeProperty(devinst, key, out uint type);
        if (data == null || type != Native.DEVPROP_TYPE_STRING) { return null; }
        return MemoryMarshal.Cast<byte, char>(data).ToString().TrimEnd('\0').Trim();
    }

    private static List<string> DevNodeStrings(uint devinst, Native.DEVPROPKEY key)
    {
        byte[]? data = DevNodeProperty(devinst, key, out uint type);
        if (data == null || type != Native.DEVPROP_TYPE_STRING_LIST) { return []; }
        return MemoryMarshal.Cast<byte, char>(data).ToString()
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static byte[]? DevNodeProperty(uint devinst, Native.DEVPROPKEY key, out uint type)
    {
        type = 0;
        uint size = 0;
        uint t;
        uint result = Native.CM_Get_DevNode_PropertyW(devinst, &key, &t, null, &size, 0);
        if (result != Native.CR_BUFFER_SMALL || size == 0 || size > 65536) { return null; }
        var data = new byte[size];
        fixed (byte* p = data)
        {
            if (Native.CM_Get_DevNode_PropertyW(devinst, &key, &t, p, &size, 0) != Native.CR_SUCCESS) { return null; }
        }
        type = t;
        return data;
    }
}
