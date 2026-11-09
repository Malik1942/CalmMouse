import Foundation

/// The two kinds of mouse CalmMouse manages. They scroll in different ways: a Magic Mouse
/// sends trackpad-style gestures (continuous, phased, with momentum), a wheel mouse sends
/// phase-less ticks. Trackpads are neither, and are never touched.
public enum MouseKind: String, Codable, CaseIterable, Sendable {
    case magicMouse
    case wheelMouse
}

/// Settings → General → "Mouse type". `automatic` works each device out for itself; the other
/// two are the fallback for a mouse that detection gets wrong.
public enum MouseTypeSetting: String, Codable, CaseIterable, Sendable {
    case automatic
    case magicMouse
    case wheelMouse
}

/// What the IORegistry says about the device behind an event.
public struct PointerIdentity: Equatable, Sendable {
    public var isMagicMouse: Bool
    /// Built into the Mac, a known or named trackpad, or anything declaring a touch digitizer.
    public var isTrackpad: Bool
    /// Declares itself a mouse (HID Generic Desktop / Mouse).
    public var isMouse: Bool

    public init(isMagicMouse: Bool, isTrackpad: Bool, isMouse: Bool) {
        self.isMagicMouse = isMagicMouse
        self.isTrackpad = isTrackpad
        self.isMouse = isMouse
    }
}

public enum PointerEventKind: Equatable, Sendable {
    case scroll(isContinuous: Bool)
    case button
}

public enum MouseClassifier {
    /// Which kind of mouse an event should be handled as, or nil to leave it alone.
    ///
    /// - Parameters:
    ///   - identity: the sending device, or nil when the sender couldn't be resolved
    ///     (synthetic events, or a registry lookup that failed).
    ///   - unknownContinuousIsMagicMouse: the old `treatUnknownContinuousAsMagicMouse`
    ///     defaults key, still honoured.
    public static func kind(of identity: PointerIdentity?, event: PointerEventKind,
                            setting: MouseTypeSetting,
                            unknownContinuousIsMagicMouse: Bool = false) -> MouseKind? {
        guard let identity else {
            // Nothing to go on but the event itself, so only act when the user has said
            // what they are holding.
            switch event {
            case .scroll(let isContinuous):
                if isContinuous {
                    return (setting == .magicMouse || unknownContinuousIsMagicMouse) ? .magicMouse : nil
                }
                return setting == .wheelMouse ? .wheelMouse : nil
            case .button:
                if setting == .wheelMouse { return .wheelMouse }
                return (setting == .magicMouse || unknownContinuousIsMagicMouse) ? .magicMouse : nil
            }
        }

        // A trackpad stays a trackpad whatever the setting says.
        if identity.isTrackpad && !identity.isMagicMouse { return nil }

        if identity.isMagicMouse {
            return setting == .wheelMouse ? nil : .magicMouse
        }

        guard identity.isMouse else { return nil }
        if setting == .magicMouse {
            // Detection took this mouse for something else and the user has corrected it. A
            // Magic Mouse sends gestures and button presses, never wheel ticks, so a real wheel
            // mouse that is also connected keeps scrolling untouched.
            if case .scroll(isContinuous: false) = event { return nil }
            return .magicMouse
        }
        // A wheel turns in ticks. Gesture-style scrolling from a device that isn't a Magic
        // Mouse is some touch surface we don't know, and those are left alone.
        if case .scroll(isContinuous: true) = event { return nil }
        return .wheelMouse
    }

    /// Which kinds the settings window should show options for.
    ///
    /// The Magic Mouse is the default: with nothing connected, its settings are what shows, and
    /// once one has been seen they stay, because it drops off Bluetooth whenever it sleeps. A
    /// wheel mouse's settings show only while one is connected. A forced type shows only itself.
    public static func kindsToShow(setting: MouseTypeSetting, connected: Set<MouseKind>,
                                   remembered: Set<MouseKind>) -> Set<MouseKind> {
        switch setting {
        case .magicMouse: return [.magicMouse]
        case .wheelMouse: return [.wheelMouse]
        case .automatic:
            var kinds = connected
            if connected.isEmpty || remembered.contains(.magicMouse) { kinds.insert(.magicMouse) }
            return kinds
        }
    }
}
