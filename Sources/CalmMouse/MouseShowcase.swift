import AppKit
import CalmMouseCore
import ImageIO
import SwiftUI

/// The Magic Mouse in the Devices column, as rendered in Blender: a still at rest and 72 frames,
/// 5° apart, to turn it. Pointing at a setting turns it back to face you and plays that setting's
/// story on the lid; the page underneath shows what it does on screen.
struct MouseShowcase<Idle: View>: View {
    let preview: SettingPreview?
    @ViewBuilder let idle: () -> Idle

    @StateObject private var model = MouseStageModel()
    @State private var pointerInside = false
    @AppStorage("magicMouseFinish") private var finishRaw = MouseFinish.white.rawValue

    private var finish: MouseFinish { MouseFinish(rawValue: finishRaw) ?? .white }

    var body: some View {
        // 30fps, and only while something moves: every frame is main-thread work the event tap's
        // run loop waits behind.
        TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: preview == nil && !model.animating)) { timeline in
            let state = model.advance(to: timeline.date.timeIntervalSinceReferenceDate, preview: preview)
            VStack(spacing: Theme.Space.medium) {
                Canvas { ctx, _ in MouseStageDrawing(ctx: ctx, state: state, frames: MouseFrames.shared(for: finish)).draw() }
                    .frame(width: MouseStage.size.width, height: MouseStage.size.height)
                    .contentShape(Rectangle())
                    .gesture(DragGesture(minimumDistance: 1)
                        .onChanged { model.drag(to: $0.translation.width, at: $0.time) }
                        .onEnded { _ in model.release() })
                    .simultaneousGesture(TapGesture(count: 2).onEnded { model.goHome() })
                    .onHover { inside in
                        guard inside != pointerInside else { return }
                        pointerInside = inside
                        if inside { NSCursor.openHand.push() } else { NSCursor.pop() }
                    }
                    // Switching device or pane takes the mouse away under the pointer: give the cursor back.
                    .onDisappear { if pointerInside { pointerInside = false; NSCursor.pop() } }
                    .contextMenu {
                        Picker(L("Mouse color"), selection: $finishRaw) {
                            Text(L("White")).tag(MouseFinish.white.rawValue)
                            Text(L("Black")).tag(MouseFinish.black.rawValue)
                        }
                    }
                    .accessibilityElement()
                    .accessibilityLabel(L("Magic Mouse"))
                    .accessibilityHint(L("Drag to turn it. Double-click to put it back."))
                    // The column is padded to 250; the mouse may use all 290 to turn.
                    .padding(.horizontal, (250 - MouseStage.size.width) / 2)
                ZStack {
                    idle().opacity(state.story == nil ? 1 : 0)
                    Canvas { ctx, size in
                        if let story = state.story { drawPreviewPage(ctx, size: size, page: story.page) }
                    }
                    .opacity(state.story == nil ? 0 : 1)
                    .accessibilityHidden(true)
                }
                .frame(width: 250, height: 104)
                .animation(.easeOut(duration: 0.2), value: state.story == nil)
            }
        }
    }
}

enum MouseFinish: String {
    case white, black
}

/// Where the square render frame sits in the 290 × 226 stage. Chosen so that the mouse fits at
/// every angle of a full turn, which leaves it ~213 pt wide at rest.
enum MouseStage {
    static let size = CGSize(width: 290, height: 226)
    static let frameSide = 720 * 0.452
    static let frameOrigin = CGPoint(x: 145 - 360 * 0.452, y: 112 - 389.4 * 0.452)
    static let frameCount = 72
}

// MARK: - Turning

/// What one frame of the stage shows.
struct MouseStageState {
    /// nil at rest, else which of the 72 turn frames.
    var turnFrame: Int?
    var story: StoryFrame?
    var storyPreview: SettingPreview?
    var storyTime = 0.0
}

final class MouseStageModel: ObservableObject {
    @Published private(set) var animating = false

