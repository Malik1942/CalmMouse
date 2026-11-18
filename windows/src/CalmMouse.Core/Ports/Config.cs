// Port of Sources/CalmMouseCore/Config.swift.
//
// Names: OptionSet `ModifierCombo` is a [Flags] enum (`[]` is `None`; `.all` and `.label` are
// extension members `ModifierCombo.All` and `combo.Label`). `action(for:)` is `Action`,
// `rule(forBundleID:)` / `config(forBundleID:)` are `Rule` / `Config`.
//
// Value semantics: the structs are mutable record structs, like Swift's `var` structs. The
// collections inside them (`ModifierActions`, `AppRules`) are read-only and copied on assignment,
// so a copy of a config can never change under you; to change one, assign a new collection.
// Equality compares their contents, as Swift's Equatable does.
//
// JSON: each Codable type has a converter that copies the Swift code. In particular:
// - `ModifierCombo` on its own is its raw Int (a [Flags] enum's default JSON form).
// - `BlockerConfig.modifierActions` is written as {"<rawValue>": "<action>"}, e.g.
//   {"2":"horizontal","9":"block"}: Swift stores it as [Int: ScrollAction], and JSONEncoder
//   writes an Int-keyed dictionary as an object with the Ints as string keys. Keys are written
//   in ordinal string order (what Swift's `.sortedKeys` gives); Swift without `.sortedKeys`
//   writes them in hash order, so compare the JSON as data, not as text.
// - `BlockerConfig` decodes every key with decodeIfPresent (missing or null → the default) and
//   does NOT encode or decode `reverseMagicMouseScroll`: Swift's CodingKeys leave it out, so it
//   isn't saved there either and comes back as false. Kept as is.
// - `AppRule` and `RuleSet` use Swift's synthesized Codable: non-optional keys are required,
//   nil optionals are left out. `id` is a UUID string in upper case.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalmMouse.Core;

// MARK: - Modifiers

/// <summary>Framework-free mirror of the modifier flags we care about.</summary>
[Flags]
public enum ModifierCombo
{
    None = 0,
    Command = 1 << 0,
    Option = 1 << 1,
    Control = 1 << 2,
    Shift = 1 << 3,
}

public static class ModifierComboExtensions
{
    extension(ModifierCombo)
    {
        public static IReadOnlyList<ModifierCombo> All =>
            [ModifierCombo.Command, ModifierCombo.Option, ModifierCombo.Control, ModifierCombo.Shift];
    }

    extension(ModifierCombo combo)
    {
        public string Label
        {
            get
            {
                if (combo == ModifierCombo.None) { return "None"; }
                string s = "";
                if ((combo & ModifierCombo.Control) != 0) { s += "⌃"; }
                if ((combo & ModifierCombo.Option) != 0) { s += "⌥"; }
                if ((combo & ModifierCombo.Shift) != 0) { s += "⇧"; }
                if ((combo & ModifierCombo.Command) != 0) { s += "⌘"; }
                return s;
            }
        }
    }
}

/// <summary>What a modifier + scroll combination should do.</summary>
[JsonConverter(typeof(RawValueEnumConverter<ScrollAction>))]
public enum ScrollAction
{
    /// <summary>Leave the event alone (macOS default behaviour for that modifier).</summary>
    [RawValue("normal")] Normal,
    /// <summary>Turn vertical scrolling into horizontal scrolling.</summary>
    [RawValue("horizontal")] Horizontal,
    /// <summary>Flip the scroll direction.</summary>
    [RawValue("invert")] Invert,
    /// <summary>Re-label the event as ⌘-scroll — what most creative apps (Figma, Sketch, Preview) zoom with.</summary>
    [RawValue("zoomCommand")] ZoomCommand,
    /// <summary>Re-label the event as ⌃-scroll — macOS system zoom (Accessibility → Zoom).</summary>
    [RawValue("zoomControl")] ZoomControl,
    /// <summary>Swallow the scroll entirely while the modifier is held.</summary>
    [RawValue("block")] Block,
}

