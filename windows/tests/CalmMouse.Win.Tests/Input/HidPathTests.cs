using CalmMouse.Core.Routing;
using CalmMouse.Win.Input;

namespace CalmMouse.Win.Tests.Input;

/// <summary>Device paths and hardware IDs as the Phase 0 laptop and Bluetooth mice give them.</summary>
public sealed class HidPathTests
{
    private const string Receiver = @"\\?\HID#VID_3554&PID_FA09&MI_01&Col05#9&1d9c93df&0&0004#{378de44c-56ef-11d1-bc8c-00a0c91e6bf6}";
    private const string TouchpadMouse = @"\\?\HID#MSFT0001&Col01#4&f9408d4&1&0000#{378de44c-56ef-11d1-bc8c-00a0c91e6bf6}";
    private const string TouchpadDigitizer = @"\\?\HID#MSFT0001&Col03#4&f9408d4&1&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void ReadsVidAndPidFromAUsbPath()
    {
        Assert.Equal(((ushort?)0x3554, (ushort?)0xFA09), HidPath.ParseVidPid(Receiver));
    }

    [Fact]
    public void ReadsVidAndPidFromBluetoothPaths()
    {
        // Classic Bluetooth (0001 prefix) and Bluetooth LE (0002), as Apple mice show up.
        Assert.Equal(((ushort?)0x004C, (ushort?)0x0269),
            HidPath.ParseVidPid(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0269&Col01#8&2f1a2b3c&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91e6bf6}"));
        Assert.Equal(((ushort?)0x046D, (ushort?)0xB023),
            HidPath.ParseVidPid(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}&Dev&VID_046d&PID_b023&REV_0011_d00f35a6b1c2&Col01#9&1&0000#{378de44c-56ef-11d1-bc8c-00a0c91e6bf6}"));
    }

    [Fact]
    public void APathWithoutIdsGivesNulls()
    {
        Assert.Equal(((ushort?)null, (ushort?)null), HidPath.ParseVidPid(TouchpadMouse));
        Assert.Equal(((ushort?)null, (ushort?)null), HidPath.ParseVidPid(@"\\?\Root#RDP_MOU#0000#{378de44c-56ef-11d1-bc8c-00a0c91e6bf6}"));
        Assert.Equal(((ushort?)null, (ushort?)null), HidPath.ParseVidPid(null));
    }

    [Fact]
    public void TurnsAnInterfacePathIntoItsInstanceId()
    {
        Assert.Equal(@"HID\VID_3554&PID_FA09&MI_01&Col05\9&1d9c93df&0&0004", HidPath.InstanceIdFromInterfacePath(Receiver));
        Assert.Equal(@"HID\MSFT0001&Col01\4&f9408d4&1&0000", HidPath.InstanceIdFromInterfacePath(TouchpadMouse));
        Assert.Null(HidPath.InstanceIdFromInterfacePath("not a path"));
        Assert.Null(HidPath.InstanceIdFromInterfacePath(""));
    }

    [Fact]
    public void SiblingCollectionsShareAGroupKey()
    {
        Assert.Equal(HidPath.SiblingGroupKey(TouchpadMouse), HidPath.SiblingGroupKey(TouchpadDigitizer));
        Assert.Equal(@"HID\MSFT0001\4&F9408D4&1", HidPath.SiblingGroupKey(TouchpadMouse));
        Assert.NotEqual(HidPath.SiblingGroupKey(Receiver), HidPath.SiblingGroupKey(TouchpadMouse));
    }

    [Fact]
    public void TheStableKeyIsVidPidAndName()
    {
        Assert.Equal("3554:FA09:2.4G Wireless Receiver", HidPath.StableKey(0x3554, 0xFA09, " 2.4G Wireless Receiver "));
        Assert.Equal("?:?:Mouse", HidPath.StableKey(null, null, "Mouse"));
        Assert.Equal("04F3:31AD:", HidPath.StableKey(0x04F3, 0x31AD, null));
    }

    [Fact]
    public void ReadsTheDigitizerUsageFromHardwareIds()
    {
        string[] touchpad = [@"HID\MSFT0001&Col03", @"HID\VEN_MSFT&DEV_0001&Col03", "HID_DEVICE_SYSTEM_VHF", "HID_DEVICE_UP:000D_U:0005", "HID_DEVICE"];
        string[] mouse = [@"HID\VID_5253&PID_1020&MI_00", "HID_DEVICE_SYSTEM_MOUSE", "HID_DEVICE_UP:0001_U:0002", "HID_DEVICE"];
        string[] screen = ["HID_DEVICE_UP:000D_U:0004"];
        Assert.Equal((ushort?)5, HidPath.DigitizerUsage(touchpad));
        Assert.True(HidPath.IsTouchpadHardwareId(touchpad));
        Assert.Null(HidPath.DigitizerUsage(mouse));
        Assert.False(HidPath.IsTouchpadHardwareId(mouse));
        Assert.Equal((ushort?)4, HidPath.DigitizerUsage(screen));
        Assert.False(HidPath.IsTouchpadHardwareId(screen));
        Assert.Null(HidPath.DigitizerUsage(null));
    }

    [Fact]
    public void ATouchpadSiblingMakesATouchpadAndAnotherDigitizerMakesUnknown()
    {
        Assert.Equal(DeviceKind.WheelMouse, HidPath.KindFromSiblingUsages([]));
        Assert.Equal(DeviceKind.Touchpad, HidPath.KindFromSiblingUsages([5]));
        Assert.Equal(DeviceKind.Touchpad, HidPath.KindFromSiblingUsages([2, 5]));
        Assert.Equal(DeviceKind.Unknown, HidPath.KindFromSiblingUsages([4]));
        Assert.Equal(DeviceKind.Unknown, HidPath.KindFromSiblingUsages([2]));
    }
}
