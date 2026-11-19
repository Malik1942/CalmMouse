// What CalmMouse for Windows remembers, and how it is written to settings.json.
//
// The file (System.Text.Json, camelCase):
//
//   {
//     "version": 1,
//     "paused": false,
//     "launchAtLogin": false,
//     "language": "system",                    // system | english | simplifiedChinese | traditionalChinese
//     "mice": {
//       "3554:F58A:2.4G Mouse": {              // the engine's stable device key (MouseDeviceInfo.StableKey)
//         "displayName": "2.4G Mouse",
//         "wheel": { "reverse": false, "blockWhilePressed": false, "smooth": false, "speed": 3 },
//         "updatedAt": "2026-11-18T09:30:00+08:00"
//       }
//     },
//     "appRules": [
//       { "exeName": "figma.exe", "displayName": "Figma", "enabled": true, "noSmoothing": false, "leaveAlone": false }
//     ]
//   }
//
// Reading is forgiving: a field that is missing or of the wrong kind gets its default, a mouse or
// app rule that can't be read is skipped, and only a file that isn't JSON at all counts as corrupt.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CalmMouse.Core;
using CalmMouse.Core.Routing;

namespace CalmMouse.Win.Settings;

/// <summary>The language CalmMouse shows. <see cref="System"/> follows the Windows display language.</summary>
public enum AppLanguage
{
    System,
    English,
    SimplifiedChinese,
    TraditionalChinese,
}

/// <summary>One wheel mouse's own settings, as the Mac's <c>WheelDevice</c>.</summary>
public sealed record MouseSettings
{
    /// <summary>The mouse's name when it was last changed, so a new connection of the same mouse can start from it.</summary>
    public string DisplayName { get; init; } = "";
    public WheelSettings Wheel { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    /// <summary>Paused from the tray: every wheel event passes untouched.</summary>
    public bool Paused { get; init; }
    public bool LaunchAtLogin { get; init; }
    public AppLanguage Language { get; init; } = AppLanguage.System;
    /// <summary>Each wheel mouse's settings, keyed by the engine's stable device key.</summary>
    public IReadOnlyDictionary<string, MouseSettings> Mice { get; init; } = new Dictionary<string, MouseSettings>(StringComparer.Ordinal);
    public IReadOnlyList<WheelAppRule> AppRules { get; init; } = [];

    /// <summary>
    /// The settings a mouse uses, as the Mac's <c>DeviceSettingsIndex.wheelDevice(key:name:)</c>:
    /// its own if it has any; else those of the mouse with the same name that was changed last (one
    /// mouse can show up with different IDs over its USB receiver and over Bluetooth, but keeps
    /// its name); a mouse with no name inherits nothing.
    /// </summary>
    public MouseSettings? MouseFor(string key, string name)
    {
        if (Mice.TryGetValue(key, out MouseSettings? own)) { return own; }
        if (string.IsNullOrEmpty(name)) { return null; }
        return Mice.Values.Where(m => m.DisplayName == name).MaxBy(m => m.UpdatedAt);
    }
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON.</summary>
public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string Write(AppSettings settings)
    {
        var root = new JsonObject
        {
            ["version"] = AppSettings.CurrentVersion,
            ["paused"] = settings.Paused,
            ["launchAtLogin"] = settings.LaunchAtLogin,
            ["language"] = JsonSerializer.SerializeToNode(settings.Language, Options),
            ["mice"] = JsonSerializer.SerializeToNode(
                settings.Mice.OrderBy(m => m.Key, StringComparer.Ordinal).ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal), Options),
            ["appRules"] = JsonSerializer.SerializeToNode(settings.AppRules, Options),
        };
        return root.ToJsonString(Options);
    }

    /// <summary>
    /// The settings in <paramref name="json"/>, or null if it isn't a JSON object at all (corrupt).
    /// Anything inside that can't be read is replaced by its default.
    /// </summary>
    public static AppSettings? Read(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (root == null) { return null; }

        var defaults = new AppSettings();
        var mice = new Dictionary<string, MouseSettings>(StringComparer.Ordinal);
        if (root["mice"] is JsonObject miceNode)
        {
            foreach ((string key, JsonNode? node) in miceNode)
            {
                if (TryRead(node, out MouseSettings? mouse) && mouse != null) { mice[key] = mouse; }
            }
        }
        var rules = new List<WheelAppRule>();
        if (root["appRules"] is JsonArray rulesNode)
        {
            foreach (JsonNode? node in rulesNode)
            {
                if (TryRead(node, out WheelAppRule? rule) && rule != null && !string.IsNullOrWhiteSpace(rule.ExeName))
                {
                    rules.Add(rule with { DisplayName = rule.DisplayName ?? rule.ExeName });
                }
            }
        }
        return new AppSettings
        {
            Version = Value(root, "version", defaults.Version),
            Paused = Value(root, "paused", defaults.Paused),
            LaunchAtLogin = Value(root, "launchAtLogin", defaults.LaunchAtLogin),
            Language = Value(root, "language", defaults.Language),
            Mice = mice,
            AppRules = rules,
        };
    }

    private static T Value<T>(JsonObject root, string name, T fallback) =>
        TryRead(root[name], out T? value) && value is not null ? value : fallback;

    private static bool TryRead<T>(JsonNode? node, out T? value)
    {
        value = default;
        if (node == null) { return false; }
        try
        {
            value = node.Deserialize<T>(Options);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
