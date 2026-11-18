import Combine
import Foundation
import CalmMouseCore

struct DetectedDevice: Equatable, Identifiable {
    let kind: DeviceKind
    let name: String
    let battery: Int?
    /// More connected devices of the same kind, shown as "+n".
    let more: Int
    var id: DeviceKind { kind }
}

/// What onboarding's detection page shows: the devices CalmMouse can do something for,
/// checked every second while the page is open so plugging one in shows up at once.
final class DeviceDetector: ObservableObject {
    @Published private(set) var devices: [DetectedDevice] = []
    var kinds: Set<DeviceKind> { Set(devices.map(\.kind)) }

    private let mice: MouseEnvironment
    private let gamepad: GamepadController
    private var timer: Timer?

    init(mice: MouseEnvironment = .shared, gamepad: GamepadController = .shared) {
        self.mice = mice
        self.gamepad = gamepad
    }

    func start() {
        guard timer == nil else { return }
        scan()
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in self?.scan() }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    deinit { timer?.invalidate() }

    private func scan() {
        mice.refresh()
        gamepad.refreshList()
        let found = Self.detected(mice: mice.connected, controllers: gamepad.controllers, batteries: mice.batteries)
        if found != devices { devices = found }
    }

    static func detected(mice: [ConnectedMouse], controllers: [ConnectedController],
                         batteries: [String: Int]) -> [DetectedDevice] {
        func one(_ kind: DeviceKind, _ names: [String], battery: (String) -> Int?) -> DetectedDevice? {
            guard let first = names.first else { return nil }
            return DetectedDevice(kind: kind, name: first, battery: battery(first), more: names.count - 1)
        }
        let magic = mice.filter { $0.kind == .magicMouse }.map(\.name)
        let wheel = mice.filter { $0.kind == .wheelMouse }.map(\.name)
        return [
            one(.magicMouse, magic) { batteries[$0] },
            one(.wheelMouse, wheel) { batteries[$0] },
            one(.controller, controllers.map(\.name)) { name in controllers.first { $0.name == name }?.battery },
        ].compactMap { $0 }
    }
}
