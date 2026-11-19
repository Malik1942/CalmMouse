namespace CalmMouse.Win.Tests;

/// <summary>The app's log, in a throwaway folder under %TEMP%.</summary>
public sealed class AppLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"CalmMouse.Tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void ItRollsOverWhenItGrowsTooBigWhileRunning()
    {
        string path = Path.Combine(_folder, "CalmMouse.log");
        using (var log = new AppLog(path, maxBytes: 2_000))
        {
            for (int i = 0; i < 100; i++) { log.Write($"line {i} {new string('x', 50)}"); }
            Assert.True(File.Exists(log.OldPath));
            Assert.True(new FileInfo(path).Length < 2_000 + 200);
            Assert.True(new FileInfo(log.OldPath).Length < 2_000 + 200);
        }
        Assert.Contains("line 99", File.ReadAllText(path));
    }

    [Fact]
    public void AnOversizedLogIsSetAsideAtStart()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "CalmMouse.log");
        File.WriteAllText(path, new string('x', 3_000));
        using (var log = new AppLog(path, maxBytes: 2_000)) { log.Write("fresh"); }
        Assert.Equal(3_000, new FileInfo(Path.ChangeExtension(path, ".old.log")).Length);
        Assert.Contains("fresh", File.ReadAllText(path));
    }
}