public static class ScrollActionExtensions
{
    extension(ScrollAction)
    {
        public static IReadOnlyList<ScrollAction> AllCases =>
        [
            ScrollAction.Normal, ScrollAction.Horizontal, ScrollAction.Invert,
            ScrollAction.ZoomCommand, ScrollAction.ZoomControl, ScrollAction.Block,
        ];
    }

    extension(ScrollAction action)
    {
        public string Label => action switch
        {
            ScrollAction.Normal => "Scroll normally",
            ScrollAction.Horizontal => "Scroll sideways",
            ScrollAction.Invert => "Scroll in reverse",
            ScrollAction.ZoomCommand => "Zoom in apps",
            ScrollAction.ZoomControl => "Zoom the whole screen",
            ScrollAction.Block => "Do nothing",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }
}

// MARK: - Base config

/// <summary>
/// Use `new BlockerConfig()` for the defaults; `default(BlockerConfig)` is all zeros.
/// </summary>
[JsonConverter(typeof(BlockerConfigJsonConverter))]
public record struct BlockerConfig
{
    /// <summary>The headline feature: swallow Magic Mouse scrolling while any of its buttons is held.</summary>
    public bool BlockScrollWhileClicked { get; set; } = true;
    /// <summary>
    /// Keep blocking for this long after the last button is released — the finger lifting
    /// off the shell almost always produces a little trailing scroll. Seconds.
    /// </summary>
    public double ReleaseGrace { get; set; } = 0.2;

    /// <summary>
    /// Minimum travel (in points) a gesture must accumulate before it's allowed through.
    /// Kills the micro-jitter you get from a finger resting on the shell. 0 = off.
    /// </summary>
    public double DeadZone { get; set; } = 0;

    /// <summary>Once a scroll gesture clearly commits to one axis, zero out the other one.</summary>
    public bool AxisLock { get; set; } = false;
    public double AxisLockThreshold { get; set; } = 10;
    public double AxisLockRatio { get; set; } = 1.4;

    /// <summary>Drop the coasting tail after a Magic Mouse flick (macOS's own setting is system-wide).</summary>
    public bool MomentumEnabled { get; set; } = true;

    /// <summary>Ignore sideways scrolling from the Magic Mouse completely.</summary>
    public bool BlockHorizontalScroll { get; set; } = false;

    /// <summary>Ignore Magic Mouse scrolling altogether (mostly useful as a per-app rule).</summary>
    public bool DisableScrollEntirely { get; set; } = false;

    private ImmutableDictionary<ModifierCombo, ScrollAction>? _modifierActions;

    /// <summary>
    /// modifier combination → what scrolling should do while it's held. Copied on assignment.
    /// </summary>
    public IReadOnlyDictionary<ModifierCombo, ScrollAction> ModifierActions
    {
        readonly get => _modifierActions ?? ImmutableDictionary<ModifierCombo, ScrollAction>.Empty;
        set => _modifierActions = ImmutableDictionary.CreateRange(value);
    }

    /// <summary>
    /// Flip the Magic Mouse's scrolling on its own. macOS has one "natural scrolling" switch
    /// for the trackpad and every mouse; this lets the Magic Mouse go the other way.
    /// </summary>
    public bool ReverseMagicMouseScroll { get; set; } = false;

    // Wheel mice. Everything above is for the Magic Mouse; a wheel mouse answers only to
    // these, and both start off so that supporting it changes nothing until asked.

    /// <summary>
    /// Flip the wheel's direction on its own. macOS has a single "natural scrolling" switch
    /// shared by every device, so whoever likes it on the trackpad gets it on the wheel too.
    /// </summary>
    public bool ReverseWheelScroll { get; set; } = false;
    /// <summary>Swallow wheel ticks while the wheel itself is pressed down as a button.</summary>
    public bool BlockScrollWhileWheelPressed { get; set; } = false;
    /// <summary>Glide between wheel notches instead of jumping (see `WheelSmoother`).</summary>
    public bool SmoothWheelScroll { get; set; } = false;
    /// <summary>How far a notch travels when smoothing, as a multiple of the base step.</summary>
    public double WheelScrollSpeed { get; set; } = 3;

