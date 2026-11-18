import Foundation
import XCTest
@testable import CalmMouseCore

// Golden traces: how the core types behave, written down step by step, so the Windows port
// (windows/tests/CalmMouse.Core.Tests/GoldenTraceTests.cs) can replay the same inputs and check
// that it produces the same outputs. The file format is described in
// shared/fixtures/core/README.md.
//
// The inputs come from two places: the hand-written scenarios in shared/fixtures/core/scenarios,
// and sequences generated here from a fixed-seed SplitMix64 (written out below, never the
// system's random number generator). Both run through the real Swift types, and the inputs with
// every output go to shared/fixtures/core/traces, one file per scenario. The generated inputs
// are in the trace files too, so the C# side never has to generate anything.
//
//     CALMMOUSE_WRITE_TRACES=1 swift test --filter GoldenTraceTests
//
// writes the trace files (and deletes ones no scenario produces any more). Without the variable
// the test compares what the code does now with the committed files and fails on a difference.

final class GoldenTraceTests: XCTestCase {
    func testGoldenTraces() {
        let write = ProcessInfo.processInfo.environment["CALMMOUSE_WRITE_TRACES"] == "1"
        do {
            try GoldenTraces.run(write: write)
        } catch {
            XCTFail("\(error)")
        }
    }
}

// MARK: - Building, writing and comparing traces

private typealias TraceRun = (config: TraceJSON, steps: [TraceJSON], outputs: [TraceJSON])

private struct Scenario {
    var component: String
    var description: String
    var config: TraceJSON?
    var steps: [TraceJSON]
}

private enum GoldenTraces {
    /// The four components, with the name their generated traces use.
    static let components: [(name: String, slug: String)] = [
        (name: "ScrollBlocker", slug: "scroll-blocker"),
        (name: "WheelSmoother", slug: "wheel-smoother"),
        (name: "ControllerPointer", slug: "controller-pointer"),
        (name: "TapRecognizer", slug: "tap-recognizer"),
    ]
    static let generatedPerComponent = 20

    static func run(write: Bool) throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // Tests/CalmMouseCoreTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // the repo
        let core = root.appendingPathComponent("shared").appendingPathComponent("fixtures").appendingPathComponent("core")
        let scenarioFolder = core.appendingPathComponent("scenarios")
        let traceFolder = core.appendingPathComponent("traces")

        var traces: [(name: String, trace: TraceJSON)] = []
        let scenarios = try loadScenarios(scenarioFolder)
        for item in scenarios {
            let source = "shared/fixtures/core/scenarios/\(item.name).json"
            let trace = try build(item.scenario, name: item.name, source: source)
            traces.append((name: item.name, trace: trace))
        }
        let generated = try generateAll()
        traces.append(contentsOf: generated)

        if write {
            try writeAll(traces, to: traceFolder)
        } else {
            try compareAll(traces, with: traceFolder)
        }
    }

    static func loadScenarios(_ folder: URL) throws -> [(name: String, scenario: Scenario)] {
        let files = try FileManager.default.contentsOfDirectory(atPath: folder.path)
            .filter { $0.hasSuffix(".json") }
            .sorted()
        guard !files.isEmpty else {
            throw TraceError("No scenarios found in \(folder.path).")
        }
        var result: [(name: String, scenario: Scenario)] = []
        for file in files {
            let name = String(file.dropLast(5))
            let context = "scenarios/\(file)"
            if name.hasPrefix("generated-") {
                throw TraceError("\(context): names starting with generated- are kept for the generated sequences.")
            }
            let value = try readJSON(folder.appendingPathComponent(file))
            let f = try Fields(value, context, allowed: ["component", "name", "description", "config", "steps"])
            if let declared = try f.optionalString("name"), declared != name {
                throw TraceError("\(context): \"name\" is \(declared), but the file is called \(name).json")
            }
            let scenario = try Scenario(component: f.string("component", nil),
                                        description: f.string("description", ""),
                                        config: f.value("config"),
                                        steps: f.array("steps"))
            result.append((name: name, scenario: scenario))
        }
        return result
    }

    static func generateAll() throws -> [(name: String, trace: TraceJSON)] {
        var result: [(name: String, trace: TraceJSON)] = []
        for (c, component) in components.enumerated() {
            for i in 0..<generatedPerComponent {
                let seed: UInt64 = 0x5EED_0000 + UInt64(c * 1000 + i)
                var rng = SplitMix64(seed: seed)
                let scenario: Scenario
                switch component.name {
                case "ScrollBlocker": scenario = ScrollBlockerGenerator.make(&rng)
                case "WheelSmoother": scenario = WheelSmootherGenerator.make(&rng)
                case "ControllerPointer": scenario = ControllerPointerGenerator.make(&rng)
                case "TapRecognizer": scenario = TapRecognizerGenerator.make(&rng)
                default: throw TraceError("No generator for \(component.name).")
                }
                let number = i + 1 < 10 ? "0\(i + 1)" : "\(i + 1)"
                let name = "generated-\(component.slug)-\(number)"
                let source = "generated by Tests/CalmMouseCoreTests/GoldenTraceTests.swift from SplitMix64 seed \(seed)"
                let trace = try build(scenario, name: name, source: source)
                result.append((name: name, trace: trace))
            }
        }
        return result
    }

    static func build(_ scenario: Scenario, name: String, source: String) throws -> TraceJSON {
        let result: TraceRun
        do {
            switch scenario.component {
            case "ScrollBlocker": result = try ScrollBlockerTrace.run(config: scenario.config, steps: scenario.steps)
            case "WheelSmoother": result = try WheelSmootherTrace.run(config: scenario.config, steps: scenario.steps)
            case "ControllerPointer": result = try ControllerPointerTrace.run(config: scenario.config, steps: scenario.steps)
            case "TapRecognizer": result = try TapRecognizerTrace.run(config: scenario.config, steps: scenario.steps)
            default: throw TraceError("unknown component \"\(scenario.component)\"")
            }
        } catch {
            throw TraceError("\(name): \(error)")
        }
        var o: [String: TraceJSON] = [:]
        o["name"] = .string(name)
        o["component"] = .string(scenario.component)
        o["description"] = .string(scenario.description)
        o["source"] = .string(source)
        o["config"] = result.config
        o["steps"] = .array(result.steps)
        o["outputs"] = .array(result.outputs)
        return .object(o)
    }

    static func writeAll(_ traces: [(name: String, trace: TraceJSON)], to folder: URL) throws {
        let fileManager = FileManager.default
        try fileManager.createDirectory(at: folder, withIntermediateDirectories: true)
        let keep = Set(traces.map { "\($0.name).json" })
        let existing = try fileManager.contentsOfDirectory(atPath: folder.path)
        for file in existing where file.hasSuffix(".json") && !keep.contains(file) {
            try fileManager.removeItem(at: folder.appendingPathComponent(file))
        }
        for item in traces {
            let url = folder.appendingPathComponent("\(item.name).json")
            let text = TraceWriter.text(item.trace)
            try text.write(to: url, atomically: true, encoding: .utf8)
            // What was written must read back as what was meant (numbers included).
            let back = try readJSON(url)
            if let difference = compare(committed: back, fresh: item.trace) {
                throw TraceError("traces/\(item.name).json does not read back as written: \(difference)")
            }
        }
    }

    static func compareAll(_ traces: [(name: String, trace: TraceJSON)], with folder: URL) throws {
        let fileManager = FileManager.default
        guard fileManager.fileExists(atPath: folder.path) else {
            throw TraceError("There are no golden traces yet: \(folder.path) does not exist. "
                + "Write them with CALMMOUSE_WRITE_TRACES=1 swift test --filter GoldenTraceTests "
                + "(or take them from the golden-traces artifact of the macOS CI job) and commit them.")
        }
        var problems: [String] = []
        let names = Set(traces.map { "\($0.name).json" })
        let files = try fileManager.contentsOfDirectory(atPath: folder.path)
            .filter { $0.hasSuffix(".json") }
            .sorted()
        for file in files where !names.contains(file) {
            problems.append("traces/\(file): no scenario produces this trace any more.")
        }
        for item in traces {
            let url = folder.appendingPathComponent("\(item.name).json")
            guard fileManager.fileExists(atPath: url.path) else {
                problems.append("traces/\(item.name).json is missing.")
                continue
            }
            let committed: TraceJSON
            do {
                committed = try readJSON(url)
            } catch {
                problems.append("traces/\(item.name).json: \(error)")
                continue
            }
            if let difference = compare(committed: committed, fresh: item.trace) {
                problems.append("traces/\(item.name).json: \(difference)")
            }
        }
        if !problems.isEmpty {
            throw TraceError("The core no longer does what its golden traces say (\(problems.count) problems). "
                + "If the change is meant, rewrite the traces with CALMMOUSE_WRITE_TRACES=1 swift test "
                + "--filter GoldenTraceTests, commit them, and bring the Windows port in line.\n"
                + problems.joined(separator: "\n"))
        }
    }

    /// The first difference between a committed trace and a fresh one, or nil when they match.
    /// Numbers match within 1e-9 (absolute or relative); everything else must be equal.
    static func compare(committed: TraceJSON, fresh: TraceJSON) -> String? {
        guard case .object(let c) = committed, case .object(let f) = fresh else {
            return "not a JSON object"
        }
        for key in Set(c.keys).union(f.keys).sorted() where key != "steps" && key != "outputs" {
            guard let a = c[key] else { return "\(key): not in the committed file" }
            guard let b = f[key] else { return "\(key): in the committed file, but no longer produced" }
            if let d = difference(a, b, path: key) { return d }
        }
        guard case .array(let committedSteps)? = c["steps"], case .array(let freshSteps)? = f["steps"],
              case .array(let committedOutputs)? = c["outputs"], case .array(let freshOutputs)? = f["outputs"] else {
            return "steps or outputs missing"
        }
        if committedSteps.count != freshSteps.count || committedOutputs.count != freshOutputs.count
            || freshSteps.count != freshOutputs.count {
            return "committed \(committedSteps.count) steps and \(committedOutputs.count) outputs, "
                + "now \(freshSteps.count) and \(freshOutputs.count)"
        }
        for i in 0..<freshSteps.count {
            if let d = difference(committedSteps[i], freshSteps[i], path: "steps[\(i)]") { return d }
            if let d = difference(committedOutputs[i], freshOutputs[i], path: "outputs[\(i)]") {
                return "step \(i) \(TraceWriter.inline(freshSteps[i])): \(d)"
            }
        }
        return nil
    }

    static func difference(_ a: TraceJSON, _ b: TraceJSON, path: String) -> String? {
        switch (a, b) {
        case (.null, .null):
            return nil
        case (.bool(let x), .bool(let y)):
            return x == y ? nil : "\(path): committed \(x), now \(y)"
        case (.string(let x), .string(let y)):
            return x == y ? nil : "\(path): committed \"\(x)\", now \"\(y)\""
        case (.array(let x), .array(let y)):
            if x.count != y.count { return "\(path): committed \(x.count) items, now \(y.count)" }
            for i in 0..<x.count {
                if let d = difference(x[i], y[i], path: "\(path)[\(i)]") { return d }
            }
            return nil
        case (.object(let x), .object(let y)):
            for key in Set(x.keys).union(y.keys).sorted() {
                guard let xv = x[key] else { return "\(path).\(key): not in the committed file" }
                guard let yv = y[key] else { return "\(path).\(key): in the committed file, but no longer produced" }
                if let d = difference(xv, yv, path: "\(path).\(key)") { return d }
            }
            return nil
        default:
            if let x = number(a), let y = number(b) {
                return closeEnough(x, y) ? nil : "\(path): committed \(x), now \(y)"
            }
            return "\(path): committed \(TraceWriter.inline(a)), now \(TraceWriter.inline(b))"
        }
    }

    static func number(_ value: TraceJSON) -> Double? {
        switch value {
        case .int(let i): return Double(i)
        case .double(let d): return d
        default: return nil
        }
    }

    static func closeEnough(_ a: Double, _ b: Double) -> Bool {
        if a == b { return true }
        let diff = abs(a - b)
        return diff <= 1e-9 || diff <= 1e-9 * max(abs(a), abs(b))
    }
}

