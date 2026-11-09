import XCTest
import CoreGraphics
import CalmMouseCore
@testable import CalmMouse

/// These build real CGEvents and push them through the same conversion code the tap uses,
/// so the field mapping is verified without needing Accessibility permission.
final class EventBridgeTests: XCTestCase {

    /// CoreGraphics quantises the deltas you pass to the initialiser, so set the fields
    /// explicitly — these are exactly the fields the tap reads and writes.
    private func scrollEvent(dx: Int64 = 0, dy: Int64 = 0) -> CGEvent {
        let e = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 2, wheel1: 0, wheel2: 0, wheel3: 0)!
        e.setIntegerValueField(.scrollWheelEventDeltaAxis1, value: dy)
        e.setIntegerValueField(.scrollWheelEventDeltaAxis2, value: dx)
        e.setIntegerValueField(.scrollWheelEventPointDeltaAxis1, value: dy)
        e.setIntegerValueField(.scrollWheelEventPointDeltaAxis2, value: dx)
        e.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1, value: Double(dy))
        e.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis2, value: Double(dx))
        return e
    }

    // MARK: Phase codes

    func testPhaseCodesRoundTrip() {
        let cases: [(Int64, ScrollPhase)] = [
            (0, .none), (1, .began), (2, .changed), (4, .ended), (8, .cancelled), (128, .mayBegin),
        ]
        for (raw, phase) in cases {
            XCTAssertEqual(EventTap.phase(raw), phase, "raw \(raw)")
            XCTAssertEqual(EventTap.phaseRaw(phase), raw, "phase \(phase)")
        }
        // Anything unexpected must degrade to `.none`, never crash.
        XCTAssertEqual(EventTap.phase(999), .none)
    }

    func testMomentumCodesRoundTrip() {
        let cases: [(Int64, MomentumPhase)] = [(0, .none), (1, .began), (2, .continued), (3, .ended)]
        for (raw, m) in cases {
            XCTAssertEqual(EventTap.momentum(raw), m)
            XCTAssertEqual(EventTap.momentumRaw(m), raw)
        }
        XCTAssertEqual(EventTap.momentum(42), .none)
    }

    // MARK: Modifier conversion

    func testModifierConversionBothWays() {
        XCTAssertEqual(EventTap.combo(from: [.maskCommand, .maskShift]), [.command, .shift])
        XCTAssertEqual(EventTap.combo(from: [.maskAlternate]), [.option])
        XCTAssertEqual(EventTap.combo(from: [.maskControl]), [.control])
        XCTAssertEqual(EventTap.combo(from: []), [])

        XCTAssertTrue(EventTap.flags(from: [.command, .option]).contains(.maskCommand))
        XCTAssertTrue(EventTap.flags(from: [.command, .option]).contains(.maskAlternate))
        XCTAssertFalse(EventTap.flags(from: [.command]).contains(.maskControl))
    }

    // MARK: Rewrites applied to a real CGEvent

    func testZeroingAnAxisClearsAllThreeDeltaRepresentations() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dx: 7, dy: 11)
        tap.apply(ScrollRewrite(phase: .changed, momentum: .none, zeroX: true), to: event)

        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis2), 0)
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventDeltaAxis2), 0)
        XCTAssertEqual(event.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis2), 0)
        // The other axis survives untouched.
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1), 11)
    }

    func testSwapAxesMovesVerticalScrollSideways() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dx: 0, dy: 9)
        tap.apply(ScrollRewrite(phase: .changed, momentum: .none, swapAxes: true), to: event)

        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1), 0)
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis2), 9)
    }

    func testInvertNegatesBothAxes() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dx: 4, dy: -6)
        tap.apply(ScrollRewrite(phase: .changed, momentum: .none, invert: true), to: event)

        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis2), -4)
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1), 6)
    }

    /// Regression: CGEvent's scroll deltas are not independent. Writing the line delta also
    /// rewrites the point delta as line × 8, so the three representations must be written in the
    /// right order or every rewritten scroll comes out eight times too fast.
    func testAllThreeDeltaRepresentationsSurviveARewrite() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dx: 0, dy: 3)
        tap.apply(ScrollRewrite(phase: .changed, momentum: .none, invert: true), to: event)

        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventDeltaAxis1), -3, "line delta")
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1), -3, "point delta")
        XCTAssertEqual(event.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1), -3, "fixed-pt delta")
    }

    func testPhaseAndMomentumAreWrittenBack() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dy: 5)
        tap.apply(ScrollRewrite(phase: .ended, momentum: .none, zeroX: true, zeroY: true), to: event)

        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventScrollPhase), 4)
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventMomentumPhase), 0)
        XCTAssertEqual(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1), 0)
    }

    func testModifierRelabellingForZoom() {
        let tap = EventTap(blocker: ScrollBlocker())
        let event = scrollEvent(dy: 5)
        event.flags = [.maskAlternate]
        tap.apply(ScrollRewrite(phase: .changed, momentum: .none,
                                addModifiers: .command, removeModifiers: .option), to: event)

        XCTAssertTrue(event.flags.contains(.maskCommand))
        XCTAssertFalse(event.flags.contains(.maskAlternate))
    }

    // MARK: Timing

    func testMachTimeConvertsTicksToRealSeconds() {
        var info = mach_timebase_info_data_t()
        mach_timebase_info(&info)
        let oneSecondInTicks = UInt64(1_000_000_000 * Double(info.denom) / Double(info.numer))
        XCTAssertEqual(MachTime.seconds(oneSecondInTicks), 1.0, accuracy: 0.001)
        // The bug this replaced: treating ticks as nanoseconds.
        if info.numer != info.denom {
            XCTAssertNotEqual(MachTime.seconds(oneSecondInTicks), Double(oneSecondInTicks) / 1e9)
        }
    }

    // MARK: Device identification

    func testDeviceIdentifierIgnoresSyntheticEvents() {
        let ident = DeviceIdentifier()
        // A CGEvent we created ourselves has no sender ID, so the device is unknown (nil),
        // never a false "yes this is a Magic Mouse".
        let event = scrollEvent(dy: 3)
        XCTAssertNil(ident.isMagicMouse(event))
        XCTAssertNil(ident.info(forSenderID: 0))
    }

    func testDeviceIdentifierResolvesRealHardwareIfPresent() throws {
        // Best-effort: if this Mac has a Magic Mouse in the IORegistry, we must recognise it.
        guard let reading = BatteryMonitor.read() else {
            throw XCTSkip("no Magic Mouse connected")
        }
        XCTAssertFalse(reading.deviceName.isEmpty)
        XCTAssertGreaterThan(reading.percent, 0)
    }

    private func device(_ product: String, vendor: Int, product productID: Int,
                        transport: String = "Bluetooth") -> DeviceIdentifier.DeviceInfo {
        .init(product: product, vendorID: vendor, productID: productID, transport: transport)
    }

    func testMagicMouseRecognisedByName() {
        // The name macOS generates, whatever IDs came with it (including none at all).
        XCTAssertTrue(device("Malik’s Magic Mouse", vendor: 0x004C, product: 0x0269).isMagicMouse)
        XCTAssertTrue(device("MAGIC MOUSE", vendor: 0, product: 0).isMagicMouse)
    }

    func testRenamedMagicMouseRecognisedByHardwareID() {
        // `Product` is the Bluetooth name and the owner can change it, so every generation
        // must still match on its IDs alone, over Bluetooth (0x004C) and USB (0x05AC).
        for productID in [0x030D, 0x0269, 0x0323] {
            XCTAssertTrue(device("Johnny’s Mouse", vendor: 0x004C, product: productID).isMagicMouse,
                          String(format: "0x%04X over Bluetooth", productID))
            XCTAssertTrue(device("妙控鼠标", vendor: 0x05AC, product: productID, transport: "USB").isMagicMouse,
                          String(format: "0x%04X over USB", productID))
        }
    }

    func testOtherDevicesAreNotMagicMice() {
        // Magic Trackpads (0x0265 Lightning, 0x0324 USB-C) sit right next to the mouse IDs.
        XCTAssertFalse(device("Magic Trackpad", vendor: 0x004C, product: 0x0265).isMagicMouse)
        XCTAssertFalse(device("Magic Trackpad", vendor: 0x004C, product: 0x0324).isMagicMouse)
        XCTAssertFalse(device("Apple Internal Keyboard / Trackpad", vendor: 0, product: 851).isMagicMouse)
        // A mouse product ID only counts under Apple's vendor ID.
        XCTAssertFalse(device("USB Optical Mouse", vendor: 0x046D, product: 0x0269).isMagicMouse)
    }

    func testDeviceSummaryForStatusOutput() {
        XCTAssertEqual(device("Johnny’s Mouse", vendor: 0x004C, product: 0x0323).summary,
                       "Johnny’s Mouse (vendor=0x004C product=0x0323 transport=Bluetooth)")
        XCTAssertEqual(device("X", vendor: 0, product: 0, transport: "").idSummary,
                       "vendor=0x0000 product=0x0000 transport=unknown")
    }

    // MARK: Mouse, wheel mouse or trackpad

    /// The built-in trackpad as the registry reports it on macOS 26 (captured 2026-11-09):
    /// no vendor or product ID, `Built-In`, and a digitizer usage (13/5) next to the mouse one.
    private var builtInTrackpad: DeviceIdentifier.DeviceInfo {
        .init(product: "Apple Internal Keyboard / Trackpad", vendorID: 0, productID: 0, transport: "FIFO",
              builtIn: true, usages: [.init(page: 1, usage: 2), .init(page: 1, usage: 1),
                                      .init(page: 13, usage: 5), .init(page: 65280, usage: 12)])
    }

    /// A third-party Bluetooth wheel mouse, same capture.
    private var wheelMouse: DeviceIdentifier.DeviceInfo {
        .init(product: "MCHOSE A7 Ultra", vendorID: 0x5D, productID: 0x4762, transport: "Bluetooth Low Energy",
              usages: [.init(page: 1, usage: 2), .init(page: 1, usage: 1),
                       .init(page: 1, usage: 6), .init(page: 12, usage: 1)])
    }

    func testCapturedWheelMouseIsAMouseAndNotATrackpad() {
        XCTAssertFalse(wheelMouse.isMagicMouse)
        XCTAssertFalse(wheelMouse.isTrackpad)
        XCTAssertTrue(wheelMouse.declaresMouse)
        XCTAssertEqual(MouseClassifier.kind(of: wheelMouse.identity, event: .scroll(isContinuous: false),
                                            setting: .automatic), .wheelMouse)
    }

    func testCapturedTrackpadIsNeverAMouse() {
        XCTAssertTrue(builtInTrackpad.isTrackpad)
        XCTAssertNil(MouseClassifier.kind(of: builtInTrackpad.identity, event: .button, setting: .wheelMouse))
    }

    func testEachTrackpadSignIsEnoughOnItsOwn() {
        // A trackpad taken for a mouse would have its scrolling interfered with, so any one
        // sign rules it out: built in, named, a known Magic Trackpad, or a touch digitizer.
        XCTAssertTrue(device("Anything", vendor: 0, product: 0).with(builtIn: true).isTrackpad)
        XCTAssertTrue(device("Magic Trackpad", vendor: 0, product: 0).isTrackpad)
        XCTAssertTrue(device("Desk thing", vendor: 0x004C, product: 0x0324).isTrackpad, "renamed USB-C Magic Trackpad")
        XCTAssertTrue(device("Tablet", vendor: 0x056A, product: 1).with(usages: [.init(page: 13, usage: 2)]).isTrackpad)
        XCTAssertFalse(device("Plain Mouse", vendor: 0x046D, product: 1).isTrackpad)
    }

    func testMagicMouseIsNotMistakenForATrackpad() {
        // Whatever else its registry entry says, a Magic Mouse is a Magic Mouse.
        let mouse = device("Malik’s Magic Mouse", vendor: 0x004C, product: 0x0269)
            .with(usages: [.init(page: 13, usage: 5)])
        XCTAssertTrue(mouse.isMagicMouse)
        XCTAssertFalse(mouse.isTrackpad)
    }

    func testDeviceThatDeclaresNoUsagesGetsTheBenefitOfTheDoubt() {
        XCTAssertTrue(device("Old Mouse", vendor: 0x046D, product: 1).declaresMouse)
        XCTAssertFalse(device("Keyboard", vendor: 0x046D, product: 2)
            .with(usages: [.init(page: 1, usage: 6)]).declaresMouse)
    }

    func testUsagePairsAreDecodedFromTheRegistryShape() {
        let raw: [[String: Any]] = [
            ["DeviceUsagePage": 1, "DeviceUsage": 2],
            ["DeviceUsagePage": 13, "DeviceUsage": 5],
            ["Unrelated": 1],
        ]
        XCTAssertEqual(DeviceIdentifier.usages(fromPairs: raw), [.init(page: 1, usage: 2), .init(page: 13, usage: 5)])
        XCTAssertEqual(DeviceIdentifier.usages(fromPairs: nil), [])
        XCTAssertEqual(DeviceIdentifier.usages(fromPairs: "nonsense"), [])
    }

    func testScanFindsOnlyMiceAndNeverTheBuiltInTrackpad() {
        // Runs against whatever is plugged into this Mac; the invariants hold regardless.
        for mouse in MouseScanner.scan() {
            XCTAssertFalse(mouse.info.isTrackpad, mouse.summary)
            XCTAssertFalse(mouse.info.builtIn, mouse.summary)
            if mouse.kind == .magicMouse { XCTAssertTrue(mouse.info.isMagicMouse || mouse.info.vendorID == 0) }
        }
    }

    func testSeenDevicesAreDistinctAndBounded() {
        let ident = DeviceIdentifier()
        let mouse = device("Johnny’s Mouse", vendor: 0x004C, product: 0x0323)
        ident.remember(mouse)
        ident.remember(mouse)
        XCTAssertEqual(ident.seenDevices, [mouse])
        for i in 0..<50 { ident.remember(device("Device \(i)", vendor: 1, product: i)) }
        XCTAssertEqual(ident.seenDevices.count, 12, "a long session must not grow this without bound")
    }
}

