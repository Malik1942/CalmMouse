// Port of Sources/CalmMouseCore/MouseKind.swift.
//
// Names: Swift's `kind(of:event:setting:unknownContinuousIsMagicMouse:)` is `Kind`, and
// `effectiveKind(detected:override:)` is `EffectiveKind`; parameters keep Swift's names
// (`event` is `@event`, a C# keyword). `CaseIterable.allCases` is the static extension
// property `AllCases`. `PointerEventKind` (an enum with an associated value) is an abstract
// record with one nested record per case.

using System.Text.Json.Serialization;

namespace CalmMouse.Core;

/// <summary>
/// The two kinds of mouse CalmMouse manages. They scroll in different ways: a Magic Mouse
/// sends trackpad-style gestures (continuous, phased, with momentum), a wheel mouse sends
/// phase-less ticks. Trackpads are neither, and are never touched.
/// </summary>
[JsonConverter(typeof(RawValueEnumConverter<MouseKind>))]
public enum MouseKind
{
    [RawValue("magicMouse")] MagicMouse,
    [RawValue("wheelMouse")] WheelMouse,
}

/// <summary>
/// Settings → General → "Mouse type". `automatic` works each device out for itself; the other
/// two are the fallback for a mouse that detection gets wrong.
/// </summary>
[JsonConverter(typeof(RawValueEnumConverter<MouseTypeSetting>))]
public enum MouseTypeSetting
{
    [RawValue("automatic")] Automatic,
    [RawValue("magicMouse")] MagicMouse,
    [RawValue("wheelMouse")] WheelMouse,
}

// One static class per enum: two static extension properties called AllCases in one class clash.
public static class MouseKindExtensions
{
    extension(MouseKind)
    {
        public static IReadOnlyList<MouseKind> AllCases => [MouseKind.MagicMouse, MouseKind.WheelMouse];
    }
}

public static class MouseTypeSettingExtensions
{
    extension(MouseTypeSetting)
    {
        public static IReadOnlyList<MouseTypeSetting> AllCases =>
            [MouseTypeSetting.Automatic, MouseTypeSetting.MagicMouse, MouseTypeSetting.WheelMouse];
    }
}

/// <summary>What the IORegistry says about the device behind an event.</summary>
public record struct PointerIdentity
{
    public bool IsMagicMouse { get; set; }
    /// <summary>Built into the Mac, a known or named trackpad, or anything declaring a touch digitizer.</summary>
    public bool IsTrackpad { get; set; }
    /// <summary>Declares itself a mouse (HID Generic Desktop / Mouse).</summary>
    public bool IsMouse { get; set; }

    public PointerIdentity(bool isMagicMouse, bool isTrackpad, bool isMouse)
    {
        IsMagicMouse = isMagicMouse;
        IsTrackpad = isTrackpad;
        IsMouse = isMouse;
    }
}

public abstract record PointerEventKind
{
    private PointerEventKind() { }

    public sealed record Scroll(bool IsContinuous) : PointerEventKind;

    public sealed record Button : PointerEventKind;
}

public static class MouseClassifier
{
    /// <summary>
    /// Which kind of mouse an event should be handled as, or nil to leave it alone.
    /// </summary>
    /// <param name="identity">the sending device, or nil when the sender couldn't be resolved
    /// (synthetic events, or a registry lookup that failed).</param>
    /// <param name="event">the event.</param>
    /// <param name="setting">Settings → General → "Mouse type".</param>
    /// <param name="unknownContinuousIsMagicMouse">the old `treatUnknownContinuousAsMagicMouse`
    /// defaults key, still honoured.</param>
    public static MouseKind? Kind(PointerIdentity? identity, PointerEventKind @event,
                                  MouseTypeSetting setting,
                                  bool unknownContinuousIsMagicMouse = false)
    {
        if (identity is not PointerIdentity id)
        {
            // Nothing to go on but the event itself, so only act when the user has said
            // what they are holding.
            switch (@event)
            {
                case PointerEventKind.Scroll(bool isContinuous):
                    if (isContinuous)
                    {
                        return (setting == MouseTypeSetting.MagicMouse || unknownContinuousIsMagicMouse) ? MouseKind.MagicMouse : null;
                    }
                    return setting == MouseTypeSetting.WheelMouse ? MouseKind.WheelMouse : null;
                case PointerEventKind.Button:
                    if (setting == MouseTypeSetting.WheelMouse) { return MouseKind.WheelMouse; }
                    return (setting == MouseTypeSetting.MagicMouse || unknownContinuousIsMagicMouse) ? MouseKind.MagicMouse : null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@event));
            }
        }

        // A trackpad stays a trackpad whatever the setting says.
        if (id.IsTrackpad && !id.IsMagicMouse) { return null; }

        if (id.IsMagicMouse)
        {
            return setting == MouseTypeSetting.WheelMouse ? null : MouseKind.MagicMouse;
        }

        if (!id.IsMouse) { return null; }
        if (setting == MouseTypeSetting.MagicMouse)
        {
            // Detection took this mouse for something else and the user has corrected it. A
            // Magic Mouse sends gestures and button presses, never wheel ticks, so a real wheel
            // mouse that is also connected keeps scrolling untouched.
            if (@event is PointerEventKind.Scroll { IsContinuous: false }) { return null; }
            return MouseKind.MagicMouse;
        }
        // A wheel turns in ticks. Gesture-style scrolling from a device that isn't a Magic
        // Mouse is some touch surface we don't know, and those are left alone.
        if (@event is PointerEventKind.Scroll { IsContinuous: true }) { return null; }
        return MouseKind.WheelMouse;
    }

    /// <summary>
    /// What a device is handled as once the user has said what it is ("Treat as" on its page).
    /// It matches `Kind(identity, event, setting)`: a real Magic Mouse set to Scroll Wheel is left alone.
    /// </summary>
    public static MouseKind? EffectiveKind(MouseKind? detected, MouseTypeSetting @override) => @override switch
    {
        MouseTypeSetting.Automatic => detected,
        MouseTypeSetting.MagicMouse => MouseKind.MagicMouse,
        MouseTypeSetting.WheelMouse => detected == MouseKind.MagicMouse ? null : MouseKind.WheelMouse,
        _ => throw new ArgumentOutOfRangeException(nameof(@override)),
    };
}