// MARK: - JSON

/// A JSON value. Files are read with JSONDecoder, but written by `TraceWriter`, so the number
/// format is Swift's own shortest round-trip `description` rather than whatever Foundation's
/// JSON writer does on a given macOS.
private enum TraceJSON: Decodable {
    case null
    case bool(Bool)
    case int(Int)
    case double(Double)
    case string(String)
    case array([TraceJSON])
    case object([String: TraceJSON])

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .double(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([TraceJSON].self) {
            self = .array(value)
        } else if let value = try? container.decode([String: TraceJSON].self) {
            self = .object(value)
        } else {
            throw DecodingError.dataCorruptedError(in: container, debugDescription: "Not a JSON value.")
        }
    }

    var isNull: Bool {
        if case .null = self { return true }
        return false
    }
}

private struct TraceError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

private func readJSON(_ url: URL) throws -> TraceJSON {
    let data = try Data(contentsOf: url)
    do {
        return try JSONDecoder().decode(TraceJSON.self, from: data)
    } catch {
        throw TraceError("\(url.lastPathComponent) is not valid JSON: \(error)")
    }
}

private func jsonBool(_ value: Bool?) -> TraceJSON {
    if let value = value { return .bool(value) }
    return .null
}

private func jsonDouble(_ value: Double?) -> TraceJSON {
    if let value = value { return .double(value) }
    return .null
}

private func jsonString(_ value: String?) -> TraceJSON {
    if let value = value { return .string(value) }
    return .null
}

private func opName(_ step: TraceJSON, _ context: String) throws -> String {
    guard case .object(let fields) = step, let op = fields["op"], case .string(let name) = op else {
        throw TraceError("\(context): every step needs an \"op\" string")
    }
    return name
}

/// Typed reads from a JSON object. A field may be left out of a hand-written scenario when its
/// fallback is not nil; unknown fields are an error, so a typo can't pass silently.
private struct Fields {
    let values: [String: TraceJSON]
    let context: String

    init(_ value: TraceJSON, _ context: String, allowed: [String]) throws {
        guard case .object(let values) = value else {
            throw TraceError("\(context): expected a JSON object")
        }
        for key in values.keys.sorted() where !allowed.contains(key) {
            throw TraceError("\(context): unknown field \"\(key)\" (known: \(allowed.joined(separator: ", ")))")
        }
        self.values = values
        self.context = context
    }

    func value(_ key: String) -> TraceJSON? {
        values[key]
    }

    func double(_ key: String, _ fallback: Double?) throws -> Double {
        guard let value = values[key] else {
            if let fallback = fallback { return fallback }
            throw TraceError("\(context): missing \"\(key)\"")
        }
        switch value {
        case .double(let d): return d
        case .int(let i): return Double(i)
        default: throw TraceError("\(context): \"\(key)\" must be a number")
        }
    }

    func int(_ key: String, _ fallback: Int?) throws -> Int {
        let d = try double(key, fallback.map { Double($0) })
        guard d.rounded() == d, abs(d) <= 9_007_199_254_740_992 else {
            throw TraceError("\(context): \"\(key)\" must be a whole number")
        }
        return Int(d)
    }

    func bool(_ key: String, _ fallback: Bool?) throws -> Bool {
        guard let value = values[key] else {
            if let fallback = fallback { return fallback }
            throw TraceError("\(context): missing \"\(key)\"")
        }
        guard case .bool(let b) = value else {
            throw TraceError("\(context): \"\(key)\" must be true or false")
        }
        return b
    }

    func string(_ key: String, _ fallback: String?) throws -> String {
        guard let value = values[key] else {
            if let fallback = fallback { return fallback }
            throw TraceError("\(context): missing \"\(key)\"")
        }
        guard case .string(let s) = value else {
            throw TraceError("\(context): \"\(key)\" must be a string")
        }
        return s
    }

    func optionalBool(_ key: String) throws -> Bool? {
        guard let value = values[key], !value.isNull else { return nil }
        guard case .bool(let b) = value else {
            throw TraceError("\(context): \"\(key)\" must be true, false or null")
        }
        return b
    }

    func optionalDouble(_ key: String) throws -> Double? {
        guard let value = values[key], !value.isNull else { return nil }
        return try double(key, nil)
    }

    func optionalString(_ key: String) throws -> String? {
        guard let value = values[key], !value.isNull else { return nil }
        guard case .string(let s) = value else {
            throw TraceError("\(context): \"\(key)\" must be a string or null")
        }
        return s
    }

    func array(_ key: String) throws -> [TraceJSON] {
        guard let value = values[key] else {
            throw TraceError("\(context): missing \"\(key)\"")
        }
        guard case .array(let items) = value else {
            throw TraceError("\(context): \"\(key)\" must be an array")
        }
        return items
    }
}

/// Writes JSON with sorted keys. Objects and arrays at the top two levels are spread over lines;
/// anything deeper stays on one line, so every step and every output is one line and a diff
/// names the step that changed. Numbers use Swift's shortest round-trip form.
private enum TraceWriter {
    static func text(_ value: TraceJSON) -> String {
        var out = ""
        write(value, depth: 0, into: &out)
        out += "\n"
        return out
    }

    static func inline(_ value: TraceJSON) -> String {
        var out = ""
        write(value, depth: 2, into: &out)
        return out
    }

    private static func indent(_ depth: Int) -> String {
        String(repeating: "  ", count: depth)
    }

    private static func write(_ value: TraceJSON, depth: Int, into out: inout String) {
        switch value {
        case .null:
            out += "null"
        case .bool(let b):
            out += b ? "true" : "false"
        case .int(let i):
            out += String(i)
        case .double(let d):
            // Not finite would not be JSON; writeAll reads every file back, so it would fail there.
            out += d.description
        case .string(let s):
            out += quoted(s)
        case .array(let items):
            if items.isEmpty {
                out += "[]"
            } else if depth >= 2 {
                out += "["
                for (i, item) in items.enumerated() {
                    if i > 0 { out += ", " }
                    write(item, depth: depth + 1, into: &out)
                }
                out += "]"
            } else {
                out += "[\n"
                for (i, item) in items.enumerated() {
                    out += indent(depth + 1)
                    write(item, depth: depth + 1, into: &out)
                    out += i + 1 < items.count ? ",\n" : "\n"
                }
                out += indent(depth) + "]"
            }
        case .object(let fields):
            let keys = fields.keys.sorted()
            if keys.isEmpty {
                out += "{}"
            } else if depth >= 2 {
                out += "{"
                for (i, key) in keys.enumerated() {
                    if i > 0 { out += ", " }
                    out += quoted(key) + ": "
                    write(fields[key] ?? .null, depth: depth + 1, into: &out)
                }
                out += "}"
            } else {
                out += "{\n"
                for (i, key) in keys.enumerated() {
                    out += indent(depth + 1) + quoted(key) + ": "
                    write(fields[key] ?? .null, depth: depth + 1, into: &out)
                    out += i + 1 < keys.count ? ",\n" : "\n"
                }
                out += indent(depth) + "}"
            }
        }
    }

    private static func quoted(_ s: String) -> String {
        var out = "\""
        for scalar in s.unicodeScalars {
            switch scalar {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            default:
                if scalar.value < 0x20 {
                    let hex = String(scalar.value, radix: 16)
                    out += "\\u" + String(repeating: "0", count: 4 - hex.count) + hex
                } else {
                    out.unicodeScalars.append(scalar)
                }
            }
        }
        out += "\""
        return out
    }
}

