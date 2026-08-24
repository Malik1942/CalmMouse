// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "CalmMouse",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CalmMouse", targets: ["CalmMouse"]),
        .library(name: "CalmMouseCore", targets: ["CalmMouseCore"]),
    ],
    targets: [
        // Pure logic: no AppKit/CoreGraphics dependency, fully unit-tested.
        .target(name: "CalmMouseCore"),
        // Menu-bar app: CGEventTap + IOKit device identification + settings UI.
        .executableTarget(
            name: "CalmMouse",
            dependencies: ["CalmMouseCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("IOKit"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
        .testTarget(name: "CalmMouseCoreTests", dependencies: ["CalmMouseCore"]),
        // Exercises the CGEvent bridging (phase codes, delta rewriting, modifier flags)
        // without needing Accessibility permission or a real event tap.
        .testTarget(name: "CalmMouseAppTests", dependencies: ["CalmMouse", "CalmMouseCore"]),
    ]
)
