import AppKit

// CLI diagnostics — handy when checking a new macOS build or debugging remotely.
let args = CommandLine.arguments
if args.count >= 2 {
    switch args[1] {
    case "--status":
        // Ask the running app for a fresh snapshot (it doesn't write one on its own), then
        // watch the file's modification date so a stale file from a dead app can't answer.
        let asked = Date()
        let deadline = Date().addingTimeInterval(3)
        while Date() < deadline {
            // Re-post each round so an app that finishes launching mid-wait still hears us.
            DistributedNotificationCenter.default().postNotificationName(
                StatusReporter.refreshRequest, object: nil, userInfo: nil, deliverImmediately: true)
            usleep(150_000)
            if let updated = StatusReporter.lastUpdated(), updated >= asked,
               let json = StatusReporter.read() {
                print(json)
                exit(0)
            }
        }
        print("no response from CalmMouse — is it running?")
        exit(1)

    case "--battery":
        if let r = BatteryMonitor.read() {
            print("\(r.deviceName): \(r.percent)%")
        } else {
            print("no Magic Mouse found")
            exit(1)
        }
        exit(0)

    case "--help", "-h":
        print("""
        CalmMouse — Magic Mouse UX fixes.
          (no args)          run the menu bar app
          --status           print the running app's runtime state
          --battery          print the Magic Mouse battery level
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