// MARK: - Names used in the files

private enum Names {
    static func phase(_ p: ScrollPhase) -> String {
        switch p {
        case .none: return "none"
        case .mayBegin: return "mayBegin"
        case .began: return "began"
        case .changed: return "changed"
        case .ended: return "ended"
        case .cancelled: return "cancelled"
        }
    }

    static func phase(named name: String, _ context: String) throws -> ScrollPhase {
        switch name {
        case "none": return ScrollPhase.none
        case "mayBegin": return .mayBegin
        case "began": return .began
        case "changed": return .changed
        case "ended": return .ended
        case "cancelled": return .cancelled
        default: throw TraceError("\(context): unknown phase \"\(name)\"")
        }
    }

    static func momentum(_ m: MomentumPhase) -> String {
        switch m {
        case .none: return "none"
        case .began: return "began"
        case .continued: return "continued"
        case .ended: return "ended"
        }
    }

    static func momentum(named name: String, _ context: String) throws -> MomentumPhase {
        switch name {
        case "none": return MomentumPhase.none
        case "began": return .began
        case "continued": return .continued
        case "ended": return .ended
        default: throw TraceError("\(context): unknown momentum phase \"\(name)\"")
        }
    }

    static func controllerButton(_ b: ControllerButton) -> String {
        switch b {
        case .south: return "south"
        case .east: return "east"
        case .touchpad: return "touchpad"
        case .menu: return "menu"
        case .options: return "options"
        case .leftStick: return "leftStick"
        case .rightStick: return "rightStick"
        }
    }

    static func controllerButton(named name: String, _ context: String) throws -> ControllerButton {
        switch name {
        case "south": return .south
        case "east": return .east
        case "touchpad": return .touchpad
        case "menu": return .menu
        case "options": return .options
        case "leftStick": return .leftStick
        case "rightStick": return .rightStick
        default: throw TraceError("\(context): unknown controller button \"\(name)\"")
        }
    }

    static func pointerButton(_ b: PointerButton) -> String {
        switch b {
        case .left: return "left"
        case .right: return "right"
        }
    }

    static func tapEvent(_ e: TapEvent) -> String {
        switch e {
        case .tap: return "tap"
        case .rightTap: return "rightTap"
        case .dragBegan: return "dragBegan"
        case .dragEnded: return "dragEnded"
        case .dragSwipeCancelled: return "dragSwipeCancelled"
        case .dragCancelled: return "dragCancelled"
        case .dragPressed: return "dragPressed"
        }
    }
}

// MARK: - ScrollBlocker

private enum ScrollBlockerTrace {
    static let baseKeys = [
        "blockScrollWhileClicked", "releaseGrace", "deadZone", "axisLock", "axisLockThreshold",
        "axisLockRatio", "momentumEnabled", "blockHorizontalScroll", "disableScrollEntirely",
        "modifierActions", "reverseMagicMouseScroll", "reverseWheelScroll",
        "blockScrollWhileWheelPressed", "smoothWheelScroll", "wheelScrollSpeed",
    ]
    static let ruleKeys = [
        "bundleID", "name", "enabled", "blockScrollWhileClicked", "disableScrollEntirely",
        "blockHorizontalScroll", "momentumEnabled", "axisLock", "deadZone",
    ]
    static let wheelKeys = ["reverse", "blockWhilePressed", "smooth", "speed"]
    static let scrollKeys = [
        "op", "t", "fromMagicMouse", "fromWheelMouse", "isContinuous", "phase", "momentum",
        "deltaX", "deltaY", "modifiers", "wheel",
    ]

    /// Read from explicit fields, not BlockerConfig's Codable: that one leaves out
    /// reverseMagicMouseScroll (the Mac app stores it separately).
    static func readRules(_ value: TraceJSON?) throws -> RuleSet {
        let d = BlockerConfig()
        var base = BlockerConfig()
        var appRules: [AppRule] = []
        guard let value = value else { return RuleSet(base: base, appRules: appRules) }
        let f = try Fields(value, "config", allowed: ["base", "appRules"])
        if let baseValue = f.value("base") {
            let g = try Fields(baseValue, "config.base", allowed: baseKeys)
            base.blockScrollWhileClicked = try g.bool("blockScrollWhileClicked", d.blockScrollWhileClicked)
            base.releaseGrace = try g.double("releaseGrace", d.releaseGrace)
            base.deadZone = try g.double("deadZone", d.deadZone)
            base.axisLock = try g.bool("axisLock", d.axisLock)
            base.axisLockThreshold = try g.double("axisLockThreshold", d.axisLockThreshold)
            base.axisLockRatio = try g.double("axisLockRatio", d.axisLockRatio)
            base.momentumEnabled = try g.bool("momentumEnabled", d.momentumEnabled)
            base.blockHorizontalScroll = try g.bool("blockHorizontalScroll", d.blockHorizontalScroll)
            base.disableScrollEntirely = try g.bool("disableScrollEntirely", d.disableScrollEntirely)
            if let actions = g.value("modifierActions") {
                base.modifierActions = try readModifierActions(actions)
            }
            base.reverseMagicMouseScroll = try g.bool("reverseMagicMouseScroll", d.reverseMagicMouseScroll)
            base.reverseWheelScroll = try g.bool("reverseWheelScroll", d.reverseWheelScroll)
            base.blockScrollWhileWheelPressed = try g.bool("blockScrollWhileWheelPressed", d.blockScrollWhileWheelPressed)
            base.smoothWheelScroll = try g.bool("smoothWheelScroll", d.smoothWheelScroll)
            base.wheelScrollSpeed = try g.double("wheelScrollSpeed", d.wheelScrollSpeed)
        }
        if let rulesValue = f.value("appRules") {
            guard case .array(let items) = rulesValue else {
                throw TraceError("config.appRules must be an array")
            }
            for (i, item) in items.enumerated() {
                let g = try Fields(item, "config.appRules[\(i)]", allowed: ruleKeys)
                let rule = try AppRule(bundleID: g.string("bundleID", nil),
                                       name: g.string("name", ""),
                                       enabled: g.bool("enabled", true),
                                       blockScrollWhileClicked: g.optionalBool("blockScrollWhileClicked"),
                                       disableScrollEntirely: g.optionalBool("disableScrollEntirely"),
                                       blockHorizontalScroll: g.optionalBool("blockHorizontalScroll"),
                                       momentumEnabled: g.optionalBool("momentumEnabled"),
                                       axisLock: g.optionalBool("axisLock"),
                                       deadZone: g.optionalDouble("deadZone"))
                appRules.append(rule)
            }
        }
        return RuleSet(base: base, appRules: appRules)
    }

    static func readModifierActions(_ value: TraceJSON) throws -> [ModifierCombo: ScrollAction] {
        guard case .array(let items) = value else {
            throw TraceError("config.base.modifierActions must be an array")
        }
        var actions: [ModifierCombo: ScrollAction] = [:]
        for (i, item) in items.enumerated() {
            let context = "config.base.modifierActions[\(i)]"
            let f = try Fields(item, context, allowed: ["modifiers", "action"])
            let raw = try f.int("modifiers", nil)
            let name = try f.string("action", nil)
            guard let action = ScrollAction(rawValue: name) else {
                throw TraceError("\(context): unknown action \"\(name)\"")
            }
            let modifiers = ModifierCombo(rawValue: raw)
            guard actions[modifiers] == nil else {
                throw TraceError("\(context): modifiers \(raw) are listed twice")
            }
            actions[modifiers] = action
        }
        return actions
    }

    static func rulesJSON(_ rules: RuleSet) -> TraceJSON {
        let c = rules.base
        var actions: [TraceJSON] = []
        for raw in c.modifierActions.keys.map({ $0.rawValue }).sorted() {
            let action = c.modifierActions[ModifierCombo(rawValue: raw)] ?? .normal
            actions.append(.object(["modifiers": .int(raw), "action": .string(action.rawValue)]))
        }
        var base: [String: TraceJSON] = [:]
        base["blockScrollWhileClicked"] = .bool(c.blockScrollWhileClicked)
        base["releaseGrace"] = .double(c.releaseGrace)
        base["deadZone"] = .double(c.deadZone)
        base["axisLock"] = .bool(c.axisLock)
        base["axisLockThreshold"] = .double(c.axisLockThreshold)
        base["axisLockRatio"] = .double(c.axisLockRatio)
        base["momentumEnabled"] = .bool(c.momentumEnabled)
        base["blockHorizontalScroll"] = .bool(c.blockHorizontalScroll)
        base["disableScrollEntirely"] = .bool(c.disableScrollEntirely)
        base["modifierActions"] = .array(actions)
        base["reverseMagicMouseScroll"] = .bool(c.reverseMagicMouseScroll)
        base["reverseWheelScroll"] = .bool(c.reverseWheelScroll)
        base["blockScrollWhileWheelPressed"] = .bool(c.blockScrollWhileWheelPressed)
        base["smoothWheelScroll"] = .bool(c.smoothWheelScroll)
        base["wheelScrollSpeed"] = .double(c.wheelScrollSpeed)

        var appRules: [TraceJSON] = []
        for r in rules.appRules {
            var o: [String: TraceJSON] = [:]
            o["bundleID"] = .string(r.bundleID)
            o["name"] = .string(r.name)
            o["enabled"] = .bool(r.enabled)
            o["blockScrollWhileClicked"] = jsonBool(r.blockScrollWhileClicked)
            o["disableScrollEntirely"] = jsonBool(r.disableScrollEntirely)
            o["blockHorizontalScroll"] = jsonBool(r.blockHorizontalScroll)
            o["momentumEnabled"] = jsonBool(r.momentumEnabled)
            o["axisLock"] = jsonBool(r.axisLock)
            o["deadZone"] = jsonDouble(r.deadZone)
            appRules.append(.object(o))
        }
        return .object(["base": .object(base), "appRules": .array(appRules)])
    }

