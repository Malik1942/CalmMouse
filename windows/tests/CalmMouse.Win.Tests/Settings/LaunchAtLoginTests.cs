using CalmMouse.Win.Settings;
using Microsoft.Win32;

namespace CalmMouse.Win.Tests.Settings;

/// <summary>Against a throwaway key, HKCU\Software\CalmMouse.Tests\&lt;guid&gt;\Run, deleted afterwards. Never the real Run key.</summary>
public sealed class LaunchAtLoginTests : IDisposable
{
    private readonly string _parent = $@"Software\CalmMouse.Tests\{Guid.NewGuid():N}";
    private string RunKey => _parent + @"\Run";
    private string ApprovedKey => _parent + @"\StartupApproved\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_parent, throwOnMissingSubKey: false);
        using RegistryKey? tests = Registry.CurrentUser.OpenSubKey(@"Software\CalmMouse.Tests", writable: true);
        if (tests is { SubKeyCount: 0, ValueCount: 0 }) { Registry.CurrentUser.DeleteSubKey(@"Software\CalmMouse.Tests", throwOnMissingSubKey: false); }
    }

    private LaunchAtLogin For(string exe) => new(exe, Registry.CurrentUser, RunKey, ApprovedKey);

    private void SetApproved(byte first)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        key.SetValue(LaunchAtLogin.ValueName, new byte[] { first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }

    private string? Value()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(LaunchAtLogin.ValueName) as string;
    }

    [Fact]
    public void ItsKeyIsTheRealRunKeyUnlessATestSaysOtherwise()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", LaunchAtLogin.RunKeyPath);
        Assert.Equal("CalmMouse", LaunchAtLogin.ValueName);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", LaunchAtLogin.ApprovedKeyPath);
    }

    [Fact]
    public void TurningItOnWritesTheQuotedExePath()
    {
        LaunchAtLogin launch = For(@"C:\Program Files\CalmMouse\CalmMouse.Win.exe");
        Assert.False(launch.IsRegistered);
        Assert.True(launch.Apply(true));
        Assert.Equal("\"C:\\Program Files\\CalmMouse\\CalmMouse.Win.exe\"", Value());
        Assert.True(launch.IsCurrent);
        Assert.False(launch.Apply(true));   // already right
    }

    [Fact]
    public void TurningItOffRemovesTheValue()
    {
        LaunchAtLogin launch = For(@"C:\Apps\CalmMouse.Win.exe");
        launch.Apply(true);
        Assert.True(launch.Apply(false));
        Assert.Null(Value());
        Assert.False(launch.Apply(false));   // nothing left to remove
    }

    [Fact]
    public void RepairRewritesAStalePathWhenOn()
    {
        For(@"C:\Old place\CalmMouse.Win.exe").Apply(true);
        LaunchAtLogin moved = For(@"D:\New place\CalmMouse.Win.exe");
        Assert.False(moved.IsCurrent);
        Assert.True(moved.Repair(enabled: true));
        Assert.Equal("\"D:\\New place\\CalmMouse.Win.exe\"", Value());
    }

    [Fact]
    public void ReadsWhatWindowsWillReallyDo()
    {
        LaunchAtLogin launch = For(@"C:\Apps\CalmMouse.Win.exe");
        Assert.Equal(LaunchAtLoginState.Off, launch.State);
        launch.Apply(true);
        Assert.Equal(LaunchAtLoginState.On, launch.State);    // no StartupApproved entry: on
        SetApproved(0x02);
        Assert.Equal(LaunchAtLoginState.On, launch.State);    // even first byte: on
        SetApproved(0x03);
        Assert.Equal(LaunchAtLoginState.TurnedOffInWindows, launch.State);   // turned off in Task Manager
        Assert.False(launch.Apply(true));                      // and CalmMouse doesn't override it
        Assert.Equal(LaunchAtLoginState.TurnedOffInWindows, launch.State);
        SetApproved(0x06);
        Assert.Equal(LaunchAtLoginState.On, launch.State);
        Assert.Equal(LaunchAtLoginState.OtherCopy, For(@"D:\Elsewhere\CalmMouse.Win.exe").State);
    }

    [Theory]
    [InlineData(@"C:\Program Files\CalmMouse\CalmMouse.Win.exe", false, true)]
    [InlineData(@"C:\Program Files\CalmMouse\calmmouse.win.exe", false, true)]
    [InlineData(@"D:\CalmMouse\windows\src\CalmMouse.Win\bin\Debug\CalmMouse.Win.exe", true, false)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false, false)]
    [InlineData(@"C:\Temp\CalmMouse.Win.Tests.exe", false, false)]
    public void OnlyTheReleaseExeRepairsTheEntry(string exe, bool debugBuild, bool may)
    {
        Assert.Equal(may, LaunchAtLogin.MayRepair(exe, debugBuild));
    }

    [Fact]
    public void RepairLeavesThingsAloneWhenOff()
    {
        For(@"C:\Old place\CalmMouse.Win.exe").Apply(true);
        Assert.False(For(@"D:\New place\CalmMouse.Win.exe").Repair(enabled: false));
        Assert.Equal("\"C:\\Old place\\CalmMouse.Win.exe\"", Value());
    }
}
