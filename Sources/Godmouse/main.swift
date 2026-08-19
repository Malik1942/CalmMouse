import AppKit

// CLI diagnostics — handy when checking a new macOS build or debugging remotely.
let args = CommandLine.arguments
if args.count >= 2 {
    switch args[1] {
    case "--status":
        // Runtime state of the *running* app, written to Application Support every 2s.
        if let json = StatusReporter.read() {
            print(json)
        } else {
            print("no status file — is Godmouse running?")
            exit(1)
        }
        exit(0)

    case "--battery":
        if let r = BatteryMonitor.read() {
            print("\(r.deviceName): \(r.percent)%")
        } else {
            print("no Magic Mouse found")
            exit(1)
        }
        exit(0)

    case "--resolve":
        // Resolve an IORegistry entry ID the way the event tap does (CGEvent field 87).
        guard args.count >= 3 else { print("usage: Godmouse --resolve <id>"); exit(2) }
        let raw = args[2]
        guard let id = raw.hasPrefix("0x") ? UInt64(raw.dropFirst(2), radix: 16) : UInt64(raw) else {
            print("bad id"); exit(2)
        }
        if let info = DeviceIdentifier().info(forSenderID: id) {
            print("sender 0x\(String(id, radix: 16)) → product=\"\(info.product)\" vendor=\(info.vendorID) productID=\(info.productID) transport=\(info.transport) isMagicMouse=\(info.isMagicMouse)")
        } else {
            print("sender 0x\(String(id, radix: 16)) → unresolved")
        }
        exit(0)

    case "--help", "-h":
        print("""
        Godmouse — Magic Mouse UX fixes.
          (no args)          run the menu bar app
          --status           print the running app's runtime state
          --battery          print the Magic Mouse battery level
          --resolve <id>     resolve an IORegistry entry ID to a device
        """)
        exit(0)

    default:
        break
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
