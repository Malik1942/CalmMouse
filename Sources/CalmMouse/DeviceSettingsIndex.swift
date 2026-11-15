import Foundation
import CalmMouseCore

/// One wheel mouse's own settings.
struct WheelDevice: Codable, Equatable {
    /// The device's name, for a new connection of the same mouse to start from.
    var name: String
    var wheel: WheelSettings
    /// nil until this mouse's tracking speed is chosen; until then it is left alone.
    var trackingSpeed: Double?
    var updatedAt: Date
}

/// "Treat as" for one device.
struct TypeOverride: Codable, Equatable {
    var name: String
    var type: MouseTypeSetting
    var updatedAt: Date
}

/// Each device's own settings, keyed by `DeviceInfo.settingsKey` (IDs plus name).
///
/// A device without settings of its own starts from the device of the same name that was
/// changed last: one mouse reports different IDs over its USB receiver and over Bluetooth, but
/// keeps its name, so switching how it connects keeps what was chosen for it. Two different
/// mice that happen to share a name still get settings of their own once either is changed.
/// A device with no name inherits nothing.
struct DeviceSettingsIndex: Equatable {
    var wheels: [String: WheelDevice] = [:]
    var overrides: [String: TypeOverride] = [:]

    func wheelDevice(key: String, name: String) -> WheelDevice? {
        if let own = wheels[key] { return own }
        guard !name.isEmpty else { return nil }
        return wheels.values.filter { $0.name == name }.max { $0.updatedAt < $1.updatedAt }
    }

    func typeOverride(key: String, name: String) -> MouseTypeSetting? {
        if let own = overrides[key] { return own.type }
        guard !name.isEmpty else { return nil }
        return overrides.values.filter { $0.name == name }.max { $0.updatedAt < $1.updatedAt }?.type
    }
}