    static func readEvent(_ f: Fields) throws -> ScrollEvent {
        var wheel: WheelSettings? = nil
        if let w = f.value("wheel"), !w.isNull {
            let g = try Fields(w, "\(f.context).wheel", allowed: wheelKeys)
            let d = WheelSettings()
            wheel = try WheelSettings(reverse: g.bool("reverse", d.reverse),
                                      blockWhilePressed: g.bool("blockWhilePressed", d.blockWhilePressed),
                                      smooth: g.bool("smooth", d.smooth),
                                      speed: g.double("speed", d.speed))
        }
        let phase = try Names.phase(named: f.string("phase", "none"), f.context)
        let momentum = try Names.momentum(named: f.string("momentum", "none"), f.context)
        let modifiers = try ModifierCombo(rawValue: f.int("modifiers", 0))
        return try ScrollEvent(timestamp: f.double("t", nil),
                               fromMagicMouse: f.bool("fromMagicMouse", true),
                               isContinuous: f.bool("isContinuous", true),
                               phase: phase,
                               momentum: momentum,
                               deltaX: f.double("deltaX", 0),
                               deltaY: f.double("deltaY", 0),
                               modifiers: modifiers,
                               fromWheelMouse: f.bool("fromWheelMouse", false),
                               wheel: wheel)
    }

    static func eventJSON(_ e: ScrollEvent) -> TraceJSON {
        var o: [String: TraceJSON] = [:]
        o["op"] = .string("scroll")
        o["t"] = .double(e.timestamp)
        o["fromMagicMouse"] = .bool(e.fromMagicMouse)
        o["fromWheelMouse"] = .bool(e.fromWheelMouse)
        o["isContinuous"] = .bool(e.isContinuous)
        o["phase"] = .string(Names.phase(e.phase))
        o["momentum"] = .string(Names.momentum(e.momentum))
        o["deltaX"] = .double(e.deltaX)
        o["deltaY"] = .double(e.deltaY)
        o["modifiers"] = .int(e.modifiers.rawValue)
        if let w = e.wheel {
            var g: [String: TraceJSON] = [:]
            g["reverse"] = .bool(w.reverse)
            g["blockWhilePressed"] = .bool(w.blockWhilePressed)
            g["smooth"] = .bool(w.smooth)
            g["speed"] = .double(w.speed)
            o["wheel"] = .object(g)
        } else {
            o["wheel"] = TraceJSON.null
        }
        return .object(o)
    }

    static func decisionJSON(_ decision: ScrollDecision) -> TraceJSON {
        var o: [String: TraceJSON] = [:]
        switch decision {
        case .pass:
            o["kind"] = .string("pass")
        case .drop:
            o["kind"] = .string("drop")
        case .rewrite(let r):
            o["kind"] = .string("rewrite")
            o["phase"] = .string(Names.phase(r.phase))
            o["momentum"] = .string(Names.momentum(r.momentum))
            o["zeroX"] = .bool(r.zeroX)
            o["zeroY"] = .bool(r.zeroY)
            o["swapAxes"] = .bool(r.swapAxes)
            o["invert"] = .bool(r.invert)
            o["addModifiers"] = .int(r.addModifiers.rawValue)
            o["removeModifiers"] = .int(r.removeModifiers.rawValue)
        case .smooth(let dx, let dy):
            o["kind"] = .string("smooth")
            o["dx"] = .double(dx)
            o["dy"] = .double(dy)
        }
        return .object(o)
    }

    static func run(config: TraceJSON?, steps: [TraceJSON]) throws -> TraceRun {
        let rules = try readRules(config)
        let blocker = ScrollBlocker(rules: rules)
        // Steps without a time (setActiveApp, liftBlockingImmediately) report the blocking
        // state at the time of the step before them, 0 at the start.
        var now = 0.0
        var stepsOut: [TraceJSON] = []
        var outputs: [TraceJSON] = []
        for (i, step) in steps.enumerated() {
            let context = "steps[\(i)]"
            let op = try opName(step, context)
            var decision = TraceJSON.null
            var interruptsGlide = TraceJSON.null
            switch op {
            case "setActiveApp":
                let f = try Fields(step, context, allowed: ["op", "bundleID"])
                let bundleID = try f.optionalString("bundleID")
                blocker.setActiveApp(bundleID: bundleID)
                stepsOut.append(.object(["op": .string(op), "bundleID": jsonString(bundleID)]))
            case "magicMouseButton", "wheelMouseButton":
                let f = try Fields(step, context, allowed: ["op", "button", "isDown", "t"])
                let button = try f.int("button", nil)
                let isDown = try f.bool("isDown", nil)
                now = try f.double("t", nil)
                if op == "magicMouseButton" {
                    blocker.magicMouseButton(button, isDown: isDown, at: now)
                } else {
                    blocker.wheelMouseButton(button, isDown: isDown, at: now)
                }
                stepsOut.append(.object(["op": .string(op), "button": .int(button), "isDown": .bool(isDown), "t": .double(now)]))
            case "scroll":
                let f = try Fields(step, context, allowed: scrollKeys)
                let event = try readEvent(f)
                now = event.timestamp
                decision = decisionJSON(blocker.handleScroll(event))
                interruptsGlide = .bool(ScrollBlocker.interruptsGlide(event))
                stepsOut.append(eventJSON(event))
            case "liftBlockingImmediately":
                _ = try Fields(step, context, allowed: ["op"])
                blocker.liftBlockingImmediately()
                stepsOut.append(.object(["op": .string(op)]))
            case "forceReleaseAllButtons":
                let f = try Fields(step, context, allowed: ["op", "t"])
                now = try f.double("t", nil)
                blocker.forceReleaseAllButtons(at: now)
                stepsOut.append(.object(["op": .string(op), "t": .double(now)]))
            default:
                throw TraceError("\(context): unknown op \"\(op)\" for ScrollBlocker")
            }
            var o: [String: TraceJSON] = [:]
            o["decision"] = decision
            o["interruptsGlide"] = interruptsGlide
            o["isBlocking"] = .bool(blocker.isBlocking(at: now))
            o["isBlockingWheel"] = .bool(blocker.isBlockingWheel(at: now))
            o["anyButtonDown"] = .bool(blocker.anyButtonDown)
            outputs.append(.object(o))
        }
        return (config: rulesJSON(rules), steps: stepsOut, outputs: outputs)
    }
}

// MARK: - WheelSmoother

private enum WheelSmootherTrace {
    static func run(config: TraceJSON?, steps: [TraceJSON]) throws -> TraceRun {
        let d = WheelSmoother()
        var timeConstant = d.timeConstant
        var minimumSpeed = d.minimumSpeed
        if let config = config {
            let f = try Fields(config, "config", allowed: ["timeConstant", "minimumSpeed"])
            timeConstant = try f.double("timeConstant", d.timeConstant)
            minimumSpeed = try f.double("minimumSpeed", d.minimumSpeed)
        }
        var smoother = WheelSmoother(timeConstant: timeConstant, minimumSpeed: minimumSpeed)
        var stepsOut: [TraceJSON] = []
        var outputs: [TraceJSON] = []
        for (i, step) in steps.enumerated() {
            let context = "steps[\(i)]"
            let op = try opName(step, context)
            var result = TraceJSON.null
            switch op {
            case "add":
                let f = try Fields(step, context, allowed: ["op", "dx", "dy"])
                let dx = try f.double("dx", 0)
                let dy = try f.double("dy", 0)
                smoother.add(dx: dx, dy: dy)
                stepsOut.append(.object(["op": .string(op), "dx": .double(dx), "dy": .double(dy)]))
            case "step":
                let f = try Fields(step, context, allowed: ["op", "dt"])
                let dt = try f.double("dt", nil)
                if let s = smoother.step(dt: dt) {
                    result = .object(["x": .int(s.x), "y": .int(s.y)])
                }
                stepsOut.append(.object(["op": .string(op), "dt": .double(dt)]))
            case "cancel":
                _ = try Fields(step, context, allowed: ["op"])
                smoother.cancel()
                stepsOut.append(.object(["op": .string(op)]))
            default:
                throw TraceError("\(context): unknown op \"\(op)\" for WheelSmoother")
            }
            let owed = smoother.owed
            var o: [String: TraceJSON] = [:]
            o["step"] = result
            o["isIdle"] = .bool(smoother.isIdle)
            o["owedX"] = .double(owed.x)
            o["owedY"] = .double(owed.y)
            outputs.append(.object(o))
        }
        let configOut: TraceJSON = .object(["timeConstant": .double(timeConstant), "minimumSpeed": .double(minimumSpeed)])
        return (config: configOut, steps: stepsOut, outputs: outputs)
    }
}

// MARK: - ControllerPointer

private enum ControllerPointerTrace {
    static let configKeys = [
        "pointerSpeed", "scrollSpeed", "tapToClick", "naturalScrolling", "stickClicks",
        "touchpadAspect", "touchpadGain", "touchpadScrollGain", "tapMaxDuration", "tapMaxTravel",
        "stickDeadZone", "stickMaxSpeed", "stickMaxScroll", "toggleHold", "stickClickSettle",
    ]