    private var yaw = 0.0          // degrees from rest, counter-clockwise seen from above
    private var velocity = 0.0     // degrees per second
    private var homing = false
    private var dragging = false
    private var lastTranslation = 0.0
    private var lastDragTime: Date?
    private var lastTick: Double?
    private var storyPreview: SettingPreview?
    private var storyStart: Double?
    private var autoHome: DispatchWorkItem?

    private var atRest: Bool { !dragging && !homing && velocity == 0 && yaw == 0 }

    func drag(to translation: CGFloat, at time: Date) {
        if !dragging {
            dragging = true
            lastTranslation = 0
            homing = false
            velocity = 0
            autoHome?.cancel()
            setAnimating(true)
        }
        let dx = Double(translation) - lastTranslation
        lastTranslation = Double(translation)
        yaw += dx * 0.8
        if let last = lastDragTime {
            let dt = max(time.timeIntervalSince(last), 0.008)
            velocity = 0.5 * velocity + 0.5 * (dx * 0.8 / dt)
        }
        lastDragTime = time
    }

    func release() {
        if let last = lastDragTime, Date().timeIntervalSince(last) > 0.09 { velocity = 0 }
        dragging = false
        lastDragTime = nil
        setAnimating(true)
    }

    func goHome() {
        homing = true
        setAnimating(true)
    }

    /// Steps the turn to `now` and works out what to draw. Runs while the view is drawn, so it
    /// publishes nothing directly.
    func advance(to now: Double, preview: SettingPreview?) -> MouseStageState {
        let dt = min(max(now - (lastTick ?? now), 0), 0.05)
        lastTick = now
        if preview != storyPreview {
            storyPreview = preview
            storyStart = nil
            if preview != nil && !atRest { homing = true }
        }
        if dragging {
            // the pointer turns it
        } else if homing {
            let target = (yaw / 360).rounded() * 360
            yaw += (target - yaw) * (1 - exp(-dt * 8))
            velocity = 0
            if abs(target - yaw) < 0.35 { yaw = 0; homing = false }
        } else if abs(velocity) > 4 {
            yaw += velocity * dt
            velocity *= exp(-dt * 3.6)
        } else if velocity != 0 {
            velocity = 0
        }
        if preview != nil, atRest, storyStart == nil { storyStart = now }

        var state = MouseStageState()
        if !atRest {
            let n = MouseStage.frameCount
            state.turnFrame = ((Int((yaw / 5).rounded()) % n) + n) % n
        }
        if let preview, let start = storyStart {
            let t = (now - start).truncatingRemainder(dividingBy: preview.loop)
            state.story = preview.frame(at: t)
            state.storyPreview = preview
            state.storyTime = t
        }
        settle()
        return state
    }

    /// Stop the clock when nothing moves; a mouse left turned goes back by itself after a while.
    private func settle() {
        guard !dragging, !homing, velocity == 0 else { return }
        if yaw != 0, autoHome == nil {
            let work = DispatchWorkItem { [weak self] in
                guard let self, !self.dragging else { return }
                self.autoHome = nil
                self.goHome()
            }
            autoHome = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 7, execute: work)
        }
        if yaw == 0 { autoHome?.cancel(); autoHome = nil }
        if animating { DispatchQueue.main.async { [weak self] in self?.setAnimating(false) } }
    }

    private func setAnimating(_ on: Bool) {
        if on { lastTick = nil }
        if animating != on { animating = on }
    }
}

// MARK: - Frames

/// The rendered frames for one finish, decoded on demand. 72 frames at 600 px would be ~100 MB
/// decoded, so only the last few are kept.
final class MouseFrames {
    private static var byFinish: [MouseFinish: MouseFrames] = [:]

    static func shared(for finish: MouseFinish) -> MouseFrames {
        if let frames = byFinish[finish] { return frames }
        let frames = MouseFrames(finish: finish)
        byFinish[finish] = frames
        return frames
    }

    private let folder: String
    private let cache = NSCache<NSNumber, CGImage>()
    private lazy var still: CGImage? = load("home")

    private init(finish: MouseFinish) {
        folder = "MagicMouse/" + finish.rawValue
        cache.countLimit = 12
    }

