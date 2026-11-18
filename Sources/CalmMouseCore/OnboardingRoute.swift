import Foundation

/// What onboarding can find connected. A trackpad is never one of them.
public enum DeviceKind: String, CaseIterable, Sendable {
    case magicMouse, wheelMouse, controller
}

public enum OnboardingStep: String, CaseIterable, Sendable {
    case devices, detect, permission, preset, tryIt, wheel, controller, done
}

/// The onboarding steps for what is connected: the same opening, then a stretch per kind of
/// device in a fixed order, then done. Worked out once, when the person leaves the detection
/// page, so plugging something in later never changes how many steps are left.
public enum OnboardingRoute {
    public static func steps(for kinds: Set<DeviceKind>) -> [OnboardingStep] {
        var steps: [OnboardingStep] = [.devices, .detect, .permission]
        if kinds.contains(.magicMouse) { steps += [.preset, .tryIt] }
        if kinds.contains(.wheelMouse) { steps.append(.wheel) }
        if kinds.contains(.controller) { steps.append(.controller) }
        return steps + [.done]
    }
}
