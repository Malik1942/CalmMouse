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
        XCTAssertTrue(reading.deviceName.lowercased().contains("magic mouse"))
        XCTAssertGreaterThan(reading.percent, 0)
    }
}
