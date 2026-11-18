// Port of Sources/CalmMouseCore/Presets.swift.
//
// Names: the built-ins are `Preset.JustFixClicking`, `ExtraSteady`, `TrackpadFeel` and `BuiltIn`.
// `Preset`'s initializer takes `name` and `values` first, because C# optional parameters must
// come last; call it with named arguments, as Swift does.
//
// JSON:
// - `PresetValues` decodes every key with decodeIfPresent (missing or null → the factory value)
//   and, like Swift's synthesized `encode(to:)`, writes all fifteen keys in CodingKeys order.
// - `Preset` uses Swift's synthesized Codable: id, name, summary, symbolName and values are all
//   required. `id` is a UUID string in upper case.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalmMouse.Core;

// MARK: - Preset values

/// <summary>
/// The knobs a preset sets: everything on the Scrolling and Clicking tabs.
/// Per-app rules, modifier shortcuts and battery settings are deliberately not part
/// of a preset — those are refinements a user layers on top, and switching the
/// overall "feel" of the mouse shouldn't wipe them.
///
/// The zero-argument initializer is the app's factory configuration, so
/// `new PresetValues()` doubles as "what a fresh install feels like".
/// (`default(PresetValues)` is all zeros, not the factory configuration.)
/// </summary>
[JsonConverter(typeof(PresetValuesJsonConverter))]
public record struct PresetValues
{
    public bool BlockScrollWhileClicked { get; set; } = true;
    public int ReleaseGraceMs { get; set; } = 200;
    public double DeadZone { get; set; } = 0;
    public bool AxisLock { get; set; } = false;
    public double AxisLockThreshold { get; set; } = 10;
    public bool MomentumEnabled { get; set; } = true;
    public bool BlockHorizontalScroll { get; set; } = false;
    public bool TapToClick { get; set; } = false;
    public double TapSensitivity { get; set; } = 0.5;
    public bool TapRightClick { get; set; } = false;
    public RightClickMode TapRightClickMode { get; set; } = RightClickMode.RightSide;
    public bool TapAndDrag { get; set; } = false;
    public bool TwoFingerDrag { get; set; } = false;
    public bool TapZoneEnabled { get; set; } = false;
    public double TapZoneDepth { get; set; } = 0.5;

    public PresetValues() { }
}

// Missing keys fall back to the factory value, so presets saved by an older
// version keep decoding after new settings are added.
internal sealed class PresetValuesJsonConverter : JsonConverter<PresetValues>
{
    public override PresetValues Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(PresetValues));
        var v = new PresetValues();
        v.BlockScrollWhileClicked = SwiftJson.DecodeIfPresent<bool>(c, "blockScrollWhileClicked", options) ?? v.BlockScrollWhileClicked;
        v.ReleaseGraceMs = SwiftJson.DecodeIfPresent<int>(c, "releaseGraceMs", options) ?? v.ReleaseGraceMs;
        v.DeadZone = SwiftJson.DecodeIfPresent<double>(c, "deadZone", options) ?? v.DeadZone;
        v.AxisLock = SwiftJson.DecodeIfPresent<bool>(c, "axisLock", options) ?? v.AxisLock;
        v.AxisLockThreshold = SwiftJson.DecodeIfPresent<double>(c, "axisLockThreshold", options) ?? v.AxisLockThreshold;
        v.MomentumEnabled = SwiftJson.DecodeIfPresent<bool>(c, "momentumEnabled", options) ?? v.MomentumEnabled;
        v.BlockHorizontalScroll = SwiftJson.DecodeIfPresent<bool>(c, "blockHorizontalScroll", options) ?? v.BlockHorizontalScroll;
        v.TapToClick = SwiftJson.DecodeIfPresent<bool>(c, "tapToClick", options) ?? v.TapToClick;
        v.TapSensitivity = SwiftJson.DecodeIfPresent<double>(c, "tapSensitivity", options) ?? v.TapSensitivity;
        v.TapRightClick = SwiftJson.DecodeIfPresent<bool>(c, "tapRightClick", options) ?? v.TapRightClick;
        v.TapRightClickMode = SwiftJson.DecodeIfPresent<RightClickMode>(c, "tapRightClickMode", options) ?? v.TapRightClickMode;
        v.TapAndDrag = SwiftJson.DecodeIfPresent<bool>(c, "tapAndDrag", options) ?? v.TapAndDrag;
        v.TwoFingerDrag = SwiftJson.DecodeIfPresent<bool>(c, "twoFingerDrag", options) ?? v.TwoFingerDrag;
        v.TapZoneEnabled = SwiftJson.DecodeIfPresent<bool>(c, "tapZoneEnabled", options) ?? v.TapZoneEnabled;
        v.TapZoneDepth = SwiftJson.DecodeIfPresent<double>(c, "tapZoneDepth", options) ?? v.TapZoneDepth;
        return v;
    }

    public override void Write(Utf8JsonWriter writer, PresetValues value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("blockScrollWhileClicked", value.BlockScrollWhileClicked);
        writer.WriteNumber("releaseGraceMs", value.ReleaseGraceMs);
        writer.WriteNumber("deadZone", value.DeadZone);
        writer.WriteBoolean("axisLock", value.AxisLock);
        writer.WriteNumber("axisLockThreshold", value.AxisLockThreshold);
        writer.WriteBoolean("momentumEnabled", value.MomentumEnabled);
        writer.WriteBoolean("blockHorizontalScroll", value.BlockHorizontalScroll);
        writer.WriteBoolean("tapToClick", value.TapToClick);
        writer.WriteNumber("tapSensitivity", value.TapSensitivity);
        writer.WriteBoolean("tapRightClick", value.TapRightClick);
        writer.WriteString("tapRightClickMode", RawValues.Of(value.TapRightClickMode));
        writer.WriteBoolean("tapAndDrag", value.TapAndDrag);
        writer.WriteBoolean("twoFingerDrag", value.TwoFingerDrag);
        writer.WriteBoolean("tapZoneEnabled", value.TapZoneEnabled);
        writer.WriteNumber("tapZoneDepth", value.TapZoneDepth);
        writer.WriteEndObject();
    }
}

