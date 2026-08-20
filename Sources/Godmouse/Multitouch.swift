import Foundation
import os

/// Runtime bridge to the private MultitouchSupport framework — the only way to see raw touches
/// on the Magic Mouse surface (the CGEventTap only ever sees the resulting scrolls).
///
/// Loaded with dlopen/dlsym so the project builds with plain `swift build` and keeps working
/// (minus tap-to-click) if a macOS release moves the framework.
/// Contact layout used by every open-source consumer of this framework for 15+ years
/// (mousetoucher, MiddleClick, MagicPrefs...). 96 bytes; verified by a stride assert below.
struct MTTouch {
        var frame: Int32
        var _pad: Int32 = 0
        var timestamp: Double
        var identifier: Int32
        var state: Int32
        var foo3: Int32 = 0
        var foo4: Int32 = 0
        var normX: Float
        var normY: Float
        var velX: Float = 0
        var velY: Float = 0
        var size: Float
        var zero1: Int32 = 0
        var angle: Float = 0
        var majorAxis: Float = 0
        var minorAxis: Float = 0
        var mmX: Float = 0
        var mmY: Float = 0
        var mmVelX: Float = 0
        var mmVelY: Float = 0
        var zero2a: Int32 = 0
        var zero2b: Int32 = 0
        var unk2: Float = 0
}

enum Multitouch {
    /// Touch `state` lifecycle as actually observed on macOS 26 with a Magic Mouse (raw-frame
    /// capture, 2026-08-20): a light tap runs 2,2,2,...,3,4,5 then lingers at 6 and leaves at 7 —
    /// i.e. **2 is the ordinary "finger on surface" state**, 3–5 flash by at lift-off, and a
    /// *sustained* 4 means a firm press. The classic lore (4 = touching) undercounts a tap to a
    /// single frame, so "in contact" is the whole 2...5 band.
    static let touchingStates: ClosedRange<Int32> = 2...5

    typealias DeviceRef = UnsafeMutableRawPointer
    // The touches pointer is passed as a raw pointer and rebound at the use site: Swift refuses
    // custom struct pointers inside @convention(c) signatures in some toolchains.
    typealias FrameCallback = @convention(c) (
        UnsafeMutableRawPointer?, UnsafeMutableRawPointer?, Int32, Double, Int32
    ) -> Int32

    private struct API {
        let createList: @convention(c) () -> Unmanaged<CFMutableArray>?
        let registerCallback: @convention(c) (DeviceRef, FrameCallback) -> Void
        let unregisterCallback: @convention(c) (DeviceRef, FrameCallback) -> Void
        let start: @convention(c) (DeviceRef, Int32) -> Void
        let stop: @convention(c) (DeviceRef) -> Void
        let isBuiltIn: @convention(c) (DeviceRef) -> Bool
        let getFamilyID: @convention(c) (DeviceRef, UnsafeMutablePointer<Int32>) -> Int32
        let getDeviceID: @convention(c) (DeviceRef, UnsafeMutablePointer<UInt64>) -> Int32
    }

    private static let api: API? = {
        assert(MemoryLayout<MTTouch>.stride == 96, "MTTouch layout drifted")
        guard let handle = dlopen(
            "/System/Library/PrivateFrameworks/MultitouchSupport.framework/MultitouchSupport",
            RTLD_LAZY
        ) else {
            Logger(subsystem: "com.godmouse.app", category: "multitouch")
                .error("MultitouchSupport framework not found — tap-to-click unavailable")
            return nil
        }
        func sym<T>(_ name: String, as type: T.Type) -> T? {
            guard let p = dlsym(handle, name) else { return nil }
            return unsafeBitCast(p, to: T.self)
        }
        guard
            let createList = sym("MTDeviceCreateList", as: (@convention(c) () -> Unmanaged<CFMutableArray>?).self),
            let register = sym("MTRegisterContactFrameCallback", as: (@convention(c) (DeviceRef, FrameCallback) -> Void).self),
            let unregister = sym("MTUnregisterContactFrameCallback", as: (@convention(c) (DeviceRef, FrameCallback) -> Void).self),
            let start = sym("MTDeviceStart", as: (@convention(c) (DeviceRef, Int32) -> Void).self),
            let stop = sym("MTDeviceStop", as: (@convention(c) (DeviceRef) -> Void).self),
            let isBuiltIn = sym("MTDeviceIsBuiltIn", as: (@convention(c) (DeviceRef) -> Bool).self),
            let getFamilyID = sym("MTDeviceGetFamilyID", as: (@convention(c) (DeviceRef, UnsafeMutablePointer<Int32>) -> Int32).self),
            let getID = sym("MTDeviceGetDeviceID", as: (@convention(c) (DeviceRef, UnsafeMutablePointer<UInt64>) -> Int32).self)
        else {
            Logger(subsystem: "com.godmouse.app", category: "multitouch")
                .error("MultitouchSupport symbols missing — tap-to-click unavailable")
            return nil
        }
        return API(createList: createList, registerCallback: register, unregisterCallback: unregister,
                   start: start, stop: stop, isBuiltIn: isBuiltIn, getFamilyID: getFamilyID,
                   getDeviceID: getID)
    }()

    static var isAvailable: Bool { api != nil }

    /// Multitouch family IDs seen in the wild: built-in trackpads ≈ 100–111, Magic Mouse = 112
    /// (113 reserved for a future generation), Magic Trackpads = 128+. The classic
    /// `MTDeviceIsOpaqueSurface` discriminator returns false for the Magic Mouse on macOS 26,
    /// so filtering is by family: external + the Magic Mouse band.
    private static let magicMouseFamilies: ClosedRange<Int32> = 112...127

    /// All Magic Mouse multitouch devices currently attached, plus the CFArray that OWNS those
    /// device refs. The caller must keep the array alive for as long as it uses the refs —
    /// dropping it can invalidate them mid-listen (callbacks silently stop).
    static func magicMouseDevices() -> (owner: CFMutableArray?, devices: [DeviceRef]) {
        guard let api else { return (nil, []) }
        guard let list = api.createList()?.takeRetainedValue() else { return (nil, []) }
        var result: [DeviceRef] = []
        for i in 0..<CFArrayGetCount(list) {
            guard let raw = CFArrayGetValueAtIndex(list, i) else { continue }
            let device = DeviceRef(mutating: raw)
            var family: Int32 = -1
            _ = api.getFamilyID(device, &family)
            if !api.isBuiltIn(device) && magicMouseFamilies.contains(family) {
                result.append(device)
            }
        }
        return (list, result)
    }

    static func deviceID(_ device: DeviceRef) -> UInt64 {
        guard let api else { return 0 }
        var id: UInt64 = 0
        _ = api.getDeviceID(device, &id)
        return id
    }

    static func listen(_ device: DeviceRef, _ callback: FrameCallback) {
        guard let api else { return }
        api.registerCallback(device, callback)
        api.start(device, 0)
    }

    static func unlisten(_ device: DeviceRef, _ callback: FrameCallback) {
        guard let api else { return }
        api.stop(device)
        api.unregisterCallback(device, callback)
    }
}
