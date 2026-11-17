import XCTest
import CalmMouseCore
@testable import CalmMouse

/// The website's settings window plays the app's stories on the same mouse, ported to JavaScript
/// in website/src/settings. Its tests compare against website/test/fixtures/app.json; this test
/// fails when the app no longer computes what that file says, so the two can't drift apart.
/// After changing a story, the mouse's shape, the stage or a preset, rewrite the file with
///     CALMMOUSE_WRITE_WEBSITE_FIXTURES=1 swift test --filter WebsiteFixtureTests
/// then run `npm test` in website/ and port the change.
final class WebsiteFixtureTests: XCTestCase {
    private static let file = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("website/test/fixtures/app.json")

    func testTheWebsiteFixtureIsWhatTheAppComputes() throws {
        let fresh = Self.fixture()
        if ProcessInfo.processInfo.environment["CALMMOUSE_WRITE_WEBSITE_FIXTURES"] == "1" {
            try JSONSerialization.data(withJSONObject: fresh, options: [.sortedKeys]).write(to: Self.file)
            return
        }
        let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: Self.file))
        // Through JSON and back, so both sides hold the same kinds of values.
        let current = try JSONSerialization.jsonObject(with: JSONSerialization.data(withJSONObject: fresh))
        XCTAssertTrue(Self.matches(saved, current),
                      "website/test/fixtures/app.json is out of date. Rewrite it with CALMMOUSE_WRITE_WEBSITE_FIXTURES=1, then run npm test in website/.")
    }

    // Computed, not stored: a static stored [String: Any] isn't concurrency-safe in Swift 6.
    static var previews: [(json: [String: Any], preview: SettingPreview)] { [
        (["kind": "blockWhileClick"], .blockWhileClick),
        (["kind": "settleAfterRelease"], .settleAfterRelease),
        (["kind": "ignoreNudges"], .ignoreNudges),
        (["kind": "straightLines"], .straightLines),
        (["kind": "momentum", "on": true], .momentum(on: true)),
        (["kind": "momentum", "on": false], .momentum(on: false)),
        (["kind": "ignoreSideways"], .ignoreSideways),
        (["kind": "tapToClick"], .tapToClick),
        (["kind": "tapRightClick", "doubleTap": false], .tapRightClick(doubleTap: false)),
        (["kind": "tapRightClick", "doubleTap": true], .tapRightClick(doubleTap: true)),
        (["kind": "tapZone", "depth": 0.5], .tapZone(depth: 0.5)),
        (["kind": "tapZone", "depth": 0.3], .tapZone(depth: 0.3)),
        (["kind": "tapAndDrag"], .tapAndDrag),
        (["kind": "twoFingerDrag"], .twoFingerDrag),
        (["kind": "keyZoom"], .keyZoom),
        (["kind": "reverseScroll", "on": true], .reverseScroll(on: true)),
        (["kind": "reverseScroll", "on": false], .reverseScroll(on: false)),
    ] }

    static func fixture() -> [String: Any] {
        func camera(_ yaw: Double) -> DeskCamera {
            DeskCamera(yaw: yaw, frame: Double(MouseStage.frameSide),
                       originX: Double(MouseStage.frameOrigin.x), originY: Double(MouseStage.frameOrigin.y))
        }
        let grid: [(Double, Double)] = [(0, 0), (0, 0.15), (0.6, -0.4), (-0.5, -0.4), (0.9, 0.9),
                                        (-0.98, 0.2), (0.3, -0.98), (0.19, -0.5), (-0.71, 0.6)]
        var surface: [[String: Any]] = [], projected: [[String: Any]] = [], discs: [[String: Any]] = []
        for (u, v) in grid {
            let p = MagicMouseFigure.surface(u: u, v: v), n = MagicMouseFigure.surfaceNormal(u: u, v: v)
            surface.append(["u": u, "v": v, "x": p.x, "y": p.y, "z": p.z, "nx": n.x, "ny": n.y, "nz": n.z])
            for yaw in [0.0, 5, 95, 180, 355] {
                for slide in [0.0, 0.8] {
                    let q = camera(yaw).project(p, slide: slide)
                    projected.append(["yaw": yaw, "slide": slide, "u": u, "v": v, "x": q.x, "y": q.y])
                }
            }
            let ring = camera(0).disc(u: u, v: v, radius: 0.5, slide: 0.3, steps: 28)
            discs.append(["u": u, "v": v, "radius": 0.5, "slide": 0.3, "steps": 28, "points": ring.map { [$0.x, $0.y] }])
        }
        var stories: [[String: Any]] = []
        for (json, preview) in previews {
            var frames: [[String: Any]] = [], elapsed: [[String: Any]] = []
            for i in 0...(Int(preview.loop / 0.11) + 1) {
                let t = Double(i) * 0.11
                frames.append(["t": t, "frame": encode(preview.frame(at: t))])
            }
            for e in [0.5, preview.loop + 0.7, 2 * preview.loop + 1.3] {
                elapsed.append(["elapsed": e, "frame": encode(preview.frame(elapsed: e))])
            }
            stories.append(["preview": json, "loop": preview.loop, "frames": frames, "elapsed": elapsed])
        }
        var fade: [[String: Any]] = []
        for i in 0...14 {
            let age = -0.05 + Double(i) * 0.025
            fade.append(["age": age, "in": StoryFade.fadeIn(age: age), "out": StoryFade.fadeOut(age: age)])
        }
        let underlay = [(1.0, 0.0), (0.6, 0.5), (0.3, 0.9), (0.8, 1.0), (0.0, 0.4)].map { o, i -> [String: Any] in
            ["outgoing": o, "incoming": i, "value": StoryFade.underlay(outgoing: o, incoming: i)]
        }
        var ring: [[String: Any]] = []
        for i in 0...31 {
            let t = -0.05 + Double(i) * 0.1
            guard let f = PresenceRing.frame(at: t) else { ring.append(["t": t, "frame": NSNull(), "ticks": []]); continue }
            let ticks = [0.0, 0.7, 2.1, 4.4].map { a -> [String: Any] in
                let k = PresenceRing.tick(f, angle: a)
                return ["angle": a, "length": k.length, "bloom": k.bloom, "mix": k.mix]
            }
            ring.append(["t": t, "frame": ["sweep": f.sweep, "radius": f.radius, "alpha": f.alpha, "phase": f.phase, "bloom": f.bloom],
                         "ticks": ticks])
        }
        let presets = Preset.builtIn.map { p -> [String: Any] in
            let v = p.values
            return ["name": p.name, "summary": p.summary, "values": [
                "blockScrollWhileClicked": v.blockScrollWhileClicked, "releaseGraceMs": v.releaseGraceMs,
                "deadZone": v.deadZone, "axisLock": v.axisLock, "axisLockThreshold": v.axisLockThreshold,
                "momentumEnabled": v.momentumEnabled, "blockHorizontalScroll": v.blockHorizontalScroll,
                "tapToClick": v.tapToClick, "tapSensitivity": v.tapSensitivity, "tapRightClick": v.tapRightClick,
                "tapRightClickMode": v.tapRightClickMode.rawValue, "tapAndDrag": v.tapAndDrag,
                "twoFingerDrag": v.twoFingerDrag, "tapZoneEnabled": v.tapZoneEnabled, "tapZoneDepth": v.tapZoneDepth]]
        }
        return [
            "stage": ["width": Double(MouseStage.size.width), "height": Double(MouseStage.size.height),
                      "frameSide": Double(MouseStage.frameSide), "frameOriginX": Double(MouseStage.frameOrigin.x),
                      "frameOriginY": Double(MouseStage.frameOrigin.y), "cardWidth": Double(MouseStage.cardSize.width),
                      "cardHeight": Double(MouseStage.cardSize.height), "cardTop": Double(MouseStage.cardTop)],
            "surface": surface, "projected": projected, "discs": discs, "stories": stories,
            "fade": fade, "underlay": underlay, "ring": ring, "presets": presets,
        ]
    }

    static func encode(_ f: StoryFrame) -> [String: Any] {
        var zone: Any = NSNull()
        switch f.zone {
        case .front(let depth)?: zone = ["kind": "front", "depth": depth]
        case .rightSide(let split)?: zone = ["kind": "rightSide", "split": split]
        case nil: break
        }
        var arrows: Any = NSNull()
        if let a = f.page.arrows {
            switch a.kind {
            case .straight: arrows = ["kind": "straight", "drift": a.drift, "page": a.page]
            case .diagonal: arrows = ["kind": "diagonal", "drift": a.drift, "page": a.page]
            case .flipped(let on): arrows = ["kind": "flipped", "on": on, "drift": a.drift, "page": a.page]
            }
        }
        return [
            "touches": f.touches.map { ["u": $0.u, "v": $0.v, "alpha": $0.alpha, "pressing": $0.pressing, "ignored": $0.ignored] },
            "ripples": f.ripples.map { ["u": $0.u, "v": $0.v, "progress": $0.progress] },
            "zone": zone,
            "dial": f.dial.map { ["u": $0.u, "v": $0.v, "progress": $0.progress] as Any } ?? NSNull(),
            "crossOnLid": f.crossOnLid.map { ["u": $0.u, "v": $0.v, "alpha": $0.alpha] as Any } ?? NSNull(),
            "pressed": f.pressed, "slide": f.slide,
            "page": ["scrollY": f.page.scrollY, "scrollX": f.page.scrollX, "zoom": f.page.zoom,
                     "button": f.page.button, "flash": f.page.flash, "object": f.page.object.map { $0 as Any } ?? NSNull(),
                     "menu": f.page.menu, "cross": f.page.cross, "arrows": arrows,
                     "key": f.page.key.map { $0 as Any } ?? NSNull(), "fade": f.page.fade],
        ]
    }

    static func matches(_ a: Any, _ b: Any) -> Bool {
        switch (a, b) {
        case let (x as [String: Any], y as [String: Any]):
            return x.keys.sorted() == y.keys.sorted() && x.allSatisfy { matches($0.value, y[$0.key]!) }
        case let (x as [Any], y as [Any]):
            return x.count == y.count && zip(x, y).allSatisfy { matches($0, $1) }
        case let (x as String, y as String):
            return x == y
        case let (x as NSNumber, y as NSNumber):
            return abs(x.doubleValue - y.doubleValue) <= 1e-9 * max(1, abs(x.doubleValue))
        case (is NSNull, is NSNull):
            return true
        default:
            return false
        }
    }
}