    /// <summary>The four settings above, for a wheel mouse that has no settings of its own.</summary>
    public readonly WheelSettings WheelTemplate =>
        new(reverse: ReverseWheelScroll, blockWhilePressed: BlockScrollWhileWheelPressed,
            smooth: SmoothWheelScroll, speed: WheelScrollSpeed);

    public BlockerConfig() { }

    public readonly ScrollAction Action(ModifierCombo modifiers) =>
        ModifierActions.TryGetValue(modifiers, out ScrollAction action) ? action : ScrollAction.Normal;

    // Swift's synthesized Equatable: every stored property, the dictionary by content, Doubles with
    // `==` (so NaN is never equal to itself, as in Swift).
    public readonly bool Equals(BlockerConfig other) =>
        BlockScrollWhileClicked == other.BlockScrollWhileClicked
        && ReleaseGrace == other.ReleaseGrace
        && DeadZone == other.DeadZone
        && AxisLock == other.AxisLock
        && AxisLockThreshold == other.AxisLockThreshold
        && AxisLockRatio == other.AxisLockRatio
        && MomentumEnabled == other.MomentumEnabled
        && BlockHorizontalScroll == other.BlockHorizontalScroll
        && DisableScrollEntirely == other.DisableScrollEntirely
        && SameContents(ModifierActions, other.ModifierActions)
        && ReverseMagicMouseScroll == other.ReverseMagicMouseScroll
        && ReverseWheelScroll == other.ReverseWheelScroll
        && BlockScrollWhileWheelPressed == other.BlockScrollWhileWheelPressed
        && SmoothWheelScroll == other.SmoothWheelScroll
        && WheelScrollSpeed == other.WheelScrollSpeed;

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BlockScrollWhileClicked);
        hash.Add(ReleaseGrace);
        hash.Add(DeadZone);
        hash.Add(AxisLock);
        hash.Add(AxisLockThreshold);
        hash.Add(AxisLockRatio);
        hash.Add(MomentumEnabled);
        hash.Add(BlockHorizontalScroll);
        hash.Add(DisableScrollEntirely);
        int actions = 0;
        foreach (KeyValuePair<ModifierCombo, ScrollAction> pair in ModifierActions)
        {
            actions ^= HashCode.Combine(pair.Key, pair.Value); // order-independent
        }
        hash.Add(actions);
        hash.Add(ReverseMagicMouseScroll);
        hash.Add(ReverseWheelScroll);
        hash.Add(BlockScrollWhileWheelPressed);
        hash.Add(SmoothWheelScroll);
        hash.Add(WheelScrollSpeed);
        return hash.ToHashCode();
    }

    private static bool SameContents(IReadOnlyDictionary<ModifierCombo, ScrollAction> a,
                                     IReadOnlyDictionary<ModifierCombo, ScrollAction> b) =>
        a.Count == b.Count
        && a.All(pair => b.TryGetValue(pair.Key, out ScrollAction value) && value == pair.Value);
}