    static func readConfig(_ value: TraceJSON?) throws -> ControllerConfig {
        let d = ControllerConfig()
        var c = ControllerConfig()
        guard let value = value else { return c }
        let f = try Fields(value, "config", allowed: configKeys)
        c.pointerSpeed = try f.double("pointerSpeed", d.pointerSpeed)
        c.scrollSpeed = try f.double("scrollSpeed", d.scrollSpeed)
        c.tapToClick = try f.bool("tapToClick", d.tapToClick)
        c.naturalScrolling = try f.bool("naturalScrolling", d.naturalScrolling)
        c.stickClicks = try f.bool("stickClicks", d.stickClicks)
        c.touchpadAspect = try f.double("touchpadAspect", d.touchpadAspect)
        c.touchpadGain = try f.double("touchpadGain", d.touchpadGain)
        c.touchpadScrollGain = try f.double("touchpadScrollGain", d.touchpadScrollGain)
        c.tapMaxDuration = try f.double("tapMaxDuration", d.tapMaxDuration)
        c.tapMaxTravel = try f.double("tapMaxTravel", d.tapMaxTravel)
        c.stickDeadZone = try f.double("stickDeadZone", d.stickDeadZone)
        c.stickMaxSpeed = try f.double("stickMaxSpeed", d.stickMaxSpeed)
        c.stickMaxScroll = try f.double("stickMaxScroll", d.stickMaxScroll)
        c.toggleHold = try f.double("toggleHold", d.toggleHold)
        c.stickClickSettle = try f.double("stickClickSettle", d.stickClickSettle)
        return c
    }

    static func configJSON(_ c: ControllerConfig) -> TraceJSON {
        var o: [String: TraceJSON] = [:]
        o["pointerSpeed"] = .double(c.pointerSpeed)
        o["scrollSpeed"] = .double(c.scrollSpeed)
        o["tapToClick"] = .bool(c.tapToClick)
        o["naturalScrolling"] = .bool(c.naturalScrolling)
        o["stickClicks"] = .bool(c.stickClicks)
        o["touchpadAspect"] = .double(c.touchpadAspect)
        o["touchpadGain"] = .double(c.touchpadGain)
        o["touchpadScrollGain"] = .double(c.touchpadScrollGain)
        o["tapMaxDuration"] = .double(c.tapMaxDuration)
        o["tapMaxTravel"] = .double(c.tapMaxTravel)
        o["stickDeadZone"] = .double(c.stickDeadZone)
        o["stickMaxSpeed"] = .double(c.stickMaxSpeed)
        o["stickMaxScroll"] = .double(c.stickMaxScroll)
        o["toggleHold"] = .double(c.toggleHold)
        o["stickClickSettle"] = .double(c.stickClickSettle)
        return .object(o)
    }

    static func readPoint(_ f: Fields, _ key: String) throws -> TouchPoint? {
        guard let value = f.value(key), !value.isNull else { return nil }
        let g = try Fields(value, "\(f.context).\(key)", allowed: ["x", "y"])
        return try TouchPoint(x: g.double("x", nil), y: g.double("y", nil))
    }

    static func pointJSON(_ p: TouchPoint?) -> TraceJSON {
        guard let p = p else { return .null }
        return .object(["x": .double(p.x), "y": .double(p.y)])
    }

    static func actionJSON(_ action: PointerAction) -> TraceJSON {
        var o: [String: TraceJSON] = [:]
        switch action {
        case .move(let dx, let dy):
            o["kind"] = .string("move")
            o["dx"] = .double(dx)
            o["dy"] = .double(dy)
        case .scroll(let vertical, let horizontal):
            o["kind"] = .string("scroll")
            o["vertical"] = .double(vertical)
            o["horizontal"] = .double(horizontal)
        case .press(let b):
            o["kind"] = .string("press")
            o["button"] = .string(Names.pointerButton(b))
        case .release(let b):
            o["kind"] = .string("release")
            o["button"] = .string(Names.pointerButton(b))
        case .click(let b):
            o["kind"] = .string("click")
            o["button"] = .string(Names.pointerButton(b))
        case .toggle:
            o["kind"] = .string("toggle")
        }
        return .object(o)
    }

    static func run(config: TraceJSON?, steps: [TraceJSON]) throws -> TraceRun {
        let c = try readConfig(config)
        var pointer = ControllerPointer(config: c)
        var stepsOut: [TraceJSON] = []
        var outputs: [TraceJSON] = []
        for (i, step) in steps.enumerated() {
            let context = "steps[\(i)]"
            let op = try opName(step, context)
            var actions: [PointerAction] = []
            switch op {
            case "setActive":
                let f = try Fields(step, context, allowed: ["op", "active"])
                let active = try f.bool("active", nil)
                actions = pointer.setActive(active)
                stepsOut.append(.object(["op": .string(op), "active": .bool(active)]))
            case "button":
                let f = try Fields(step, context, allowed: ["op", "button", "pressed", "t"])
                let name = try f.string("button", nil)
                let which = try Names.controllerButton(named: name, context)
                let pressed = try f.bool("pressed", nil)
                let t = try f.double("t", nil)
                actions = pointer.button(which, pressed: pressed, at: t)
                stepsOut.append(.object(["op": .string(op), "button": .string(Names.controllerButton(which)),
                                         "pressed": .bool(pressed), "t": .double(t)]))
            case "touchpad":
                let f = try Fields(step, context, allowed: ["op", "primary", "secondary", "t"])
                let primary = try readPoint(f, "primary")
                let secondary = try readPoint(f, "secondary")
                let t = try f.double("t", nil)
                actions = pointer.touchpad(primary: primary, secondary: secondary, at: t)
                stepsOut.append(.object(["op": .string(op), "primary": pointJSON(primary),
                                         "secondary": pointJSON(secondary), "t": .double(t)]))
            case "sticks":
                let f = try Fields(step, context, allowed: ["op", "left", "right"])
                let left = try readPoint(f, "left") ?? TouchPoint(x: 0, y: 0)
                let right = try readPoint(f, "right") ?? TouchPoint(x: 0, y: 0)
                pointer.sticks(left: left, right: right)
                stepsOut.append(.object(["op": .string(op), "left": pointJSON(left), "right": pointJSON(right)]))
            case "tick":
                let f = try Fields(step, context, allowed: ["op", "dt", "t"])
                let dt = try f.double("dt", nil)
                let t = try f.double("t", nil)
                actions = pointer.tick(dt: dt, at: t)
                stepsOut.append(.object(["op": .string(op), "dt": .double(dt), "t": .double(t)]))
            default:
                throw TraceError("\(context): unknown op \"\(op)\" for ControllerPointer")
            }
            var o: [String: TraceJSON] = [:]
            o["actions"] = .array(actions.map { actionJSON($0) })
            o["isActive"] = .bool(pointer.isActive)
            o["needsTicks"] = .bool(pointer.needsTicks)
            o["leftHeld"] = .bool(pointer.isHeld(.left))
            o["rightHeld"] = .bool(pointer.isHeld(.right))
            outputs.append(.object(o))
        }
        return (config: configJSON(c), steps: stepsOut, outputs: outputs)
    }
}

// MARK: - TapRecognizer

private enum TapRecognizerTrace {
    static let configKeys = [
        "enabled", "sensitivity", "maxDuration", "maxMovement", "minPeakSize", "scrollCooldown",
        "buttonCooldown", "scrollActivityThreshold", "tapZoneEnabled", "tapZoneDepth",
        "tapZoneEdgeMargin", "frontIsHighY", "tapRightClick", "rightClickMode", "rightSideStart",
        "doubleTapWindow", "doubleTapMaxDistance", "tapAndDrag", "dragWindow", "dragMaxDistance",
        "dragSwipeCancelDistance", "twoFingerDrag", "twoFingerPairWindow", "twoFingerLongPress",
        "scrollBurstWindow", "scrollBurstCancelThreshold", "pressQuietPeriod", "pairArmTTL",
        "pairRearmCooldown",
    ]

    /// Sensitivity goes first: setting it retunes maxDuration, maxMovement and scrollCooldown,
    /// which the fields after it may then set on their own.
    static func readConfig(_ value: TraceJSON?) throws -> TapConfig {
        var c = TapConfig()
        guard let value = value else { return c }
        let f = try Fields(value, "config", allowed: configKeys)
        let sensitivity = try f.double("sensitivity", TapConfig().sensitivity)
        c.sensitivity = sensitivity
        let d = c
        c.enabled = try f.bool("enabled", d.enabled)
        c.maxDuration = try f.double("maxDuration", d.maxDuration)
        c.maxMovement = try f.double("maxMovement", d.maxMovement)
        c.minPeakSize = try f.double("minPeakSize", d.minPeakSize)
        c.scrollCooldown = try f.double("scrollCooldown", d.scrollCooldown)
        c.buttonCooldown = try f.double("buttonCooldown", d.buttonCooldown)
        c.scrollActivityThreshold = try f.double("scrollActivityThreshold", d.scrollActivityThreshold)
        c.tapZoneEnabled = try f.bool("tapZoneEnabled", d.tapZoneEnabled)
        c.tapZoneDepth = try f.double("tapZoneDepth", d.tapZoneDepth)
        c.tapZoneEdgeMargin = try f.double("tapZoneEdgeMargin", d.tapZoneEdgeMargin)
        c.frontIsHighY = try f.bool("frontIsHighY", d.frontIsHighY)
        c.tapRightClick = try f.bool("tapRightClick", d.tapRightClick)
        let modeName = try f.string("rightClickMode", d.rightClickMode.rawValue)
        guard let mode = RightClickMode(rawValue: modeName) else {
            throw TraceError("config: unknown rightClickMode \"\(modeName)\"")
        }
        c.rightClickMode = mode
        c.rightSideStart = try f.double("rightSideStart", d.rightSideStart)
        c.doubleTapWindow = try f.double("doubleTapWindow", d.doubleTapWindow)
        c.doubleTapMaxDistance = try f.double("doubleTapMaxDistance", d.doubleTapMaxDistance)
        c.tapAndDrag = try f.bool("tapAndDrag", d.tapAndDrag)
        c.dragWindow = try f.double("dragWindow", d.dragWindow)
        c.dragMaxDistance = try f.double("dragMaxDistance", d.dragMaxDistance)
        c.dragSwipeCancelDistance = try f.double("dragSwipeCancelDistance", d.dragSwipeCancelDistance)
        c.twoFingerDrag = try f.bool("twoFingerDrag", d.twoFingerDrag)
        c.twoFingerPairWindow = try f.double("twoFingerPairWindow", d.twoFingerPairWindow)
        c.twoFingerLongPress = try f.double("twoFingerLongPress", d.twoFingerLongPress)
        c.scrollBurstWindow = try f.double("scrollBurstWindow", d.scrollBurstWindow)
        c.scrollBurstCancelThreshold = try f.double("scrollBurstCancelThreshold", d.scrollBurstCancelThreshold)
        c.pressQuietPeriod = try f.double("pressQuietPeriod", d.pressQuietPeriod)
        c.pairArmTTL = try f.double("pairArmTTL", d.pairArmTTL)
        c.pairRearmCooldown = try f.double("pairRearmCooldown", d.pairRearmCooldown)
        return c
    }

