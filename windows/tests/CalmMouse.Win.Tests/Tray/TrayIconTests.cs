using System.Drawing;
using CalmMouse.Win.Localization;
using CalmMouse.Win.Tray;

namespace CalmMouse.Win.Tests.Tray;

public sealed class TrayIconTests
{
    [Theory]
    [InlineData(TrayState.Normal, false)]
    [InlineData(TrayState.Normal, true)]
    [InlineData(TrayState.Paused, false)]
    [InlineData(TrayState.Paused, true)]
    [InlineData(TrayState.Problem, false)]
    [InlineData(TrayState.Problem, true)]
    public void EveryIconHasEverySize(TrayState state, bool lightTaskbar)
    {
        using Stream stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(TrayIcon.IconResourceName(state, lightTaskbar))!;
        Assert.NotNull(stream);
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        foreach (int size in new[] { 16, 20, 24, 28, 32, 40, 48 })
        {
            buffer.Position = 0;
            using var icon = new Icon(buffer, size, size);
            Assert.Equal(new Size(size, size), icon.Size);   // an exact frame, not a scaled one
        }
        using Icon loaded = TrayIcon.LoadIcon(state, lightTaskbar);
        Assert.Equal(TrayIcon.SmallIconSize(), loaded.Width);
    }

    [Fact]
    public void TheTooltipSaysWhatIsGoingOn()
    {
        Assert.Equal(Strings.L("CalmMouse — active"), TrayIcon.Tooltip(new TrayView(TrayState.Normal, null, false)));
        Assert.Equal(Strings.L("CalmMouse is paused"), TrayIcon.Tooltip(new TrayView(TrayState.Paused, null, false)));
        Assert.Equal("Something is wrong.", TrayIcon.Tooltip(new TrayView(TrayState.Problem, "Something is wrong.", true)));
    }

    [Fact]
    public void ProblemTooltipsFitTheTray()
    {
        // A tray tooltip holds at most 127 characters.
        var tables = Localizer.LoadEmbeddedTables();
        string[] keys =
        [
            "CalmMouse stopped because of a problem. Your mouse scrolls as usual.",
            "CalmMouse couldn't start. Your mouse scrolls as usual.",
            "Something on this PC is blocking CalmMouse, so your mouse scrolls as usual for now. It will try again soon.",
            "CalmMouse — active",
            "CalmMouse is paused",
        ];
        foreach (UiLanguage language in Enum.GetValues<UiLanguage>())
        {
            var l = new Localizer(tables, language);
            Assert.All(keys, key => Assert.True(l.Get(key).Length <= 127, $"{language}: \"{l.Get(key)}\" is too long"));
        }
    }
}