// Dictionary keyed by an OptionSet isn't Codable out of the box; store as [rawValue: action].
internal sealed class BlockerConfigJsonConverter : JsonConverter<BlockerConfig>
{
    public override BlockerConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(BlockerConfig));
        var s = new BlockerConfig();
        s.BlockScrollWhileClicked = SwiftJson.DecodeIfPresent<bool>(c, "blockScrollWhileClicked", options) ?? s.BlockScrollWhileClicked;
        s.ReleaseGrace = SwiftJson.DecodeIfPresent<double>(c, "releaseGrace", options) ?? s.ReleaseGrace;
        s.DeadZone = SwiftJson.DecodeIfPresent<double>(c, "deadZone", options) ?? s.DeadZone;
        s.AxisLock = SwiftJson.DecodeIfPresent<bool>(c, "axisLock", options) ?? s.AxisLock;
        s.AxisLockThreshold = SwiftJson.DecodeIfPresent<double>(c, "axisLockThreshold", options) ?? s.AxisLockThreshold;
        s.AxisLockRatio = SwiftJson.DecodeIfPresent<double>(c, "axisLockRatio", options) ?? s.AxisLockRatio;
        s.MomentumEnabled = SwiftJson.DecodeIfPresent<bool>(c, "momentumEnabled", options) ?? s.MomentumEnabled;
        s.BlockHorizontalScroll = SwiftJson.DecodeIfPresent<bool>(c, "blockHorizontalScroll", options) ?? s.BlockHorizontalScroll;
        s.DisableScrollEntirely = SwiftJson.DecodeIfPresent<bool>(c, "disableScrollEntirely", options) ?? s.DisableScrollEntirely;
        s.ModifierActions = SwiftJson.DecodeIfPresent(c, "modifierActions", options, out JsonElement raw)
            ? DecodeModifierActions(raw, options)
            : ImmutableDictionary<ModifierCombo, ScrollAction>.Empty;
        s.ReverseWheelScroll = SwiftJson.DecodeIfPresent<bool>(c, "reverseWheelScroll", options) ?? s.ReverseWheelScroll;
        s.BlockScrollWhileWheelPressed = SwiftJson.DecodeIfPresent<bool>(c, "blockScrollWhileWheelPressed", options) ?? s.BlockScrollWhileWheelPressed;
        s.SmoothWheelScroll = SwiftJson.DecodeIfPresent<bool>(c, "smoothWheelScroll", options) ?? s.SmoothWheelScroll;
        s.WheelScrollSpeed = SwiftJson.DecodeIfPresent<double>(c, "wheelScrollSpeed", options) ?? s.WheelScrollSpeed;
        return s;
    }

    /// <summary>Swift's `[Int: ScrollAction]`: an object whose keys must parse as Int.</summary>
    private static Dictionary<ModifierCombo, ScrollAction> DecodeModifierActions(JsonElement raw, JsonSerializerOptions options)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected to decode modifierActions from a JSON object, found {raw.ValueKind}.");
        }
        var actions = new Dictionary<ModifierCombo, ScrollAction>();
        foreach (JsonProperty property in raw.EnumerateObject())
        {
            if (!SwiftJson.TryParseIntKey(property.Name, out int key))
            {
                throw new JsonException($"Expected Int key but found String key \"{property.Name}\" instead.");
            }
            actions[(ModifierCombo)key] = SwiftJson.Decode<ScrollAction>(raw, property.Name, options);
        }
        return actions;
    }

    public override void Write(Utf8JsonWriter writer, BlockerConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("blockScrollWhileClicked", value.BlockScrollWhileClicked);
        writer.WriteNumber("releaseGrace", value.ReleaseGrace);
        writer.WriteNumber("deadZone", value.DeadZone);
        writer.WriteBoolean("axisLock", value.AxisLock);
        writer.WriteNumber("axisLockThreshold", value.AxisLockThreshold);
        writer.WriteNumber("axisLockRatio", value.AxisLockRatio);
        writer.WriteBoolean("momentumEnabled", value.MomentumEnabled);
        writer.WriteBoolean("blockHorizontalScroll", value.BlockHorizontalScroll);
        writer.WriteBoolean("disableScrollEntirely", value.DisableScrollEntirely);
        writer.WriteStartObject("modifierActions");
        foreach ((string key, ScrollAction action) in value.ModifierActions
                     .Select(pair => (SwiftJson.IntKey((int)pair.Key), pair.Value))
                     .OrderBy(pair => pair.Item1, StringComparer.Ordinal))
        {
            writer.WriteString(key, RawValues.Of(action));
        }
        writer.WriteEndObject();
        writer.WriteBoolean("reverseWheelScroll", value.ReverseWheelScroll);
        writer.WriteBoolean("blockScrollWhileWheelPressed", value.BlockScrollWhileWheelPressed);
        writer.WriteBoolean("smoothWheelScroll", value.SmoothWheelScroll);
        writer.WriteNumber("wheelScrollSpeed", value.WheelScrollSpeed);
        writer.WriteEndObject();
    }
}