    static func configJSON(_ c: TapConfig) -> TraceJSON {
        var o: [String: TraceJSON] = [:]
        o["enabled"] = .bool(c.enabled)
        o["sensitivity"] = .double(c.sensitivity)
        o["maxDuration"] = .double(c.maxDuration)
        o["maxMovement"] = .double(c.maxMovement)
        o["minPeakSize"] = .double(c.minPeakSize)
        o["scrollCooldown"] = .double(c.scrollCooldown)
        o["buttonCooldown"] = .double(c.buttonCooldown)
        o["scrollActivityThreshold"] = .double(c.scrollActivityThreshold)
        o["tapZoneEnabled"] = .bool(c.tapZoneEnabled)
        o["tapZoneDepth"] = .double(c.tapZoneDepth)
        o["tapZoneEdgeMargin"] = .double(c.tapZoneEdgeMargin)
        o["frontIsHighY"] = .bool(c.frontIsHighY)
        o["tapRightClick"] = .bool(c.tapRightClick)
        o["rightClickMode"] = .string(c.rightClickMode.rawValue)
        o["rightSideStart"] = .double(c.rightSideStart)
        o["doubleTapWindow"] = .double(c.doubleTapWindow)
        o["doubleTapMaxDistance"] = .double(c.doubleTapMaxDistance)
        o["tapAndDrag"] = .bool(c.tapAndDrag)
        o["dragWindow"] = .double(c.dragWindow)
        o["dragMaxDistance"] = .double(c.dragMaxDistance)
        o["dragSwipeCancelDistance"] = .double(c.dragSwipeCancelDistance)
        o["twoFingerDrag"] = .bool(c.twoFingerDrag)
        o["twoFingerPairWindow"] = .double(c.twoFingerPairWindow)
        o["twoFingerLongPress"] = .double(c.twoFingerLongPress)
        o["scrollBurstWindow"] = .double(c.scrollBurstWindow)
        o["scrollBurstCancelThreshold"] = .double(c.scrollBurstCancelThreshold)
        o["pressQuietPeriod"] = .double(c.pressQuietPeriod)
        o["pairArmTTL"] = .double(c.pairArmTTL)
        o["pairRearmCooldown"] = .double(c.pairRearmCooldown)
        return .object(o)
    }

    static func run(config: TraceJSON?, steps: [TraceJSON]) throws -> TraceRun {
        let c = try readConfig(config)
        let recognizer = TapRecognizer(config: c)
        var stepsOut: [TraceJSON] = []
        var outputs: [TraceJSON] = []
        for (i, step) in steps.enumerated() {
            let context = "steps[\(i)]"
            let op = try opName(step, context)
            var events: [TapEvent] = []
            switch op {
            case "physicalButton":
                let f = try Fields(step, context, allowed: ["op", "isDown", "t"])
                let isDown = try f.bool("isDown", nil)
                let t = try f.double("t", nil)
                events = recognizer.physicalButton(isDown: isDown, at: t)
                stepsOut.append(.object(["op": .string(op), "isDown": .bool(isDown), "t": .double(t)]))
            case "noteScroll":
                let f = try Fields(step, context, allowed: ["op", "deltaMagnitude", "t"])
                let magnitude = try f.double("deltaMagnitude", nil)
                let t = try f.double("t", nil)
                events = recognizer.noteScroll(deltaMagnitude: magnitude, at: t)
                stepsOut.append(.object(["op": .string(op), "deltaMagnitude": .double(magnitude), "t": .double(t)]))
            case "handleFrame":
                let f = try Fields(step, context, allowed: ["op", "touches", "t", "cursorMoving"])
                let items = try f.array("touches")
                var touches: [TouchSample] = []
                var touchesOut: [TraceJSON] = []
                for (j, item) in items.enumerated() {
                    let g = try Fields(item, "\(context).touches[\(j)]", allowed: ["id", "x", "y", "size"])
                    let touch = try TouchSample(id: g.int("id", nil), x: g.double("x", nil),
                                                y: g.double("y", nil), size: g.double("size", nil))
                    touches.append(touch)
                    touchesOut.append(.object(["id": .int(touch.id), "x": .double(touch.x),
                                               "y": .double(touch.y), "size": .double(touch.size)]))
                }
                let t = try f.double("t", nil)
                let cursorMoving = try f.bool("cursorMoving", false)
                events = recognizer.handleFrame(touches, at: t, cursorMoving: cursorMoving)
                stepsOut.append(.object(["op": .string(op), "touches": .array(touchesOut), "t": .double(t),
                                         "cursorMoving": .bool(cursorMoving)]))
            case "disarmDrag":
                _ = try Fields(step, context, allowed: ["op"])
                recognizer.disarmDrag()
                stepsOut.append(.object(["op": .string(op)]))
            case "reset":
                _ = try Fields(step, context, allowed: ["op"])
                recognizer.reset()
                stepsOut.append(.object(["op": .string(op)]))
            default:
                throw TraceError("\(context): unknown op \"\(op)\" for TapRecognizer")
            }
            var o: [String: TraceJSON] = [:]
            o["events"] = .array(events.map { TraceJSON.string(Names.tapEvent($0)) })
            o["tapCount"] = .int(recognizer.tapCount)
            o["dragActive"] = .bool(recognizer.dragActive)
            o["dragArmIsPair"] = .bool(recognizer.dragArmIsPair)
            o["lastRejection"] = jsonString(recognizer.lastRejection?.rawValue)
            outputs.append(.object(o))
        }
        return (config: configJSON(c), steps: stepsOut, outputs: outputs)
    }
}

// MARK: - Generated sequences

/// SplitMix64 (Steele, Lea and Flood), written out so the sequences never depend on the
/// platform's random number generator.
private struct SplitMix64 {
    private var state: UInt64

    init(seed: UInt64) {
        state = seed
    }

    mutating func next() -> UInt64 {
        state = state &+ 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }

    /// A whole number in 0..<n.
    mutating func below(_ n: Int) -> Int {
        precondition(n > 0)
        return Int(next() % UInt64(n))
    }

    mutating func chance(_ p: Double) -> Bool {
        let unit = Double(next() >> 11) / 9_007_199_254_740_992.0
        return unit < p
    }

    mutating func pick<T>(_ items: [T]) -> T {
        items[below(items.count)]
    }
}

private let generatedDescription = "Generated from a fixed seed: random settings and a long, plausible but unplanned sequence of inputs."

/// Times are whole milliseconds and distances whole hundredths or thousandths, divided once,
/// so the files show 0.123 rather than an accumulated 0.12300000000000001.
private func thousandths(_ n: Int) -> Double {
    Double(n) / 1000
}

private enum ScrollBlockerGenerator {
    static let actionNames = ["normal", "horizontal", "invert", "zoomCommand", "zoomControl", "block"]
    static let apps = ["com.apple.Safari", "com.figma.Desktop", "COM.FIGMA.DESKTOP", "com.apple.dt.Xcode", "org.example.Other"]

