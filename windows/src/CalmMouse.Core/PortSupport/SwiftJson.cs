using System.Globalization;
using System.Text.Json;

namespace CalmMouse.Core;

/// <summary>
/// The pieces of Swift's JSONDecoder / JSONEncoder that the hand-written converters in Ports/
/// need, so each converter reads like the Swift `init(from:)` / `encode(to:)` it copies.
///
/// Decoding: <see cref="Decode{T}"/> is Swift's `decode(_:forKey:)` (a missing key or a null
/// is an error); <see cref="DecodeIfPresent{T}"/> is `decodeIfPresent(_:forKey:)` (a missing
/// key or a null gives nil). Unknown keys are ignored, as in Swift. Keys are case-sensitive.
///
/// Encoding: properties are written in the order the Swift code encodes them. A nil optional
/// is left out (Swift's synthesized `encodeIfPresent`).
/// </summary>
internal static class SwiftJson
{
    /// <summary>Reads the current JSON value, which must be an object (Swift's keyed container).</summary>
    public static JsonElement ReadObject(ref Utf8JsonReader reader, string typeName)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected to decode {typeName} from a JSON object, found {document.RootElement.ValueKind}.");
        }
        return document.RootElement.Clone();
    }

    /// <summary>`decode(T.self, forKey: key)`.</summary>
    public static T Decode<T>(JsonElement container, string key, JsonSerializerOptions options)
    {
        if (!container.TryGetProperty(key, out JsonElement value))
        {
            throw new JsonException($"No value associated with key \"{key}\".");
        }
        if (value.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Expected {typeof(T).Name} value for key \"{key}\" but found null instead.");
        }
        return Value<T>(value, key, options);
    }

    /// <summary>`decodeIfPresent(T.self, forKey: key)`: nil (here: false) for a missing key or a null.</summary>
    public static bool DecodeIfPresent<T>(JsonElement container, string key, JsonSerializerOptions options, out T result)
    {
        if (!container.TryGetProperty(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            result = default!;
            return false;
        }
        result = Value<T>(value, key, options);
        return true;
    }

    /// <summary>`decodeIfPresent` for a value-type optional (`Bool?`, `Double?`).</summary>
    public static T? DecodeIfPresent<T>(JsonElement container, string key, JsonSerializerOptions options) where T : struct =>
        DecodeIfPresent(container, key, options, out T result) ? result : null;

    private static T Value<T>(JsonElement value, string key, JsonSerializerOptions options)
    {
        object? result = typeof(T) switch
        {
            Type t when t == typeof(bool) => value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Mismatch<T>(key, value),
            },
            Type t when t == typeof(double) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d)
                ? d
                : throw Mismatch<T>(key, value),
            // Swift's Int is 64 bits; every Int these types store fits in 32.
            Type t when t == typeof(int) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)
                ? i
                : throw Mismatch<T>(key, value),
            Type t when t == typeof(string) => value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw Mismatch<T>(key, value),
            // Foundation's UUID: only the 36-character hyphenated form, any case.
            Type t when t == typeof(Guid) => value.ValueKind == JsonValueKind.String
                    && Guid.TryParseExact(value.GetString(), "D", out Guid g)
                ? g
                : throw new JsonException($"Attempted to decode UUID from invalid UUID string for key \"{key}\"."),
            _ => value.Deserialize<T>(options),
        };
        return (T)result!;
    }

    private static JsonException Mismatch<T>(string key, JsonElement value) =>
        new($"Expected to decode {typeof(T).Name} for key \"{key}\" but found {value.ValueKind} instead.");

    /// <summary>Foundation writes `UUID` as `uuidString`: hyphenated, upper case.</summary>
    public static void WriteUuid(Utf8JsonWriter writer, string key, Guid value) =>
        writer.WriteString(key, value.ToString("D").ToUpperInvariant());

    /// <summary>Swift encodes a `[Int: V]` dictionary as a JSON object whose keys are the Ints as decimal strings.</summary>
    public static string IntKey(int key) => key.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Swift's `Int(String)` for a dictionary key: optional leading + or -, then ASCII digits only.
    /// </summary>
    public static bool TryParseIntKey(string key, out int value) =>
        int.TryParse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
