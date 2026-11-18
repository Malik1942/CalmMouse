// Port of Sources/CalmMouseCore/WheelSettings.swift.
//
// JSON: Swift's synthesized Codable, so all four keys are required when decoding and always
// written, in declaration order: {"reverse":false,"blockWhilePressed":false,"smooth":false,"speed":3}.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalmMouse.Core;

/// <summary>
/// What one wheel mouse asks for. Each wheel mouse keeps its own, so a mouse at home and one
/// at the office can scroll differently; a mouse CalmMouse hasn't been told about uses the
/// template in `BlockerConfig`.
/// </summary>
[JsonConverter(typeof(WheelSettingsJsonConverter))]
public record struct WheelSettings
{
    /// <summary>Flip the wheel's direction on its own (macOS has one natural-scrolling switch for all).</summary>
    public bool Reverse { get; set; } = false;
    /// <summary>Swallow wheel ticks while the wheel itself is pressed down as a button.</summary>
    public bool BlockWhilePressed { get; set; } = false;
    /// <summary>Glide between notches instead of jumping (see `WheelSmoother`).</summary>
    public bool Smooth { get; set; } = false;
    /// <summary>How far a notch travels when gliding, as a multiple of the base step.</summary>
    public double Speed { get; set; } = 3;

    /// <summary>The factory settings. (Without it `new WheelSettings()` would be all zeros, speed included.)</summary>
    public WheelSettings() { }

    public WheelSettings(bool reverse = false, bool blockWhilePressed = false, bool smooth = false,
                         double speed = 3)
    {
        Reverse = reverse;
        BlockWhilePressed = blockWhilePressed;
        Smooth = smooth;
        Speed = speed;
    }
}

internal sealed class WheelSettingsJsonConverter : JsonConverter<WheelSettings>
{
    public override WheelSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(WheelSettings));
        return new WheelSettings(
            reverse: SwiftJson.Decode<bool>(c, "reverse", options),
            blockWhilePressed: SwiftJson.Decode<bool>(c, "blockWhilePressed", options),
            smooth: SwiftJson.Decode<bool>(c, "smooth", options),
            speed: SwiftJson.Decode<double>(c, "speed", options));
    }

    public override void Write(Utf8JsonWriter writer, WheelSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("reverse", value.Reverse);
        writer.WriteBoolean("blockWhilePressed", value.BlockWhilePressed);
        writer.WriteBoolean("smooth", value.Smooth);
        writer.WriteNumber("speed", value.Speed);
        writer.WriteEndObject();
    }
}