    var rest: CGImage? { still }

    func turn(_ index: Int) -> CGImage? {
        if let image = cache.object(forKey: index as NSNumber) { return image }
        guard let image = load(String(format: "turn-%02d", index)) else { return nil }
        cache.setObject(image, forKey: index as NSNumber)
        return image
    }

    private func load(_ name: String) -> CGImage? {
        guard let url = Bundle.main.url(forResource: name, withExtension: "webp", subdirectory: folder),
              let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(source, 0, [kCGImageSourceShouldCacheImmediately: true] as CFDictionary)
    }
}

// MARK: - Drawing

/// The frame, then the story on the lid in the same camera the frame was rendered with.
private struct MouseStageDrawing {
    var ctx: GraphicsContext
    let state: MouseStageState
    let frames: MouseFrames

    private let accent = Color.accentColor
    private let ignored = Color.secondary

    func draw() {
        let frame = state.story
        let camera = DeskCamera(yaw: Double(state.turnFrame ?? 0) * 5, frame: MouseStage.frameSide,
                                originX: MouseStage.frameOrigin.x, originY: MouseStage.frameOrigin.y)
        let slide = frame?.slide ?? 0
        let dx = slide == 0 ? 0 : camera.project(Vec3(0, 0, 0), slide: slide).x - camera.project(Vec3(0, 0, 0)).x
        let image = state.turnFrame.flatMap { frames.turn($0) } ?? frames.rest
        if let image {
            let rect = CGRect(x: MouseStage.frameOrigin.x + dx, y: MouseStage.frameOrigin.y + ((frame?.pressed ?? false) ? 0.8 : 0),
                              width: MouseStage.frameSide, height: MouseStage.frameSide)
            ctx.draw(Image(decorative: image, scale: 1), in: rect)
        }
        guard let frame, let preview = state.storyPreview else { return }
        drawZone(frame, camera, slide)
        drawTrails(preview, frame, camera, slide)
        for ripple in frame.ripples where ripple.progress > 0 && ripple.progress < 1 {
            let ring = path(camera.disc(u: ripple.u, v: ripple.v, radius: 0.5 + 1.1 * ripple.progress, slide: slide), closed: true)
            ctx.stroke(ring, with: .color(accent.opacity(0.9 * (1 - ripple.progress))), lineWidth: 1.8)
        }
        if let dial = frame.dial, dial.progress > 0, dial.progress < 1 {
            let ring = camera.disc(u: dial.u, v: dial.v, radius: 0.42, slide: slide, steps: 40)
            let n = max(2, Int((40 * dial.progress).rounded()))
            ctx.stroke(path(Array(ring.prefix(n)), closed: false), with: .color(accent),
                       style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
        }
        for touch in frame.touches where touch.alpha > 0.01 { drawTouch(touch, camera, slide) }
        if let mark = frame.crossOnLid, mark.alpha > 0.01 {
            let c = point(camera.project(MagicMouseFigure.surface(u: mark.u, v: mark.v), slide: slide))
            var x = Path()
            x.move(to: CGPoint(x: c.x - 5, y: c.y - 5)); x.addLine(to: CGPoint(x: c.x + 5, y: c.y + 5))
            x.move(to: CGPoint(x: c.x + 5, y: c.y - 5)); x.addLine(to: CGPoint(x: c.x - 5, y: c.y + 5))
            ctx.stroke(x, with: .color(ignored.opacity(mark.alpha)), style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
        }
    }

    private func point(_ p: (x: Double, y: Double)) -> CGPoint { CGPoint(x: p.x, y: p.y) }

    private func path(_ points: [(x: Double, y: Double)], closed: Bool) -> Path {
        var p = Path()
        p.addLines(points.map(point))
        if closed { p.closeSubpath() }
        return p
    }

    /// The part of the lid a setting is about, walked along the lid so curved edges stay curved.
    private func drawZone(_ frame: StoryFrame, _ camera: DeskCamera, _ slide: Double) {
        guard let zone = frame.zone else { return }
        var region: [(x: Double, y: Double)] = [], edge: [(x: Double, y: Double)] = []
        func walk(_ u0: Double, _ v0: Double, _ u1: Double, _ v1: Double, _ n: Int, isEdge: Bool = false) {
            for i in 0...n {
                let f = Double(i) / Double(n)
                let q = camera.project(MagicMouseFigure.surface(u: u0 + (u1 - u0) * f, v: v0 + (v1 - v0) * f), slide: slide)
                region.append(q)
                if isEdge { edge.append(q) }
            }
        }
        let e = 0.985
        switch zone {
        case .front(let depth):
            let vb = -1 + 2 * depth
            walk(-e, vb, -e, -e, 20); walk(-e, -e, e, -e, 30); walk(e, -e, e, vb, 20); walk(e, vb, -e, vb, 30, isEdge: true)
        case .rightSide(let split):
            walk(split, -e, e, -e, 16); walk(e, -e, e, e, 40); walk(e, e, split, e, 16); walk(split, e, split, -e, 40, isEdge: true)
        }
        ctx.fill(path(region, closed: true), with: .color(accent.opacity(0.16)))
        ctx.stroke(path(edge, closed: false), with: .color(accent.opacity(0.75)), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
    }

    /// Where each finger was a moment ago: the stories are pure functions of time.
    private func drawTrails(_ preview: SettingPreview, _ frame: StoryFrame, _ camera: DeskCamera, _ slide: Double) {
        for (k, touch) in frame.touches.enumerated() where touch.alpha > 0.05 {
            var trail: [(p: CGPoint, a: Double)] = []
            for i in 0..<14 {
                let past = preview.frame(at: max(state.storyTime - Double(i) * 0.025, 0))
                guard k < past.touches.count, past.touches[k].alpha > 0.05 else { break }
                let t = past.touches[k]
                let p = point(camera.project(MagicMouseFigure.surface(u: t.u, v: t.v), slide: slide))
                // A finger that jumps (lifted and put down elsewhere) leaves no streak.
                if let last = trail.last, hypot(p.x - last.p.x, p.y - last.p.y) > 12 { break }
                trail.append((p, t.alpha * (1 - Double(i) / 14)))
            }
            guard trail.count > 1 else { continue }
            for i in 1..<trail.count where hypot(trail[i].p.x - trail[i - 1].p.x, trail[i].p.y - trail[i - 1].p.y) > 0.3 {
                var seg = Path()
                seg.move(to: trail[i - 1].p); seg.addLine(to: trail[i].p)
                ctx.stroke(seg, with: .color((touch.ignored ? ignored : accent).opacity(0.5 * trail[i].a)),
                           style: StrokeStyle(lineWidth: 6 * (1 - Double(i) / 14) + 1, lineCap: .round))
            }
        }
    }

    /// A fingertip on the lid: a glowing disc lying on the surface, a white core while it presses.
    private func drawTouch(_ touch: StoryTouch, _ camera: DeskCamera, _ slide: Double) {
        let color = touch.ignored ? ignored : accent
        let disc = path(camera.disc(u: touch.u, v: touch.v, radius: touch.pressing ? 0.56 : 0.5, slide: slide), closed: true)
        let c = point(camera.project(MagicMouseFigure.surface(u: touch.u, v: touch.v), slide: slide))
        var glow = ctx
        glow.addFilter(.shadow(color: color.opacity(0.55 * touch.alpha), radius: 6))
        glow.fill(disc, with: .radialGradient(Gradient(colors: [color.opacity(0.95 * touch.alpha), color.opacity(0.62 * touch.alpha)]),
                                              center: c, startRadius: 0, endRadius: 14))
        ctx.stroke(disc, with: .color(.white.opacity(0.75 * touch.alpha)), lineWidth: 1)
        if touch.pressing {
            let core = path(camera.disc(u: touch.u, v: touch.v, radius: 0.17, slide: slide, steps: 16), closed: true)
            ctx.fill(core, with: .color(.white.opacity(0.92 * touch.alpha)))
        }
    }
}
