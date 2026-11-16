import XCTest

/// Every string the app looks up with `L("…")` must be in each language's Localizable.strings,
/// or a Chinese Mac shows that line in English.
final class LocalizationTests: XCTestCase {
    private let root = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

    /// The `L("…")` keys in the app's sources, as written there (escapes kept, interpolations skipped).
    private func lookedUpKeys() throws -> Set<String> {
        let sources = root.appendingPathComponent("Sources/CalmMouse")
        let files = try FileManager.default.contentsOfDirectory(at: sources, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "swift" }
        let call = try NSRegularExpression(pattern: #"\bL\("((?:[^"\\]|\\.)*)"\)"#)
        var keys = Set<String>()
        for file in files {
            let text = try String(contentsOf: file, encoding: .utf8)
            for m in call.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
                let key = String(text[Range(m.range(at: 1), in: text)!])
                if !key.contains(#"\("#) { keys.insert(key) }
            }
        }
        return keys
    }

    /// The keys a Localizable.strings file defines, as written there.
    private func definedKeys(_ language: String) throws -> Set<String> {
        let file = root.appendingPathComponent("Resources/\(language).lproj/Localizable.strings")
        let text = try String(contentsOf: file, encoding: .utf8)
        let entry = try NSRegularExpression(pattern: #"(?m)^"((?:[^"\\]|\\.)*)"\s*="#)
        return Set(entry.matches(in: text, range: NSRange(text.startIndex..., in: text)).map {
            String(text[Range($0.range(at: 1), in: text)!])
        })
    }

    func testEveryLookedUpStringIsTranslated() throws {
        let keys = try lookedUpKeys()
        XCTAssertGreaterThan(keys.count, 50, "the scan should find the app's captions")
        for language in ["en", "zh-Hans", "zh-Hant"] {
            let missing = keys.subtracting(try definedKeys(language)).sorted()
            XCTAssertEqual(missing, [], "\(language) is missing \(missing.count) string(s)")
        }
    }
}
