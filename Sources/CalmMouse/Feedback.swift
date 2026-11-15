import Foundation
import CalmMouseCore

/// "Send Feedback…": opens a new GitHub issue on the bug form, with the details a report needs
/// already filled in.
///
/// The issue is public, so mice are listed by kind and hardware ID, never by name: a Magic
/// Mouse's name is whatever its owner called it in Bluetooth settings, usually their own name.
/// The hardware ID is what a fix needs anyway (it is how a USB-C Magic Mouse gets recognised).
enum Feedback {
    static let newIssue = "https://github.com/Malik1942/CalmMouse/issues/new"
    /// Where the Star button is.
    static let repository = URL(string: "https://github.com/Malik1942/CalmMouse")!

    struct Details {
        var version: String
        var build: String
        var macOS: String
        var model: String
        var accessibilityTrusted: Bool
        var enabled: Bool
        var tapRunning: Bool
        var mouseType: MouseTypeSetting
        var tapToClick: Bool
        var mice: [ConnectedMouse]
    }

    /// Plain English whatever the app's language: it is read by whoever fixes the bug.
    static func diagnostics(_ d: Details) -> String {
        func onOff(_ value: Bool) -> String { value ? "on" : "off" }
        var lines = [
            "CalmMouse \(d.version) (\(d.build))",
            "\(d.macOS), \(d.model)",
            "Accessibility: \(d.accessibilityTrusted ? "granted" : "not granted")"
                + " · CalmMouse: \(onOff(d.enabled)) · Event tap: \(d.tapRunning ? "running" : "stopped")",
            "Mouse type: \(d.mouseType.rawValue) · Tap to click: \(onOff(d.tapToClick))",
        ]
        if d.mice.isEmpty {
            lines.append("Mice: none connected")
        } else {
            lines.append("Mice:")
            for mouse in d.mice {
                let kind: String
                switch mouse.kind {
                case .magicMouse: kind = "Magic Mouse"
                case .wheelMouse: kind = "Wheel mouse"
                case nil: kind = "Not managed"
                }
                let id = String(format: "0x%04X:0x%04X", mouse.info.vendorID, mouse.info.productID)
                let transport = mouse.info.transport.isEmpty ? "unknown connection" : mouse.info.transport
                lines.append("- \(kind) · \(id) · \(transport)")
            }
        }
        return lines.joined(separator: "\n")
    }

    /// The bug form (`.github/ISSUE_TEMPLATE/bug_report.yml`), prefilled through its field ids.
    static func issueURL(_ d: Details) -> URL {
        var components = URLComponents(string: newIssue)!
        components.queryItems = [
            URLQueryItem(name: "template", value: "bug_report.yml"),
            URLQueryItem(name: "version", value: d.version),
            URLQueryItem(name: "macos", value: d.macOS),
            URLQueryItem(name: "diagnostics", value: diagnostics(d)),
        ]
        return components.url!
    }

    /// Settings asks the app delegate, which knows the tap's state, to send feedback.
    static let sendRequested = Notification.Name("CalmMouseSendFeedback")

    /// "macOS 26.1 (25B78)".
    static var macOSVersion: String {
        let v = ProcessInfo.processInfo.operatingSystemVersion
        let number = v.patchVersion == 0 ? "\(v.majorVersion).\(v.minorVersion)"
                                         : "\(v.majorVersion).\(v.minorVersion).\(v.patchVersion)"
        let build = sysctlString("kern.osversion")
        return build.isEmpty ? "macOS \(number)" : "macOS \(number) (\(build))"
    }

    /// The Mac's model identifier, such as "Mac15,3". Not tied to the person.
    static var macModel: String {
        let model = sysctlString("hw.model")
        return model.isEmpty ? "unknown Mac" : model
    }

    private static func sysctlString(_ name: String) -> String {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return "" }
        var buffer = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return "" }
        return String(cString: buffer)
    }
}
