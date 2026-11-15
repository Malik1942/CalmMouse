import XCTest
@testable import CalmMouseCore

/// The Magic Mouse drawn in Settings: its shape and the camera of the Blender renders. Touches are
/// drawn on top of those renders, so the projection has to land where Blender put the pixels.
final class MouseFigureTests: XCTestCase {

    /// Points Blender itself projected for the 720 px desk render (camera.py → camera-desk.json).
    private let blender: [(name: String, local: Vec3, x: Double, y: Double)] = [
        ("apex", Vec3(0, 0, 2.16), 360.0, 300.4563),
        ("front", Vec3(0, 5.675, 0.58), 243.0049, 205.0799),
        ("back", Vec3(0, -5.675, 0.58), 503.2089, 565.1846),
        ("right", Vec3(2.855, 0, 1.26), 497.2286, 298.1482),
        ("left", Vec3(-2.855, 0, 1.26), 216.0511, 381.4127),
    ]

    func testDeskCameraLandsOnBlendersPixels() {
        let camera = DeskCamera(frame: 720)
        for point in blender {
            let p = camera.project(point.local)
            XCTAssertEqual(p.x, point.x, accuracy: 0.01, point.name)
            XCTAssertEqual(p.y, point.y, accuracy: 0.01, point.name)
        }
    }

    func testFrameSizeAndOriginScaleAndShiftTheSamePicture() {
        let full = DeskCamera(frame: 720).project(blender[1].local)
        let small = DeskCamera(frame: 360, originX: 10, originY: -20).project(blender[1].local)
        XCTAssertEqual(small.x, 10 + full.x / 2, accuracy: 1e-9)
        XCTAssertEqual(small.y, -20 + full.y / 2, accuracy: 1e-9)
    }

    func testTurningTheMouseSpinsItAboutItsMiddle() {
        let home = DeskCamera(frame: 720), turned = DeskCamera(yaw: 180, frame: 720)
        let apex = turned.project(blender[0].local)
        XCTAssertEqual(apex.x, 360.0, accuracy: 0.01)
        XCTAssertEqual(apex.y, 300.4563, accuracy: 0.01)
        // Half a turn puts the front where the back was.
        let front = turned.project(blender[1].local), back = home.project(blender[2].local)
        XCTAssertEqual(front.x, back.x, accuracy: 0.01)
        XCTAssertEqual(front.y, back.y, accuracy: 0.01)
    }

    func testSlidingMovesTheWholeMouseSideways() {
        let camera = DeskCamera(frame: 720)
        let still = camera.project(blender[0].local), slid = camera.project(blender[0].local, slide: 1)
        XCTAssertGreaterThan(slid.x, still.x + 10)
        XCTAssertEqual(slid.y, still.y, accuracy: 2)
    }

    func testTheLidPeaksInTheMiddleAndMeetsTheEdgeAtTheOutline() {
        let top = MagicMouseFigure.surface(u: 0, v: 0)
        XCTAssertEqual(top.z, 2.16, accuracy: 1e-6)
        let side = MagicMouseFigure.surface(u: 1, v: 0)
        XCTAssertEqual(side.x, 2.855, accuracy: 1e-6)
        XCTAssertEqual(side.z, 1.34, accuracy: 1e-6)   // seam 1.26 + the lid's edge land
        let front = MagicMouseFigure.surface(u: 0, v: -1)
        XCTAssertEqual(front.y, 5.675, accuracy: 1e-6) // front points to +y
    }

    func testTheLidFacesStraightUpAtItsTop() {
        let n = MagicMouseFigure.surfaceNormal(u: 0, v: 0)
        XCTAssertEqual(n.z, 1, accuracy: 0.001)
    }

    func testAFingerTouchIsDrawnAroundTheTouchedPoint() {
        let camera = DeskCamera(frame: 720)
        let ring = camera.disc(u: 0.3, v: -0.4, radius: 0.5)
        XCTAssertEqual(ring.count, 28)
        guard !ring.isEmpty else { return }
        let centre = camera.project(MagicMouseFigure.surface(u: 0.3, v: -0.4))
        let mx = ring.map(\.x).reduce(0, +) / Double(ring.count), my = ring.map(\.y).reduce(0, +) / Double(ring.count)
        XCTAssertEqual(mx, centre.x, accuracy: 1)
        XCTAssertEqual(my, centre.y, accuracy: 1)
        // 0.5 cm on the lid is roughly 0.5 cm on screen: tens of pixels across, not a dot.
        let width = ring.map(\.x).max()! - ring.map(\.x).min()!
        XCTAssertGreaterThan(width, 30)
        XCTAssertLessThan(width, 70)
    }
}
