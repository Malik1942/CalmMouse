import XCTest
@testable import CalmMouseCore

final class OnboardingRouteTests: XCTestCase {
    private let head: [OnboardingStep] = [.devices, .detect, .permission]

    func testNothingFoundGoesStraightToDone() {
        XCTAssertEqual(OnboardingRoute.steps(for: []), head + [.done])
    }

    func testMagicMouseKeepsPresetAndTryIt() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.magicMouse]), head + [.preset, .tryIt, .done])
    }

    func testWheelMouseGetsItsPage() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.wheelMouse]), head + [.wheel, .done])
    }

    func testControllerGetsItsPage() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.controller]), head + [.controller, .done])
    }

    func testSeveralKindsRunInAFixedOrder() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.controller, .magicMouse]),
                       head + [.preset, .tryIt, .controller, .done])
        XCTAssertEqual(OnboardingRoute.steps(for: Set(DeviceKind.allCases)),
                       head + [.preset, .tryIt, .wheel, .controller, .done])
    }
}