// MARK: - Per-app rules

/// <summary>
/// Overrides that apply only while a given app is frontmost. `null` means "inherit".
/// On Windows `BundleID` holds the app's exe name.
/// </summary>
[JsonConverter(typeof(AppRuleJsonConverter))]
public record struct AppRule
{
    public Guid Id { get; set; }
    public string BundleID { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }

    public bool? BlockScrollWhileClicked { get; set; }
    public bool? DisableScrollEntirely { get; set; }
    public bool? BlockHorizontalScroll { get; set; }
    public bool? MomentumEnabled { get; set; }
    public bool? AxisLock { get; set; }
    public double? DeadZone { get; set; }

    /// <summary>`id` defaults to a new UUID, as in Swift (a C# default can't call Guid.NewGuid()).</summary>
    public AppRule(string bundleID, string name, Guid? id = null, bool enabled = true,
                   bool? blockScrollWhileClicked = null, bool? disableScrollEntirely = null,
                   bool? blockHorizontalScroll = null, bool? momentumEnabled = null,
                   bool? axisLock = null, double? deadZone = null)
    {
        Id = id ?? Guid.NewGuid();
        BundleID = bundleID;
        Name = name;
        Enabled = enabled;
        BlockScrollWhileClicked = blockScrollWhileClicked;
        DisableScrollEntirely = disableScrollEntirely;
        BlockHorizontalScroll = blockHorizontalScroll;
        MomentumEnabled = momentumEnabled;
        AxisLock = axisLock;
        DeadZone = deadZone;
    }

    /// <summary>True when the rule doesn't actually override anything.</summary>
    public readonly bool IsEmpty =>
        BlockScrollWhileClicked == null && DisableScrollEntirely == null && BlockHorizontalScroll == null
        && MomentumEnabled == null && AxisLock == null && DeadZone == null;

    public readonly BlockerConfig Apply(BlockerConfig @base)
    {
        if (!Enabled) { return @base; }
        BlockerConfig c = @base;
        if (BlockScrollWhileClicked is bool v1) { c.BlockScrollWhileClicked = v1; }
        if (DisableScrollEntirely is bool v2) { c.DisableScrollEntirely = v2; }
        if (BlockHorizontalScroll is bool v3) { c.BlockHorizontalScroll = v3; }
        if (MomentumEnabled is bool v4) { c.MomentumEnabled = v4; }
        if (AxisLock is bool v5) { c.AxisLock = v5; }
        if (DeadZone is double v6) { c.DeadZone = v6; }
        return c;
    }
}

