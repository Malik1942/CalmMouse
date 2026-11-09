import Foundation
import CoreGraphics
import CoreVideo
import CalmMouseCore

/// Plays a `WheelSmoother` out on the display's own frame clock, posting one small scroll per
/// frame in place of the wheel tick the event tap swallowed.
///
/// The frame clock is a `CVDisplayLink`, so the steps land in step with the screen's refresh
/// (60 or 120 times a second) rather than a timer's guess at it. It only runs while a glide
/// is in progress.
final class WheelSmoothDriver {
    /// Marks the events posted here, for anything that needs to tell them from hardware.
    static let eventTag: Int64 = 0x434D_534D   // "CMSM"

    private let lock = NSLock()
    private var smoother = WheelSmoother()
    private var link: CVDisplayLink?
    private var running = false
    private var lastFrame: TimeInterval = 0
    private var posted = 0

    /// Scroll events posted so far, for `--status`.
    var eventsPosted: Int {
        lock.lock(); defer { lock.unlock() }
        return posted
    }

    /// Queue a tick's distance. Called on the main thread, from the event tap.
    func add(dx: Double, dy: Double) {
        lock.lock()
        smoother.add(dx: dx, dy: dy)
        let needsStart = !running
        if needsStart {
            running = true
            lastFrame = MachTime.now()
        }
        lock.unlock()
        guard needsStart else { return }

        if link == nil { link = Self.makeLink { [weak self] in self?.frame() } }
        if let link {
            CVDisplayLinkStart(link)        // harmless if a stop is still pending
        } else {
            // No frame clock to be had: scroll the whole distance at once rather than not at all.
            lock.lock()
            let owed = smoother.owed
            smoother.cancel()
            running = false
            lock.unlock()
            post(x: Int(owed.x.rounded()), y: Int(owed.y.rounded()))
        }
    }

    /// Stop where the page is and forget the rest of the glide.
    func cancel() {
        lock.lock()
        smoother.cancel()
        lock.unlock()
    }

    private static func makeLink(_ onFrame: @escaping () -> Void) -> CVDisplayLink? {
        var link: CVDisplayLink?
        guard CVDisplayLinkCreateWithActiveCGDisplays(&link) == kCVReturnSuccess, let link else { return nil }
        CVDisplayLinkSetOutputHandler(link) { _, _, _, _, _ in
            onFrame()
            return kCVReturnSuccess
        }
        return link
    }

    /// One display refresh, on the display link's own thread.
    private func frame() {
        lock.lock()
        let now = MachTime.now()
        let step = smoother.step(dt: now - lastFrame)
        lastFrame = now
        if step == nil { running = false }
        lock.unlock()

        guard let step else {
            // Stopping from inside the callback would wait on the callback itself. Do it from
            // the main thread, unless a new tick has restarted the glide by then.
            DispatchQueue.main.async { [weak self] in
                guard let self, let link = self.link else { return }
                self.lock.lock()
                let stillIdle = !self.running
                self.lock.unlock()
                if stillIdle { CVDisplayLinkStop(link) }
            }
            return
        }
        if step.x != 0 || step.y != 0 { post(x: step.x, y: step.y) }
    }

    /// Posts a pixel-precise scroll below the HID-level tap, so CalmMouse's own tap never sees
    /// it again and every app does.
    private func post(x: Int, y: Int) {
        guard x != 0 || y != 0,
              let event = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 2,
                                  wheel1: Int32(clamping: y), wheel2: Int32(clamping: x), wheel3: 0) else { return }
        // The keys held right now still apply, except Shift: it has already been turned into
        // sideways travel, and leaving it on would have some apps turn it a second time.
        var flags = CGEventSource.flagsState(.combinedSessionState)
        flags.remove(.maskShift)
        event.flags = flags
        event.setIntegerValueField(.eventSourceUserData, value: Self.eventTag)
        event.post(tap: .cgSessionEventTap)
        lock.lock()
        posted += 1
        lock.unlock()
    }
}
