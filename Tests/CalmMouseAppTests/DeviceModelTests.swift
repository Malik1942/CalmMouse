import AVFoundation
import XCTest
@testable import CalmMouse
import CalmMouseCore

final class DeviceModelTests: XCTestCase {
    func testFolders() {
        XCTAssertEqual(DeviceModel.magicMouse.folder(.white), "MagicMouse/white")
        XCTAssertEqual(DeviceModel.magicMouse.folder(.black), "MagicMouse/black")
        // The wheel mouse comes in pale grey (white) and graphite (black), like the Magic Mouse.
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.white), "Devices/wheel/white")
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.black), "Devices/wheel/black")
        // The controller has one colour: the finish doesn't change it.
        XCTAssertEqual(DeviceModel.controller.folder(.white), "Devices/controller")
        XCTAssertEqual(DeviceModel.controller.folder(.black), "Devices/controller")
    }

    func testEachMouseRemembersItsOwnColor() {
        // A white Magic Mouse and a graphite wheel mouse can both be shown as they are.
        XCTAssertEqual(DeviceModel.magicMouse.finishKey, "magicMouseFinish")
        XCTAssertEqual(DeviceModel.wheelMouse.finishKey, "wheelMouseFinish")
        XCTAssertNil(DeviceModel.controller.finishKey)
    }

    func testFromDeviceKind() {
        XCTAssertEqual(DeviceModel(.magicMouse), .magicMouse)
        XCTAssertEqual(DeviceModel(.wheelMouse), .wheelMouse)
        XCTAssertEqual(DeviceModel(.controller), .controller)
    }
}

/// The rendered devices the app draws: a still and 72 turn frames each.
final class DeviceAssetTests: XCTestCase {
    private let resources = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Resources")

    func testEveryDeviceHasAStillAndSeventyTwoFrames() {
        let folders = DeviceModel.allCases.flatMap { model in
            Set(MouseFinish.allCases.map { model.folder($0) }).sorted()
        }
        XCTAssertEqual(folders.count, 5)   // Magic Mouse ×2, wheel mouse ×2, controller
        for folder in folders {
            let dir = resources.appendingPathComponent(folder)
            XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("home.webp").path), folder)
            for i in 0..<72 {
                let name = String(format: "turn-%02d.webp", i)
                XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent(name).path), "\(folder)/\(name)")
            }
        }
    }

    func testTheMagicMouseIntroIsHEVCWithAlphaAndThreeSeconds() async throws {
        for finish in ["white", "black"] {
            let url = resources.appendingPathComponent("MagicMouse/\(finish)/intro.mov")
            let asset = AVURLAsset(url: url)
            let tracks = try await asset.loadTracks(withMediaType: .video)
            let track = try XCTUnwrap(tracks.first, finish)
            let descriptions = try await track.load(.formatDescriptions)
            let format = try XCTUnwrap(descriptions.first)
            // HEVC with alpha is stored as HEVC whose description says it carries an alpha channel
            // (kCMVideoCodecType_HEVCWithAlpha is only what you ask the encoder for).
            XCTAssertEqual(CMFormatDescriptionGetMediaSubType(format), kCMVideoCodecType_HEVC, finish)
            let alpha = CMFormatDescriptionGetExtension(format, extensionKey: kCMFormatDescriptionExtension_ContainsAlphaChannel)
            XCTAssertEqual(alpha as? Bool, true, "\(finish) intro has no alpha channel")
            // And it decodes as transparent around the mouse: the first frame is empty desk.
            let generator = AVAssetImageGenerator(asset: asset)
            generator.requestedTimeToleranceBefore = .zero
            generator.requestedTimeToleranceAfter = .zero
            let (corner, _) = try await generator.image(at: .zero)
            XCTAssertEqual(alphaAt(corner, x: 4, y: 4), 0, "\(finish) intro's corner isn't transparent")
            let seconds = try await asset.load(.duration).seconds
            XCTAssertEqual(seconds, 3.0, accuracy: 0.05, finish)
            let size = try FileManager.default.attributesOfItem(atPath: url.path)[.size] as! Int
            XCTAssertLessThanOrEqual(size, 4_000_000, "\(finish) intro is over the 4 MB budget")
        }
    }

    private func alphaAt(_ image: CGImage, x: Int, y: Int) -> UInt8 {
        var pixel = [UInt8](repeating: 0, count: 4)
        let ctx = CGContext(data: &pixel, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.draw(image, in: CGRect(x: -x, y: -(image.height - 1 - y), width: image.width, height: image.height))
        return pixel[3]
    }
}
