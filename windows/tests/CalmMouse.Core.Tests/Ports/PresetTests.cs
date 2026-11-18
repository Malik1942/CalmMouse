// Translated from Tests/CalmMouseCoreTests/PresetTests.swift. JSONEncoder / JSONDecoder are
// System.Text.Json with the converters in Ports/Presets.cs. xUnit's Assert.Equal takes no
// message, so the Swift messages on those asserts are kept as comments.

using System.Text.Json;

namespace CalmMouse.Core.Tests;

/// <summary>Built-in presets and the custom-preset persistence format.</summary>
public sealed class PresetTests
{
    // MARK: Built-ins

    [Fact]
    public void testThreeBuiltInPresetsWithDistinctIdentities()
    {
        Assert.Equal(3, Preset.BuiltIn.Count);
        Assert.Equal(3, Preset.BuiltIn.Select(p => p.Id).ToHashSet().Count);
        Assert.Equal(3, Preset.BuiltIn.Select(p => p.Name).ToHashSet(StringComparer.Ordinal).Count);
        foreach (Preset preset in Preset.BuiltIn)
        {
            Assert.False(preset.Summary.Length == 0, $"{preset.Name} needs a summary for its card");
        }
    }

    /// <summary>
    /// "Just fix clicking" *is* the factory configuration — the README and the
    /// onboarding flow both promise that.
    /// </summary>
    [Fact]
    public void testJustFixClickingMatchesFactoryDefaults()
    {
        Assert.Equal(new PresetValues(), Preset.JustFixClicking.Values);
    }

    [Fact]
    public void testBuiltInsAreDistinctFromEachOther()
    {
        Assert.NotEqual(new PresetValues(), Preset.ExtraSteady.Values);
        Assert.NotEqual(new PresetValues(), Preset.TrackpadFeel.Values);
        Assert.NotEqual(Preset.TrackpadFeel.Values, Preset.ExtraSteady.Values);
    }

    [Fact]
    public void testExtraSteadyCalmsScrollingWithoutTouchingTaps()
    {
        PresetValues v = Preset.ExtraSteady.Values;
        Assert.True(v.BlockScrollWhileClicked);
        Assert.True(v.DeadZone > 0);
        Assert.True(v.AxisLock);
        Assert.False(v.MomentumEnabled);
        Assert.False(v.TapToClick, "scroll preset shouldn't surprise-enable tapping");
    }

    [Fact]
    public void testTrackpadFeelEnablesTapsWithoutChangingScrollFeel()
    {
        PresetValues v = Preset.TrackpadFeel.Values;
        Assert.True(v.TapToClick);
        Assert.True(v.TapRightClick);
        // "the default mode — double-tap would silently eat double-clicks"
        Assert.Equal(RightClickMode.RightSide, v.TapRightClickMode);
        Assert.True(v.TapAndDrag);
        PresetValues scrollOnly = v;
        scrollOnly.TapToClick = false;
        scrollOnly.TapRightClick = false;
        scrollOnly.TapAndDrag = false;
        // "everything except taps stays factory"
        Assert.Equal(new PresetValues(), scrollOnly);
    }

    // MARK: Persistence

    [Fact]
    public void testPresetRoundTripsThroughJSON()
    {
        var values = new PresetValues();
        values.DeadZone = 12;
        values.TapToClick = true;
        values.TapZoneDepth = 0.65;
        var preset = new Preset(name: "Mine", summary: "notes", symbolName: "star", values: values);
        Preset decoded = JsonSerializer.Deserialize<Preset>(JsonSerializer.SerializeToUtf8Bytes(preset));
        Assert.Equal(preset, decoded);
    }

    /// <summary>
    /// A preset saved before a setting existed must keep decoding, with the new
    /// knob at its factory value.
    /// </summary>
    [Fact]
    public void testDecodingToleratesMissingKeys()
    {
        byte[] old = """{"deadZone": 7, "momentumEnabled": false}"""u8.ToArray();
        PresetValues values = JsonSerializer.Deserialize<PresetValues>(old);
        Assert.Equal(7, values.DeadZone);
        Assert.False(values.MomentumEnabled);
        Assert.Equal(200, values.ReleaseGraceMs);
        Assert.True(values.BlockScrollWhileClicked);
        Assert.False(values.TapRightClick, "settings newer than the saved preset stay factory");
        Assert.Equal(RightClickMode.RightSide, values.TapRightClickMode);
    }
}