internal sealed class AppRuleJsonConverter : JsonConverter<AppRule>
{
    public override AppRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(AppRule));
        return new AppRule(
            id: SwiftJson.Decode<Guid>(c, "id", options),
            bundleID: SwiftJson.Decode<string>(c, "bundleID", options),
            name: SwiftJson.Decode<string>(c, "name", options),
            enabled: SwiftJson.Decode<bool>(c, "enabled", options),
            blockScrollWhileClicked: SwiftJson.DecodeIfPresent<bool>(c, "blockScrollWhileClicked", options),
            disableScrollEntirely: SwiftJson.DecodeIfPresent<bool>(c, "disableScrollEntirely", options),
            blockHorizontalScroll: SwiftJson.DecodeIfPresent<bool>(c, "blockHorizontalScroll", options),
            momentumEnabled: SwiftJson.DecodeIfPresent<bool>(c, "momentumEnabled", options),
            axisLock: SwiftJson.DecodeIfPresent<bool>(c, "axisLock", options),
            deadZone: SwiftJson.DecodeIfPresent<double>(c, "deadZone", options));
    }

    public override void Write(Utf8JsonWriter writer, AppRule value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        SwiftJson.WriteUuid(writer, "id", value.Id);
        writer.WriteString("bundleID", value.BundleID);
        writer.WriteString("name", value.Name);
        writer.WriteBoolean("enabled", value.Enabled);
        if (value.BlockScrollWhileClicked is bool v1) { writer.WriteBoolean("blockScrollWhileClicked", v1); }
        if (value.DisableScrollEntirely is bool v2) { writer.WriteBoolean("disableScrollEntirely", v2); }
        if (value.BlockHorizontalScroll is bool v3) { writer.WriteBoolean("blockHorizontalScroll", v3); }
        if (value.MomentumEnabled is bool v4) { writer.WriteBoolean("momentumEnabled", v4); }
        if (value.AxisLock is bool v5) { writer.WriteBoolean("axisLock", v5); }
        if (value.DeadZone is double v6) { writer.WriteNumber("deadZone", v6); }
        writer.WriteEndObject();
    }
}

/// <summary>
/// Base settings plus per-app overrides; resolves the config for whichever app is frontmost.
/// Use `new RuleSet()` for the defaults; `default(RuleSet)` has an all-zeros `Base`.
/// </summary>
[JsonConverter(typeof(RuleSetJsonConverter))]
public record struct RuleSet
{
    public BlockerConfig Base { get; set; }

    private ImmutableArray<AppRule> _appRules;

    /// <summary>Copied on assignment.</summary>
    public IReadOnlyList<AppRule> AppRules
    {
        readonly get => _appRules.IsDefault ? [] : _appRules;
        set => _appRules = [.. value];
    }

    public RuleSet() : this(null, null) { }

    /// <summary>`base` defaults to `new BlockerConfig()`, `appRules` to none.</summary>
    public RuleSet(BlockerConfig? @base = null, IReadOnlyList<AppRule>? appRules = null)
    {
        Base = @base ?? new BlockerConfig();
        AppRules = appRules ?? [];
    }

    /// <summary>
    /// The first enabled rule for `bundleID`. Matching ignores case, like Swift's
    /// `caseInsensitiveCompare` (here: ordinal, ignoring case).
    /// </summary>
    public readonly AppRule? Rule(string? bundleID)
    {
        if (bundleID is null) { return null; }
        foreach (AppRule rule in AppRules)
        {
            if (rule.Enabled && string.Equals(rule.BundleID, bundleID, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }
        return null;
    }

    public readonly BlockerConfig Config(string? bundleID) =>
        Rule(bundleID)?.Apply(Base) ?? Base;

    public readonly bool Equals(RuleSet other) =>
        Base.Equals(other.Base) && AppRules.SequenceEqual(other.AppRules);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Base);
        foreach (AppRule rule in AppRules) { hash.Add(rule); }
        return hash.ToHashCode();
    }
}

internal sealed class RuleSetJsonConverter : JsonConverter<RuleSet>
{
    public override RuleSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement c = SwiftJson.ReadObject(ref reader, nameof(RuleSet));
        return new RuleSet(
            @base: SwiftJson.Decode<BlockerConfig>(c, "base", options),
            appRules: SwiftJson.Decode<List<AppRule>>(c, "appRules", options));
    }

    public override void Write(Utf8JsonWriter writer, RuleSet value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("base");
        JsonSerializer.Serialize(writer, value.Base, options);
        writer.WritePropertyName("appRules");
        writer.WriteStartArray();
        foreach (AppRule rule in value.AppRules)
        {
            JsonSerializer.Serialize(writer, rule, options);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
