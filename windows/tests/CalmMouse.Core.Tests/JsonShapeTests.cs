using System.Text.Json;

namespace CalmMouse.Core.Tests;

/// <summary>
/// Not a Swift test file: pins the JSON the C# port reads and writes to the shape Swift's Codable
/// gives the same types (see the comments in Ports/Config.cs and Ports/Presets.cs), which the
/// golden traces and saved settings depend on.
/// </summary>
public sealed class JsonShapeTests
{
    [Fact]
    public void BlockerConfigWritesSwiftsKeysAndLeavesOutReverseMagicMouseScroll()
    {
        var c = new BlockerConfig { ReverseMagicMouseScroll = true };
        c.ModifierActions = new Dictionary<ModifierCombo, ScrollAction>
        {
            [ModifierCombo.Command | ModifierCombo.Shift] = ScrollAction.Block,
            [ModifierCombo.Option] = ScrollAction.Horizontal,
            [ModifierCombo.Control | ModifierCombo.Shift] = ScrollAction.ZoomControl,
        };
        Assert.Equal(
            """{"blockScrollWhileClicked":true,"releaseGrace":0.2,"deadZone":0,"axisLock":false,"axisLockThreshold":10,"axisLockRatio":1.4,"momentumEnabled":true,"blockHorizontalScroll":false,"disableScrollEntirely":false,"modifierActions":{"12":"zoomControl","2":"horizontal","9":"block"},"reverseWheelScroll":false,"blockScrollWhileWheelPressed":false,"smoothWheelScroll":false,"wheelScrollSpeed":3}""",
            JsonSerializer.Serialize(c));
    }

    [Fact]
    public void BlockerConfigTreatsMissingAndNullKeysAsDefaults()
    {
        BlockerConfig c = JsonSerializer.Deserialize<BlockerConfig>(
            """{"deadZone":null,"axisLock":true,"modifierActions":{"+2":"invert","8":"zoomCommand"},"extra":1}""");
        var expected = new BlockerConfig { AxisLock = true };
        expected.ModifierActions = new Dictionary<ModifierCombo, ScrollAction>
        {
            [ModifierCombo.Option] = ScrollAction.Invert,
            [ModifierCombo.Shift] = ScrollAction.ZoomCommand,
        };
        Assert.Equal(expected, c);
        Assert.Equal(new BlockerConfig(), JsonSerializer.Deserialize<BlockerConfig>("{}"));
    }

    [Fact]
    public void BadModifierActionsAreErrorsAsInSwift()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlockerConfig>("""{"modifierActions":{"option":"invert"}}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlockerConfig>("""{"modifierActions":{"2":"Invert"}}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlockerConfig>("""{"modifierActions":{"2":2}}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlockerConfig>("""{"axisLock":1}"""));
    }

    [Fact]
    public void AppRuleWritesAnUpperCaseUuidAndLeavesOutNils()
    {
        var rule = new AppRule(bundleID: "figma.exe", name: "Figma",
                               id: Guid.Parse("6e1b49f2-0001-4000-8000-00000000000a"), deadZone: 4.5);
        string json = JsonSerializer.Serialize(rule);
        Assert.Equal(
            """{"id":"6E1B49F2-0001-4000-8000-00000000000A","bundleID":"figma.exe","name":"Figma","enabled":true,"deadZone":4.5}""",
            json);
        Assert.Equal(rule, JsonSerializer.Deserialize<AppRule>(json));
    }

    [Fact]
    public void AppRuleNeedsItsNonOptionalKeys()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppRule>(
            """{"id":"6E1B49F2-0001-4000-8000-00000000000A","bundleID":"a","name":"A"}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppRule>(
            """{"id":"6E1B49F2-0001-4000-8000-00000000000A","bundleID":null,"name":"A","enabled":true}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppRule>(
            """{"id":"{6E1B49F2-0001-4000-8000-00000000000A}","bundleID":"a","name":"A","enabled":true}"""));
    }

    [Fact]
    public void RuleSetRoundTripsAndComparesByContent()
    {
        var set = new RuleSet(new BlockerConfig { DeadZone = 3 },
                              [new AppRule(bundleID: "a.exe", name: "A", axisLock: true)]);
        string json = JsonSerializer.Serialize(set);
        Assert.StartsWith("""{"base":{"blockScrollWhileClicked":true,""", json);
        Assert.Contains(""","appRules":[{"id":""", json);
        Assert.Equal(set, JsonSerializer.Deserialize<RuleSet>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuleSet>("""{"base":{}}"""));
    }

    [Fact]
    public void WheelSettingsUsesSynthesizedCodable()
    {
        Assert.Equal("""{"reverse":true,"blockWhilePressed":false,"smooth":true,"speed":2.5}""",
                     JsonSerializer.Serialize(new WheelSettings(reverse: true, smooth: true, speed: 2.5)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WheelSettings>("""{"reverse":true}"""));
    }

    [Fact]
    public void PresetWritesEveryValueAndRequiresItsOwnKeys()
    {
        Assert.Equal(
            """{"id":"6E1B49F2-0001-4000-8000-000000000002","name":"Extra steady","summary":"Ignores nudges and diagonal drift.\nThe page stops when your finger does.","symbolName":"scope","values":{"blockScrollWhileClicked":true,"releaseGraceMs":300,"deadZone":8,"axisLock":true,"axisLockThreshold":10,"momentumEnabled":false,"blockHorizontalScroll":false,"tapToClick":false,"tapSensitivity":0.5,"tapRightClick":false,"tapRightClickMode":"rightSide","tapAndDrag":false,"twoFingerDrag":false,"tapZoneEnabled":false,"tapZoneDepth":0.5}}""",
            JsonSerializer.Serialize(Preset.ExtraSteady));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Preset>(
            """{"id":"6E1B49F2-0001-4000-8000-000000000002","name":"A","symbolName":"scope","values":{}}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresetValues>("""{"tapRightClickMode":"left"}"""));
    }

    [Fact]
    public void StringEnumsUseTheirRawValues()
    {
        Assert.Equal("\"magicMouse\"", JsonSerializer.Serialize(MouseKind.MagicMouse));
        Assert.Equal("\"automatic\"", JsonSerializer.Serialize(MouseTypeSetting.Automatic));
        Assert.Equal("\"zoomCommand\"", JsonSerializer.Serialize(ScrollAction.ZoomCommand));
        Assert.Equal(MouseKind.WheelMouse, JsonSerializer.Deserialize<MouseKind>("\"wheelMouse\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MouseKind>("\"WheelMouse\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MouseKind>("1"));
        // An OptionSet on its own is its raw Int.
        Assert.Equal("9", JsonSerializer.Serialize(ModifierCombo.Command | ModifierCombo.Shift));
    }
}
