import XCTest
@testable import CalmMouseCore

/// Built-in presets and the custom-preset persistence format.
final class PresetTests: XCTestCase {

    // MARK: Built-ins

    func testThreeBuiltInPresetsWithDistinctIdentities() {
        XCTAssertEqual(Preset.builtIn.count, 3)
        XCTAssertEqual(Set(Preset.builtIn.map(\.id)).count, 3)
        XCTAssertEqual(Set(Preset.builtIn.map(\.name)).count, 3)
        for preset in Preset.builtIn {
            XCTAssertFalse(preset.summary.isEmpty, "\(preset.name) needs a summary for its card")
        }
    }

    /// "Just fix clicking" *is* the factory configuration — the README and the
    /// onboarding flow both promise that.
    func testJustFixClickingMatchesFactoryDefaults() {
        XCTAssertEqual(Preset.justFixClicking.values, PresetValues())
    }

    func testBuiltInsAreDistinctFromEachOther() {
        XCTAssertNotEqual(Preset.extraSteady.values, PresetValues())
        XCTAssertNotEqual(Preset.trackpadFeel.values, PresetValues())
        XCTAssertNotEqual(Preset.extraSteady.values, Preset.trackpadFeel.values)
    }

    func testExtraSteadyCalmsScrollingWithoutTouchingTaps() {
        let v = Preset.extraSteady.values
        XCTAssertTrue(v.blockScrollWhileClicked)
        XCTAssertGreaterThan(v.deadZone, 0)
        XCTAssertTrue(v.axisLock)
        XCTAssertFalse(v.momentumEnabled)
        XCTAssertFalse(v.tapToClick, "scroll preset shouldn't surprise-enable tapping")
    }

    func testTrackpadFeelEnablesTapsWithoutChangingScrollFeel() {
        let v = Preset.trackpadFeel.values
        XCTAssertTrue(v.tapToClick)
        XCTAssertTrue(v.tapAndDrag)
        var scrollOnly = v
        scrollOnly.tapToClick = false
        scrollOnly.tapAndDrag = false
        XCTAssertEqual(scrollOnly, PresetValues(), "everything except taps stays factory")
    }

    // MARK: Persistence

    func testPresetRoundTripsThroughJSON() throws {
        var values = PresetValues()
        values.deadZone = 12
        values.tapToClick = true
        values.tapZoneDepth = 0.65
        let preset = Preset(name: "Mine", summary: "notes", symbolName: "star", values: values)
        let decoded = try JSONDecoder().decode(Preset.self, from: JSONEncoder().encode(preset))
        XCTAssertEqual(decoded, preset)
    }

    /// A preset saved before a setting existed must keep decoding, with the new
    /// knob at its factory value.
    func testDecodingToleratesMissingKeys() throws {
        let old = #"{"deadZone": 7, "momentumEnabled": false}"#.data(using: .utf8)!
        let values = try JSONDecoder().decode(PresetValues.self, from: old)
        XCTAssertEqual(values.deadZone, 7)
        XCTAssertFalse(values.momentumEnabled)
        XCTAssertEqual(values.releaseGraceMs, 200)
        XCTAssertTrue(values.blockScrollWhileClicked)
    }
}