// MARK: - Preset

/// <summary>
/// A named, one-click starting point. Applying one sets the Scrolling and Clicking
/// options; the user is free to tweak everything afterwards.
/// </summary>
[JsonConverter(typeof(PresetJsonConverter))]
public record struct Preset
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
    /// <summary>SF Symbol shown on the preset's card.</summary>
    public string SymbolName { get; set; }
    public PresetValues Values { get; set; }

    /// <summary>`id` defaults to a new UUID, as in Swift (a C# default can't call Guid.NewGuid()).</summary>
    public Preset(string name, PresetValues values, Guid? id = null, string summary = "",
                  string symbolName = "slider.horizontal.3")
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Summary = summary;
        SymbolName = symbolName;
        Values = values;
    }

    // MARK: - Built-in presets

    /// <summary>The headline fix and nothing else — factory configuration.</summary>
    public static Preset JustFixClicking { get; } = new(
        id: Guid.Parse("6E1B49F2-0001-4000-8000-000000000001"),
        name: "Just fix clicking",
        summary: "Pages hold still while you click.\nNothing else changes.",
        symbolName: "cursorarrow.click.2",
        values: new PresetValues());

    /// <summary>For people who hate every kind of accidental scrolling.</summary>
    public static Preset ExtraSteady { get; } = MakeExtraSteady();

    private static Preset MakeExtraSteady()
    {
        var v = new PresetValues();
        v.ReleaseGraceMs = 300;
        v.DeadZone = 8;
        v.AxisLock = true;
        v.MomentumEnabled = false;
        return new Preset(
            id: Guid.Parse("6E1B49F2-0001-4000-8000-000000000002"),
            name: "Extra steady",
            summary: "Ignores nudges and diagonal drift.\nThe page stops when your finger does.",
            symbolName: "scope",
            values: v);
    }

    /// <summary>Taps, right-taps and tap-drags, like a trackpad grafted onto the mouse.</summary>
    public static Preset TrackpadFeel { get; } = MakeTrackpadFeel();

    private static Preset MakeTrackpadFeel()
    {
        var v = new PresetValues();
        v.TapToClick = true;
        v.TapRightClick = true;
        v.TapAndDrag = true;
        return new Preset(
            id: Guid.Parse("6E1B49F2-0001-4000-8000-000000000003"),
            name: "Trackpad feel",
            summary: "Tap to click and right-click.\nDouble Tap Drag to move things.",
            symbolName: "hand.tap",
            values: v);
    }

    // Declared after the three above: static initializers run in textual order.
    public static IReadOnlyList<Preset> BuiltIn { get; } = [JustFixClicking, ExtraSteady, TrackpadFeel];
}

internal sealed class PresetJsonConverter : JsonConverter<Preset>
{
    public override Preset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(Preset));
        return new Preset(
            id: SwiftJson.Decode<Guid>(c, "id", options),
            name: SwiftJson.Decode<string>(c, "name", options),
            summary: SwiftJson.Decode<string>(c, "summary", options),
            symbolName: SwiftJson.Decode<string>(c, "symbolName", options),
            values: SwiftJson.Decode<PresetValues>(c, "values", options));
    }

    public override void Write(Utf8JsonWriter writer, Preset value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        SwiftJson.WriteUuid(writer, "id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("summary", value.Summary);
        writer.WriteString("symbolName", value.SymbolName);
        writer.WritePropertyName("values");
        JsonSerializer.Serialize(writer, value.Values, options);
        writer.WriteEndObject();
    }
}
