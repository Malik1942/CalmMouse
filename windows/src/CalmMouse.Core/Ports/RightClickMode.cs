// Declared in Sources/CalmMouseCore/TapRecognizer.swift. Kept in its own file because Presets
// needed it first; the rest of that Swift file is Ports/TapRecognizer.cs.

using System.Text.Json.Serialization;

namespace CalmMouse.Core;

/// <summary>How a tap becomes a RIGHT click when tap-right-click is on.</summary>
[JsonConverter(typeof(RawValueEnumConverter<RightClickMode>))]
public enum RightClickMode
{
    /// <summary>
    /// Taps landing on the right part of the surface right-click — the same place a
    /// physical Magic Mouse right click lives, and the default for exactly that reason.
    /// </summary>
    [RawValue("rightSide")] RightSide,
    /// <summary>
    /// A second quick tap in the same spot right-clicks. Chosen explicitly: it takes over
    /// the tap-tap gesture, so double-clicks need a physical press (or slower taps).
    /// </summary>
    [RawValue("doubleTap")] DoubleTap,
}

public static class RightClickModeExtensions
{
    extension(RightClickMode)
    {
        public static IReadOnlyList<RightClickMode> AllCases => [RightClickMode.RightSide, RightClickMode.DoubleTap];
    }
}
