using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalmMouse.Core;

/// <summary>
/// The raw value of a case of a Swift `enum Foo: String`. C# enum members are PascalCase
/// (`MouseKind.MagicMouse`); the raw value keeps Swift's spelling (`"magicMouse"`), which is
/// what Swift's Codable writes to JSON and what `init(rawValue:)` accepts.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RawValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

/// <summary>Swift's `rawValue` and `init(rawValue:)` for enums whose members carry <see cref="RawValueAttribute"/>.</summary>
public static class RawValues
{
    /// <summary>`value.rawValue`.</summary>
    public static string Of<T>(T value) where T : struct, Enum =>
        Table<T>.ToRaw.TryGetValue(value, out string? raw)
            ? raw
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"{typeof(T).Name} has no case with this value.");

    /// <summary>`T(rawValue: raw)`: exact, ordinal match, like Swift's `==` on ASCII strings.</summary>
    public static T? From<T>(string raw) where T : struct, Enum =>
        Table<T>.FromRaw.TryGetValue(raw, out T value) ? value : null;

    private static class Table<T> where T : struct, Enum
    {
        public static readonly Dictionary<T, string> ToRaw = Build();
        public static readonly Dictionary<string, T> FromRaw =
            ToRaw.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        private static Dictionary<T, string> Build()
        {
            var table = new Dictionary<T, string>();
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                RawValueAttribute attribute = field.GetCustomAttribute<RawValueAttribute>()
                    ?? throw new InvalidOperationException($"{typeof(T).Name}.{field.Name} has no [RawValue].");
                table.Add((T)field.GetValue(null)!, attribute.Value);
            }
            return table;
        }
    }
}

/// <summary>
/// Reads and writes an enum the way Swift's Codable does for `enum Foo: String, Codable`: as its
/// raw value string. Anything else (an unknown string, a number, a different case) is an error,
/// as it is in Swift; System.Text.Json's own enum converter would also accept numbers and
/// differently-cased names.
/// </summary>
public sealed class RawValueEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string for {typeof(T).Name}, found {reader.TokenType}.");
        }
        string raw = reader.GetString()!;
        return RawValues.From<T>(raw)
            ?? throw new JsonException($"Cannot initialize {typeof(T).Name} from invalid String value {raw}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(RawValues.Of(value));
}