    static func make(_ rng: inout SplitMix64) -> Scenario {
        var base: [String: TraceJSON] = [:]
        base["blockScrollWhileClicked"] = .bool(rng.chance(0.85))
        base["releaseGrace"] = .double(rng.pick([0.0, 0.1, 0.2, 0.3]))
        base["deadZone"] = .double(rng.pick([0.0, 0.0, 5.0, 10.0, 20.0]))
        base["axisLock"] = .bool(rng.chance(0.3))
        base["axisLockThreshold"] = .double(rng.pick([5.0, 10.0, 15.0]))
        base["axisLockRatio"] = .double(rng.pick([1.2, 1.4, 2.0]))
        base["momentumEnabled"] = .bool(rng.chance(0.8))
        base["blockHorizontalScroll"] = .bool(rng.chance(0.2))
        base["disableScrollEntirely"] = .bool(rng.chance(0.08))
        var actions: [TraceJSON] = []
        var used: [Int] = []
        let actionCount = rng.below(4)
        for _ in 0..<actionCount {
            let modifiers = 1 + rng.below(15)
            let name = rng.pick(actionNames)
            if used.contains(modifiers) { continue }
            used.append(modifiers)
            actions.append(.object(["modifiers": .int(modifiers), "action": .string(name)]))
        }
        base["modifierActions"] = .array(actions)
        base["reverseMagicMouseScroll"] = .bool(rng.chance(0.25))
        base["reverseWheelScroll"] = .bool(rng.chance(0.3))
        base["blockScrollWhileWheelPressed"] = .bool(rng.chance(0.4))
        base["smoothWheelScroll"] = .bool(rng.chance(0.4))
        base["wheelScrollSpeed"] = .double(rng.pick([1.0, 2.0, 3.0, 5.0]))

        var appRules: [TraceJSON] = []
        let ruleCount = rng.below(3)
        for r in 0..<ruleCount {
            var rule: [String: TraceJSON] = [:]
            rule["bundleID"] = .string(rng.pick(["com.figma.Desktop", "com.apple.dt.Xcode", "com.apple.Safari"]))
            rule["name"] = .string("Rule \(r + 1)")
            rule["enabled"] = .bool(rng.chance(0.85))
            if rng.chance(0.4) { rule["blockScrollWhileClicked"] = .bool(rng.chance(0.5)) }
            if rng.chance(0.25) { rule["disableScrollEntirely"] = .bool(rng.chance(0.5)) }
            if rng.chance(0.3) { rule["blockHorizontalScroll"] = .bool(rng.chance(0.5)) }
            if rng.chance(0.3) { rule["momentumEnabled"] = .bool(rng.chance(0.5)) }
            if rng.chance(0.3) { rule["axisLock"] = .bool(rng.chance(0.5)) }
            if rng.chance(0.3) { rule["deadZone"] = .double(rng.pick([0.0, 8.0, 15.0])) }
            appRules.append(.object(rule))
        }

        var steps: [TraceJSON] = []
        var ms = 0
        var gesture: [(phase: String, momentum: String)] = []
        var magicDown: [Int] = []
        var wheelDown = false
        let count = 50 + rng.below(151)
        for _ in 0..<count {
            let longPause = rng.chance(0.08)
            ms += longPause ? 300 + rng.below(1200) : rng.below(41)
            let t = thousandths(ms)
            let roll = rng.below(100)
            if roll < 55 {
                if gesture.isEmpty { gesture = makeGesture(&rng) }
                let next = gesture.removeFirst()
                steps.append(gestureEvent(&rng, t: t, phase: next.phase, momentum: next.momentum, fromMagicMouse: true))
            } else if roll < 70 {
                steps.append(wheelTick(&rng, t: t))
            } else if roll < 80 {
                let button = rng.below(2)
                let spurious = rng.chance(0.05)
                var isDown = !magicDown.contains(button)
                if spurious { isDown = !isDown }
                if isDown {
                    if !magicDown.contains(button) { magicDown.append(button) }
                } else {
                    magicDown.removeAll { $0 == button }
                }
                steps.append(.object(["op": .string("magicMouseButton"), "button": .int(button),
                                      "isDown": .bool(isDown), "t": .double(t)]))
            } else if roll < 86 {
                let button = rng.pick([2, 2, 2, 0, 1])
                let isDown: Bool
                if button == 2 {
                    isDown = !wheelDown
                    wheelDown = isDown
                } else {
                    isDown = rng.chance(0.5)
                }
                steps.append(.object(["op": .string("wheelMouseButton"), "button": .int(button),
                                      "isDown": .bool(isDown), "t": .double(t)]))
            } else if roll < 90 {
                let app: TraceJSON = rng.chance(0.15) ? .null : .string(rng.pick(apps))
                steps.append(.object(["op": .string("setActiveApp"), "bundleID": app]))
            } else if roll < 93 {
                magicDown = []
                wheelDown = false
                steps.append(.object(["op": .string("forceReleaseAllButtons"), "t": .double(t)]))
            } else if roll < 95 {
                magicDown = []
                steps.append(.object(["op": .string("liftBlockingImmediately")]))
            } else {
                // A trackpad or another continuous device: never the Magic Mouse's business.
                let phase = rng.pick(["began", "changed", "ended", "none"])
                steps.append(gestureEvent(&rng, t: t, phase: phase, momentum: "none", fromMagicMouse: false))
            }
        }
        var config: [String: TraceJSON] = [:]
        config["base"] = .object(base)
        config["appRules"] = .array(appRules)
        return Scenario(component: "ScrollBlocker", description: generatedDescription,
                        config: .object(config), steps: steps)
    }

    /// One gesture's events in a plausible order, with the odd one missing (a lost ended, a
    /// gesture joined mid-flight, a momentum tail that never ends).
    static func makeGesture(_ rng: inout SplitMix64) -> [(phase: String, momentum: String)] {
        var sequence: [(phase: String, momentum: String)] = []
        if rng.chance(0.1) {
            let ticks = 1 + rng.below(4)
            for _ in 0..<ticks { sequence.append((phase: "none", momentum: "none")) }
            return sequence
        }
        if rng.chance(0.4) { sequence.append((phase: "mayBegin", momentum: "none")) }
        if rng.chance(0.93) { sequence.append((phase: "began", momentum: "none")) }
        let changes = 1 + rng.below(8)
        for _ in 0..<changes { sequence.append((phase: "changed", momentum: "none")) }
        if rng.chance(0.92) {
            let ending = rng.chance(0.9) ? "ended" : "cancelled"
            sequence.append((phase: ending, momentum: "none"))
        }
        if rng.chance(0.5) {
            sequence.append((phase: "none", momentum: "began"))
            let coasting = rng.below(7)
            for _ in 0..<coasting { sequence.append((phase: "none", momentum: "continued")) }
            if rng.chance(0.92) { sequence.append((phase: "none", momentum: "ended")) }
        }
        return sequence
    }

    static func gestureEvent(_ rng: inout SplitMix64, t: Double, phase: String, momentum: String,
                             fromMagicMouse: Bool) -> TraceJSON {
        let ending = phase == "ended" || phase == "cancelled" || momentum == "ended"
        let stillEnding = rng.chance(0.8)
        let quiet = phase == "mayBegin" || (ending && stillEnding)
        var dx = 0.0
        var dy = 0.0
        if !quiet {
            dy = Double(rng.below(41) - 20) / 2
            let diagonal = rng.chance(0.4)
            dx = diagonal ? Double(rng.below(41) - 20) / 2 : 0
        }
        let plain = rng.chance(0.8)
        let modifiers = plain ? 0 : 1 + rng.below(15)
        var o: [String: TraceJSON] = [:]
        o["op"] = .string("scroll")
        o["t"] = .double(t)
        o["fromMagicMouse"] = .bool(fromMagicMouse)
        o["fromWheelMouse"] = .bool(false)
        o["isContinuous"] = .bool(true)
        o["phase"] = .string(phase)
        o["momentum"] = .string(momentum)
        o["deltaX"] = .double(dx)
        o["deltaY"] = .double(dy)
        o["modifiers"] = .int(modifiers)
        o["wheel"] = TraceJSON.null
        return .object(o)
    }

    static func wheelTick(_ rng: inout SplitMix64, t: Double) -> TraceJSON {
        let notch = rng.pick([-1.0, -3.0, -10.0, 1.0, 4.0, 10.0, -42.0, 57.0])
        let sideways = rng.chance(0.15)
        let plain = rng.chance(0.75)
        let modifiers = plain ? 0 : rng.pick([1, 2, 4, 8, 9, 12])
        var o: [String: TraceJSON] = [:]
        o["op"] = .string("scroll")
        o["t"] = .double(t)
        o["fromMagicMouse"] = .bool(false)
        o["fromWheelMouse"] = .bool(true)
        o["isContinuous"] = .bool(false)
        o["phase"] = .string("none")
        o["momentum"] = .string("none")
        o["deltaX"] = .double(sideways ? notch : 0)
        o["deltaY"] = .double(sideways ? 0 : notch)
        o["modifiers"] = .int(modifiers)
        if rng.chance(0.5) {
            o["wheel"] = TraceJSON.null
        } else {
            var w: [String: TraceJSON] = [:]
            w["reverse"] = .bool(rng.chance(0.5))
            w["blockWhilePressed"] = .bool(rng.chance(0.5))
            w["smooth"] = .bool(rng.chance(0.6))
            w["speed"] = .double(rng.pick([1.0, 2.0, 3.0, 5.0]))
            o["wheel"] = .object(w)
        }
        return .object(o)
    }
}

private enum WheelSmootherGenerator {
    static func make(_ rng: inout SplitMix64) -> Scenario {
        var config: [String: TraceJSON] = [:]
        config["timeConstant"] = .double(rng.pick([0.0, 0.05, 0.09, 0.09, 0.15, 0.3]))
        config["minimumSpeed"] = .double(rng.pick([0.0, 50.0, 150.0, 150.0, 400.0]))
        var steps: [TraceJSON] = []
        let count = 50 + rng.below(151)
        for _ in 0..<count {
            let roll = rng.below(100)
            if roll < 22 {
                let notch = rng.pick([35.0, 105.0, 105.0, 175.0, 315.0])
                let fraction = Double(rng.below(2001) - 1000) / 100
                let whole = rng.chance(0.7)
                let amount = whole ? notch : fraction
                let negative = rng.chance(0.5)
                let sign: Double = negative ? -1 : 1
                let axis = rng.below(10)
                var dx = 0.0
                var dy = sign * amount
                if axis < 2 {
                    dx = sign * amount
                    dy = 0
                } else if axis == 2 {
                    dx = -sign * amount / 2
                }
                steps.append(.object(["op": .string("add"), "dx": .double(dx), "dy": .double(dy)]))
            } else if roll < 97 {
                let usual = rng.chance(0.85)
                let milliseconds = usual ? rng.pick([4, 7, 8, 16, 17, 33]) : rng.pick([0, 100, 250])
                steps.append(.object(["op": .string("step"), "dt": .double(thousandths(milliseconds))]))
            } else {
                steps.append(.object(["op": .string("cancel")]))
            }
        }
        return Scenario(component: "WheelSmoother", description: generatedDescription,
                        config: .object(config), steps: steps)
    }
}

private enum ControllerPointerGenerator {
    static let buttons = ["south", "east", "touchpad", "menu", "options", "leftStick", "rightStick"]