private extension DeviceIdentifier.DeviceInfo {
    func with(builtIn: Bool) -> Self { var copy = self; copy.builtIn = builtIn; return copy }
    func with(usages: Set<DeviceIdentifier.Usage>) -> Self { var copy = self; copy.usages = usages; return copy }
}

extension EventBridgeTests {
    /// While a synthetic drag holds the button, hardware mouseMoved events must become genuine
    /// leftMouseDragged events — macOS only does this conversion for physically held buttons,
    /// and without it window dragging renders nothing until release.
    func testMovedToDraggedConversion() throws {
        let moved = try XCTUnwrap(CGEvent(mouseEventSource: nil, mouseType: .mouseMoved,
                                          mouseCursorPosition: CGPoint(x: 100, y: 200),
                                          mouseButton: .left))
        moved.setIntegerValueField(.mouseEventDeltaX, value: 7)
        moved.setIntegerValueField(.mouseEventDeltaY, value: -3)

        EventTap.convertMovedToDragged(moved, clickState: 2)

        XCTAssertEqual(moved.type, .leftMouseDragged)
        XCTAssertEqual(moved.getIntegerValueField(.mouseEventButtonNumber), 0)
        XCTAssertEqual(moved.getIntegerValueField(.mouseEventClickState), 2,
                       "double-click drags must keep their click state while moving")
        XCTAssertEqual(moved.getIntegerValueField(.mouseEventDeltaX), 7)
        XCTAssertEqual(moved.getIntegerValueField(.mouseEventDeltaY), -3)
        XCTAssertEqual(moved.location, CGPoint(x: 100, y: 200))
    }
}
