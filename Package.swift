// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Godmouse",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Godmouse", targets: ["Godmouse"]),
        .library(name: "GodmouseCore", targets: ["GodmouseCore"]),
    ],
    targets: [
        // Pure logic: no AppKit/CoreGraphics dependency, fully unit-tested.
        .target(name: "GodmouseCore"),
        // Menu-bar app: CGEventTap + IOKit device identification + settings UI.
        .executableTarget(
            name: "Godmouse",
            dependencies: ["GodmouseCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("IOKit"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
        .testTarget(name: "GodmouseCoreTests", dependencies: ["GodmouseCore"]),
        // Exercises the CGEvent bridging (phase codes, delta rewriting, modifier flags)
        // without needing Accessibility permission or a real event tap.
        .testTarget(name: "GodmouseAppTests", dependencies: ["Godmouse", "GodmouseCore"]),
    ]
)