    static func make(_ rng: inout SplitMix64) -> Scenario {
        var config: [String: TraceJSON] = [:]
        config["pointerSpeed"] = .double(rng.pick([-1.0, -0.5, 0.0, 0.0, 0.5, 1.0]))
        config["scrollSpeed"] = .double(rng.pick([-1.0, -0.5, 0.0, 0.0, 0.5, 1.0]))
        config["tapToClick"] = .bool(rng.chance(0.8))
        config["naturalScrolling"] = .bool(rng.chance(0.7))
        config["stickClicks"] = .bool(rng.chance(0.8))
        config["stickDeadZone"] = .double(rng.pick([0.1, 0.15, 0.25]))
        config["toggleHold"] = .double(rng.pick([0.3, 0.5]))

        var steps: [TraceJSON] = []
        var ms = 0
        // Fingers on the pad in thousandths (-1000...1000); the first is the primary slot unless
        // only the second finger is left.
        var fingers: [(x: Int, y: Int)] = []
        var secondaryOnly = false
        var held: [String] = []
        if rng.chance(0.8) {
            steps.append(.object(["op": .string("setActive"), "active": .bool(true)]))
        }
        let count = 50 + rng.below(151)
        while steps.count < count {
            let longPause = rng.chance(0.05)
            let advance = longPause ? 200 + rng.below(800) : rng.below(25)
            ms += advance
            let t = thousandths(ms)
            let roll = rng.below(100)
            if roll < 40 {
                if fingers.isEmpty {
                    if rng.chance(0.6) { fingers.append(randomPoint(&rng)) }
                    if rng.chance(0.25) { fingers.append(randomPoint(&rng)) }
                    secondaryOnly = false
                } else {
                    let change = rng.below(10)
                    if change == 0 {
                        fingers.removeAll()
                    } else if change == 1 && fingers.count == 1 {
                        fingers.append(randomPoint(&rng))
                        secondaryOnly = false
                    } else if change == 2 && fingers.count == 2 {
                        fingers.removeFirst()
                        secondaryOnly = rng.chance(0.5)
                    } else {
                        let swipe = rng.chance(0.3)
                        let reach = swipe ? 60 : 8
                        let dx = rng.below(2 * reach + 1) - reach
                        let dy = rng.below(2 * reach + 1) - reach
                        for j in 0..<fingers.count {
                            fingers[j].x = clamp(fingers[j].x + dx)
                            fingers[j].y = clamp(fingers[j].y + dy)
                        }
                    }
                }
                var primary = TraceJSON.null
                var secondary = TraceJSON.null
                if fingers.count == 2 {
                    primary = pointJSON(fingers[0])
                    secondary = pointJSON(fingers[1])
                } else if fingers.count == 1 {
                    if secondaryOnly {
                        secondary = pointJSON(fingers[0])
                    } else {
                        primary = pointJSON(fingers[0])
                    }
                }
                steps.append(.object(["op": .string("touchpad"), "primary": primary, "secondary": secondary, "t": .double(t)]))
            } else if roll < 55 {
                let name = rng.pick(buttons)
                let pressed = !held.contains(name)
                if pressed { held.append(name) } else { held.removeAll { $0 == name } }
                steps.append(.object(["op": .string("button"), "button": .string(name), "pressed": .bool(pressed), "t": .double(t)]))
            } else if roll < 65 {
                let left = randomStick(&rng)
                let right = randomStick(&rng)
                steps.append(.object(["op": .string("sticks"), "left": pointJSON(left), "right": pointJSON(right)]))
            } else if roll < 95 {
                steps.append(.object(["op": .string("tick"), "dt": .double(thousandths(advance)), "t": .double(t)]))
            } else if roll < 97 {
                steps.append(.object(["op": .string("setActive"), "active": .bool(rng.chance(0.6))]))
            } else {
                // Hold the on/off chord for a while, ticking, then let go.
                steps.append(.object(["op": .string("button"), "button": .string("menu"), "pressed": .bool(true), "t": .double(t)]))
                steps.append(.object(["op": .string("button"), "button": .string("options"), "pressed": .bool(true), "t": .double(t)]))
                let ticks = 1 + rng.below(6)
                for _ in 0..<ticks {
                    let frame = 100 + rng.below(100)
                    ms += frame
                    steps.append(.object(["op": .string("tick"), "dt": .double(thousandths(frame)), "t": .double(thousandths(ms))]))
                }
                steps.append(.object(["op": .string("button"), "button": .string("menu"), "pressed": .bool(false), "t": .double(thousandths(ms))]))
                steps.append(.object(["op": .string("button"), "button": .string("options"), "pressed": .bool(false), "t": .double(thousandths(ms))]))
                held.removeAll { $0 == "menu" || $0 == "options" }
            }
        }
        return Scenario(component: "ControllerPointer", description: generatedDescription,
                        config: .object(config), steps: steps)
    }

    static func clamp(_ v: Int) -> Int {
        min(max(v, -1000), 1000)
    }

    static func randomPoint(_ rng: inout SplitMix64) -> (x: Int, y: Int) {
        let x = rng.below(2001) - 1000
        let y = rng.below(2001) - 1000
        return (x: x, y: y)
    }

    /// Centred, resting inside the dead zone, or pushed somewhere.
    static func randomStick(_ rng: inout SplitMix64) -> (x: Int, y: Int) {
        let kind = rng.below(10)
        if kind < 3 { return (x: 0, y: 0) }
        if kind < 5 {
            let x = rng.below(161) - 80
            let y = rng.below(161) - 80
            return (x: x, y: y)
        }
        return randomPoint(&rng)
    }

    static func pointJSON(_ p: (x: Int, y: Int)) -> TraceJSON {
        .object(["x": .double(thousandths(p.x)), "y": .double(thousandths(p.y))])
    }
}

private enum TapRecognizerGenerator {
    static func make(_ rng: inout SplitMix64) -> Scenario {
        var config: [String: TraceJSON] = [:]
        config["enabled"] = .bool(rng.chance(0.9))
        config["sensitivity"] = .double(rng.pick([0.0, 0.25, 0.5, 0.75, 1.0]))
        config["tapRightClick"] = .bool(rng.chance(0.4))
        config["rightClickMode"] = .string(rng.chance(0.5) ? "rightSide" : "doubleTap")
        config["tapAndDrag"] = .bool(rng.chance(0.5))
        config["twoFingerDrag"] = .bool(rng.chance(0.4))
        config["tapZoneEnabled"] = .bool(rng.chance(0.3))
        config["tapZoneDepth"] = .double(rng.pick([0.25, 0.5, 0.75]))
        config["frontIsHighY"] = .bool(rng.chance(0.7))

        var steps: [TraceJSON] = []
        var ms = 0
        var nextID = 1
        // Touches on the surface: x and y in thousandths (0...1000), size in hundredths.
        var touches: [(id: Int, x: Int, y: Int, size: Int)] = []
        var lastLift = (x: 500, y: 500)
        var buttonDown = false
        let count = 50 + rng.below(151)
        for _ in 0..<count {
            let longPause = rng.chance(0.1)
            ms += longPause ? 150 + rng.below(500) : 5 + rng.below(30)
            let t = thousandths(ms)
            let roll = rng.below(100)
            if roll < 70 {
                let change = rng.below(10)
                if change < 2 && touches.count < 3 {
                    // A finger lands, often where the last one lifted (a tap-and-drag follow-up).
                    let nearLast = rng.chance(0.5)
                    var x = rng.below(1001)
                    var y = rng.below(1001)
                    if nearLast {
                        x = min(max(lastLift.x + rng.below(101) - 50, 0), 1000)
                        y = min(max(lastLift.y + rng.below(101) - 50, 0), 1000)
                    }
                    let size = rng.pick([2, 30, 60, 60, 90, 140])
                    touches.append((id: nextID, x: x, y: y, size: size))
                    nextID += 1
                } else if change == 2 && touches.isEmpty {
                    // Two fingers landing together (a two-finger drag).
                    let x = 200 + rng.below(500)
                    let y = rng.below(1001)
                    touches.append((id: nextID, x: x, y: y, size: 60))
                    touches.append((id: nextID + 1, x: x + 160, y: y, size: 60))
                    nextID += 2
                } else if change < 6 && !touches.isEmpty {
                    let index = rng.below(touches.count)
                    lastLift = (x: touches[index].x, y: touches[index].y)
                    touches.remove(at: index)
                } else {
                    let swipe = rng.chance(0.2)
                    let reach = swipe ? 80 : 6
                    for j in 0..<touches.count {
                        touches[j].x = min(max(touches[j].x + rng.below(2 * reach + 1) - reach, 0), 1000)
                        touches[j].y = min(max(touches[j].y + rng.below(2 * reach + 1) - reach, 0), 1000)
                        if rng.chance(0.2) { touches[j].size = rng.pick([30, 60, 90, 140]) }
                    }
                }
                var frame: [TraceJSON] = []
                for touch in touches {
                    frame.append(.object(["id": .int(touch.id), "x": .double(thousandths(touch.x)),
                                          "y": .double(thousandths(touch.y)), "size": .double(Double(touch.size) / 100)]))
                }
                let cursorMoving = rng.chance(0.3)
                steps.append(.object(["op": .string("handleFrame"), "touches": .array(frame), "t": .double(t),
                                      "cursorMoving": .bool(cursorMoving)]))
            } else if roll < 82 {
                let magnitude = rng.pick([0.0, 1.5, 4.0, 9.0, 20.0, 45.0])
                steps.append(.object(["op": .string("noteScroll"), "deltaMagnitude": .double(magnitude), "t": .double(t)]))
            } else if roll < 92 {
                buttonDown.toggle()
                steps.append(.object(["op": .string("physicalButton"), "isDown": .bool(buttonDown), "t": .double(t)]))
            } else if roll < 97 {
                steps.append(.object(["op": .string("disarmDrag")]))
            } else {
                steps.append(.object(["op": .string("reset")]))
            }
        }
        return Scenario(component: "TapRecognizer", description: generatedDescription,
                        config: .object(config), steps: steps)
    }
}
