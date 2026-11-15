import AppKit
import CalmMouseCore
import ImageIO
import SwiftUI

/// The Magic Mouse in the Devices column, as rendered in Blender: a still at rest and 72 frames,
/// 5° apart, to turn it. Pointing at a setting turns it back to face you and plays that setting's
/// story on the lid; the page underneath shows what it does on screen.
struct MouseShowcase<Idle: View>: View {
    let preview: SettingPreview?
    /// The ring around the mouse plays when it connects and when it takes a preset.
    var connected = true
    var presetID: UUID?
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
                        if let story = state.story { PageCardDrawing(ctx: ctx, size: size, page: story.page).draw() }
                    }
                    .opacity(state.story == nil ? 0 : 1)
                    .accessibilityHidden(true)
                }
                .frame(width: 250, height: 104)
                .animation(.easeOut(duration: 0.2), value: state.story == nil)
            }
        }
        .onAppear { model.playRing() }
        .onChange(of: connected) { if $0 { model.playRing() } }
        .onChange(of: presetID) { if $0 != nil { model.playRing() } }
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
    /// The big ring around the mouse, while it plays.
    var ring: PresenceRing.Frame?
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
    private var ringRequested = false
    private var ringStart: Double?

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

    /// Plays the ring around the whole mouse once.
    func playRing() {
        ringRequested = true
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
        if ringRequested { ringStart = now; ringRequested = false }

        var state = MouseStageState()
        if !atRest {
            let n = MouseStage.frameCount
            state.turnFrame = ((Int((yaw / 5).rounded()) % n) + n) % n
        }
        if let preview, let start = storyStart {
            let t = (now - start).truncatingRemainder(dividingBy: preview.loop)
            state.story = preview.frame(at: t)
        }
        if let start = ringStart {
            state.ring = PresenceRing.frame(at: now - start)
            if state.ring == nil { ringStart = nil }
        }
        settle()
        return state
    }

    /// Stop the clock when nothing moves; a mouse left turned goes back by itself after a while.
    private func settle() {
        guard !dragging, !homing, velocity == 0, ringStart == nil, !ringRequested else { return }
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
    /// Fingertips and taps: one flat, translucent light blue.
    private let touchBlue = Color(red: 0.33, green: 0.64, blue: 0.98)

    func draw() {
        let frame = state.story
        let camera = DeskCamera(yaw: Double(state.turnFrame ?? 0) * 5, frame: MouseStage.frameSide,
                                originX: MouseStage.frameOrigin.x, originY: MouseStage.frameOrigin.y)
        let slide = frame?.slide ?? 0
        let dx = slide == 0 ? 0 : camera.project(Vec3(0, 0, 0), slide: slide).x - camera.project(Vec3(0, 0, 0)).x
        if let ring = state.ring { drawRing(ring, camera, yaw: Double(state.turnFrame ?? 0) * 5) }
        let image = state.turnFrame.flatMap { frames.turn($0) } ?? frames.rest
        if let image {
            let rect = CGRect(x: MouseStage.frameOrigin.x + dx, y: MouseStage.frameOrigin.y + ((frame?.pressed ?? false) ? 0.8 : 0),
                              width: MouseStage.frameSide, height: MouseStage.frameSide)
            ctx.draw(Image(decorative: image, scale: 1), in: rect)
        }
        guard let frame else { return }
        drawZone(frame, camera, slide)
        for ripple in frame.ripples where ripple.progress > 0 && ripple.progress < 1 { drawRipple(ripple, camera, slide) }
        if let dial = frame.dial, dial.progress > 0, dial.progress < 1 {
            let ring = camera.disc(u: dial.u, v: dial.v, radius: 0.42, slide: slide, steps: 40)
            let arc = path(Array(ring.prefix(max(2, Int((40 * dial.progress).rounded())))), closed: false)
            ctx.stroke(arc, with: .color(accent), style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
        }
        for touch in frame.touches where touch.alpha > 0.01 { drawTouch(touch, camera, slide) }
        if let mark = frame.crossOnLid, mark.alpha > 0.01 { drawIgnoredMark(mark, camera, slide) }
    }

    private func point(_ p: (x: Double, y: Double)) -> CGPoint { CGPoint(x: p.x, y: p.y) }

    private func path(_ points: [(x: Double, y: Double)], closed: Bool) -> Path {
        var p = Path()
        p.addLines(points.map(point))
        if closed { p.closeSubpath() }
        return p
    }

    /// The ring around the whole mouse, lying on the desk: radial ticks that swell into travelling
    /// lobes, blue drifting into cyan, blooming out into dots midway. Under the mouse, so its body
    /// hides the far side.
    private func drawRing(_ ring: PresenceRing.Frame, _ camera: DeskCamera, yaw: Double) {
        let n = 144, shown = Int((Double(n) * ring.sweep).rounded())
        guard shown > 0 else { return }
        let start = (-90 - DeskCamera.homeYaw - yaw) * .pi / 180     // the side nearest you
        func at(_ r: Double, _ a: Double) -> CGPoint { point(camera.project(Vec3(r * cos(a), r * sin(a), 0))) }
        for i in 0..<shown {
            let a = start + 2 * Double.pi * Double(i) / Double(n)
            let tick = PresenceRing.tick(ring, angle: a)
            let outer = at(ring.radius, a)
            let edge = min(1, max(0, Double(min(outer.x, MouseStage.size.width - outer.x)) / 18))
            let alpha = ring.alpha * edge
            guard alpha > 0.01 else { continue }
            let color = Color(red: 0.16 + 0.09 * tick.mix, green: 0.42 + 0.42 * tick.mix, blue: 1 - 0.06 * tick.mix)
            let strength = 0.5 + 0.5 * (tick.length - 0.1) / 0.32
            var dash = Path()
            dash.move(to: outer); dash.addLine(to: at(ring.radius - tick.length, a))
            ctx.stroke(dash, with: .color(color.opacity(0.92 * alpha * strength)), style: StrokeStyle(lineWidth: 1.1, lineCap: .round))
            // The bloom: rows of dots carried outward, fading as they go.
            let rows = Int(tick.bloom / 0.11)
            if rows > 0 {
                for j in 1...rows {
                    let p = at(ring.radius + Double(j) * 0.11, a), k = 1 - Double(j) / Double(rows + 1)
                    ctx.fill(Path(ellipseIn: CGRect(x: p.x - 0.65, y: p.y - 0.65, width: 1.3, height: 1.3)),
                             with: .color(color.opacity(0.85 * alpha * k)))
                }
            }
        }
    }

    /// The part of the lid a setting is about, walked along the lid so curved edges stay curved:
    /// a light tint and a thin dashed edge.
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
        ctx.fill(path(region, closed: true), with: .color(accent.opacity(0.12)))
        ctx.stroke(path(edge, closed: false), with: .color(accent.opacity(0.7)), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
    }

    /// A tap: a soft flat disc in the fingertip's colour that swells a little and fades.
    private func drawRipple(_ ripple: StoryRipple, _ camera: DeskCamera, _ slide: Double) {
        let p = ripple.progress, fade = (1 - p) * (1 - p)
        let disc = path(camera.disc(u: ripple.u, v: ripple.v, radius: 0.5 + 0.55 * p, slide: slide), closed: true)
        ctx.fill(disc, with: .color(touchBlue.opacity(0.3 * fade)))
    }

    /// A fingertip on the lid: a flat, translucent light-blue disc lying on the surface; firmer
    /// while it presses. A grip finger that doesn't count is grey.
    private func drawTouch(_ touch: StoryTouch, _ camera: DeskCamera, _ slide: Double) {
        let disc = path(camera.disc(u: touch.u, v: touch.v, radius: 0.5, slide: slide), closed: true)
        let color = touch.ignored ? Color(white: 0.5) : touchBlue
        ctx.fill(disc, with: .color(color.opacity((touch.pressing ? 0.68 : 0.46) * touch.alpha)))
    }

    /// A grip finger that doesn't count: a small cross beside it.
    private func drawIgnoredMark(_ mark: StoryFrame.Mark, _ camera: DeskCamera, _ slide: Double) {
        let c = point(camera.project(MagicMouseFigure.surface(u: mark.u, v: mark.v), slide: slide)), r = 3.5
        var x = Path()
        x.move(to: CGPoint(x: c.x - r, y: c.y - r)); x.addLine(to: CGPoint(x: c.x + r, y: c.y + r))
        x.move(to: CGPoint(x: c.x + r, y: c.y - r)); x.addLine(to: CGPoint(x: c.x - r, y: c.y + r))
        ctx.stroke(x, with: .color(ignored.opacity(mark.alpha)), style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
    }
}

// MARK: - The page

/// The little window under the mouse: what the setting does on screen. A page of text that
/// scrolls or holds still, a button that clicks, a menu, a thing being dragged.
private struct PageCardDrawing {
    var ctx: GraphicsContext
    let size: CGSize
    let page: PageState

    private let accent = Color.accentColor
    private let ink = Color.primary
    private let paper = Color(nsColor: .textBackgroundColor)
    private let bar: CGFloat = 15

    private var window: CGRect { CGRect(x: 14, y: 5, width: size.width - 28, height: size.height - 13) }
    private var content: CGRect {
        CGRect(x: window.minX + 12, y: window.minY + bar + 1, width: window.width - 24, height: window.height - bar - 1)
    }
    /// Where the pointer is: on the button when there is one.
    private var pointer: CGPoint {
        page.button ? CGPoint(x: buttonRect.midX + 10, y: buttonRect.midY + 3) : CGPoint(x: content.minX + 70, y: content.midY + 14)
    }
    private var buttonRect: CGRect { CGRect(x: content.minX, y: content.minY + 42, width: 54, height: 17) }

    func draw() {
        let shape = Path(roundedRect: window, cornerRadius: 9)
        ctx.drawLayer { layer in
            layer.addFilter(.shadow(color: .black.opacity(0.13), radius: 7, x: 0, y: 2))
            layer.fill(shape, with: .color(paper))
        }
        ctx.stroke(shape, with: .color(ink.opacity(0.1)), lineWidth: 0.5)
        for i in 0..<3 {
            let c = CGPoint(x: window.minX + 10 + CGFloat(i) * 8, y: window.minY + bar / 2)
            ctx.fill(Path(ellipseIn: CGRect(x: c.x - 2.3, y: c.y - 2.3, width: 4.6, height: 4.6)), with: .color(ink.opacity(0.16)))
        }
        var divider = Path()
        divider.move(to: CGPoint(x: window.minX, y: window.minY + bar)); divider.addLine(to: CGPoint(x: window.maxX, y: window.minY + bar))
        ctx.stroke(divider, with: .color(ink.opacity(0.07)), lineWidth: 0.5)

        var inside = ctx
        inside.clip(to: shape)
        inside.clip(to: Path(CGRect(x: window.minX, y: content.minY, width: window.width, height: content.height)))
        if page.button { drawDialog(&inside) } else { drawText(&inside) }
        if let object = page.object { drawObject(&inside, at: object) }
        // Text running off the page fades instead of being cut.
        let fade = Gradient(colors: [paper, paper.opacity(0)])
        inside.fill(Path(CGRect(x: window.minX, y: content.minY, width: window.width, height: 9)),
                    with: .linearGradient(fade, startPoint: CGPoint(x: 0, y: content.minY), endPoint: CGPoint(x: 0, y: content.minY + 9)))
        inside.fill(Path(CGRect(x: window.minX, y: content.maxY - 12, width: window.width, height: 12)),
                    with: .linearGradient(fade, startPoint: CGPoint(x: 0, y: content.maxY), endPoint: CGPoint(x: 0, y: content.maxY - 12)))
        if let arrows = page.arrows { drawArrows(&inside, arrows) }
        if page.button || page.menu > 0.01 || page.object != nil {
            drawCursor(page.object.map { CGPoint(x: objectRect($0).midX + 4, y: objectRect($0).midY + 4) } ?? pointer)
        }
        if page.menu > 0.01 { drawMenu(alpha: page.menu) }
        if let held = page.key { drawKey(held: held > 0.5) }
        if page.cross > 0.01 { drawIgnoredBadge(alpha: page.cross) }
    }

    /// A page of text: headings, paragraphs and pictures, scrolling by `scrollY` and zooming.
    private func drawText(_ c: inout GraphicsContext) {
        var c = c
        let zoom = page.zoom
        if zoom != 1 {
            c.translateBy(x: content.minX, y: content.minY)
            c.scaleBy(x: zoom, y: zoom)
            c.translateBy(x: -content.minX, y: -content.minY)
        }
        let period: CGFloat = 100, scroll = CGFloat(page.scrollY) * 1.2, shift = CGFloat(page.scrollX) * 1.2
        let width = content.width - (page.arrows == nil ? 0 : 30)
        let first = Int((scroll / period).rounded(.down)) - 1
        for n in first...(first + 3) {
            let top = content.minY + CGFloat(n) * period - scroll + 6
            func line(_ y: CGFloat, _ x: CGFloat, _ w: CGFloat, thick: CGFloat = 2.6, alpha: Double = 0.15) {
                var p = Path()
                p.move(to: CGPoint(x: x - shift, y: top + y)); p.addLine(to: CGPoint(x: x + w - shift, y: top + y))
                c.stroke(p, with: .color(ink.opacity(alpha)), style: StrokeStyle(lineWidth: thick, lineCap: .round))
            }
            let x0 = content.minX
            line(4, x0, width * 0.5, thick: 4.5, alpha: 0.28)
            for (i, w) in [0.94, 0.86, 0.9, 0.55].enumerated() { line(16 + CGFloat(i) * 9, x0, width * w) }
            let picture = CGRect(x: x0 - shift, y: top + 56, width: 46, height: 28)
            c.fill(Path(roundedRect: picture, cornerRadius: 4), with: .color(ink.opacity(0.07)))
            for (i, w) in [0.62, 0.55, 0.38].enumerated() { line(60 + CGFloat(i) * 9, x0 + 56, (width - 56) * w) }
        }
    }

    /// For the stories that click: a short message with a button under the pointer.
    private func drawDialog(_ c: inout GraphicsContext) {
        func line(_ y: CGFloat, _ w: CGFloat, thick: CGFloat = 2.6, alpha: Double = 0.15) {
            var p = Path()
            p.move(to: CGPoint(x: content.minX, y: content.minY + y)); p.addLine(to: CGPoint(x: content.minX + w, y: content.minY + y))
            c.stroke(p, with: .color(ink.opacity(alpha)), style: StrokeStyle(lineWidth: thick, lineCap: .round))
        }
        line(10, content.width * 0.45, thick: 4.5, alpha: 0.28)
        line(22, content.width * 0.9)
        line(31, content.width * 0.68)
        let flash = page.flash
        let button = buttonRect.insetBy(dx: flash > 0.5 ? 0.6 : 0, dy: flash > 0.5 ? 0.4 : 0)
        let shape = Path(roundedRect: button, cornerRadius: button.height / 2)
        c.fill(shape, with: .color(accent.opacity(0.12 + 0.88 * flash)))
        c.stroke(shape, with: .color(accent.opacity(0.45 + 0.5 * flash)), lineWidth: 0.8)
        var label = Path()
        label.move(to: CGPoint(x: button.midX - 11, y: button.midY)); label.addLine(to: CGPoint(x: button.midX + 11, y: button.midY))
        c.stroke(label, with: .color(flash > 0.3 ? Color.white.opacity(0.95) : accent.opacity(0.75)),
                 style: StrokeStyle(lineWidth: 2.6, lineCap: .round))
    }

    private func objectRect(_ move: Double) -> CGRect {
        CGRect(x: content.minX + 18 + CGFloat(move) * 2.6, y: content.midY - 12, width: 24, height: 24)
    }

    /// The thing being dragged, lifted off the page.
    private func drawObject(_ c: inout GraphicsContext, at move: Double) {
        let r = objectRect(move), shape = Path(roundedRect: r, cornerRadius: 6)
        c.drawLayer { layer in
            layer.addFilter(.shadow(color: .black.opacity(0.25), radius: 4, x: 0, y: 2))
            layer.fill(shape, with: .color(accent.opacity(0.9)))
        }
        c.stroke(shape, with: .color(.white.opacity(0.35)), lineWidth: 0.8)
    }

    /// The right-click menu, popping up at the pointer.
    private func drawMenu(alpha: Double) {
        let at = pointer
        let r = CGRect(x: at.x + 2, y: max(window.minY + bar + 2, at.y - 50), width: 76, height: 46)
        let shape = Path(roundedRect: r, cornerRadius: 6)
        ctx.drawLayer { layer in
            layer.opacity = alpha
            layer.addFilter(.shadow(color: .black.opacity(0.22), radius: 8, x: 0, y: 3))
            layer.fill(shape, with: .color(paper))
        }
        ctx.stroke(shape, with: .color(ink.opacity(0.12 * alpha)), lineWidth: 0.5)
        let row = CGRect(x: r.minX + 4, y: r.minY + 4, width: r.width - 8, height: 12)
        ctx.fill(Path(roundedRect: row, cornerRadius: 3), with: .color(accent.opacity(alpha)))
        for (i, w) in [36.0, 28.0, 40.0].enumerated() {
            var p = Path()
            let y = r.minY + 10 + CGFloat(i) * 13
            p.move(to: CGPoint(x: r.minX + 10, y: y)); p.addLine(to: CGPoint(x: r.minX + 10 + w, y: y))
            ctx.stroke(p, with: .color(i == 0 ? Color.white.opacity(alpha) : ink.opacity(0.3 * alpha)),
                       style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
        }
    }

    /// The arrow pointer, as macOS draws it.
    private func drawCursor(_ at: CGPoint) {
        var p = Path()
        let pts: [(CGFloat, CGFloat)] = [(0, 0), (0, 13.5), (3.4, 10.4), (5.8, 15.6), (7.7, 14.8), (5.4, 9.6), (9.8, 9.6)]
        p.addLines(pts.map { CGPoint(x: at.x + $0.0, y: at.y + $0.1) })
        p.closeSubpath()
        ctx.drawLayer { layer in
            layer.addFilter(.shadow(color: .black.opacity(0.3), radius: 1.5, x: 0, y: 1))
            layer.fill(p, with: .color(.black))
        }
        ctx.stroke(p, with: .color(.white), style: StrokeStyle(lineWidth: 1, lineJoin: .round))
    }

    /// What the page does, beside it: the straight line it keeps, or which way it now goes.
    private func drawArrows(_ c: inout GraphicsContext, _ arrows: PageState.Arrows) {
        let x = content.maxX - 10, top = content.minY + 10, bottom = content.maxY - 10
        func arrow(_ a: CGPoint, _ b: CGPoint, _ color: Color, _ alpha: Double, dashed: Bool = false) {
            guard alpha > 0.01 else { return }
            var p = Path()
            p.move(to: a); p.addLine(to: b)
            let angle = atan2(b.y - a.y, b.x - a.x)
            for side in [-1.0, 1.0] {
                p.move(to: b)
                p.addLine(to: CGPoint(x: b.x - 6 * cos(angle + side * 0.55), y: b.y - 6 * sin(angle + side * 0.55)))
            }
            c.stroke(p, with: .color(color.opacity(alpha)), style: StrokeStyle(lineWidth: 1.8, lineCap: .round, lineJoin: .round, dash: dashed ? [3, 3] : []))
        }
        switch arrows.kind {
        case .straight:
            arrow(CGPoint(x: x - 14, y: bottom), CGPoint(x: x - 5, y: top), Color.secondary, arrows.drift, dashed: true)
            arrow(CGPoint(x: x + 3, y: bottom), CGPoint(x: x + 3, y: top), accent, arrows.page)
        case .diagonal:
            arrow(CGPoint(x: x - 10, y: bottom), CGPoint(x: x + 3, y: top), accent, arrows.page)
        case .flipped(let on):
            arrow(CGPoint(x: x, y: on ? top : bottom), CGPoint(x: x, y: on ? bottom : top), accent, arrows.page)
        }
    }

    /// The key held while scrolling.
    private func drawKey(held: Bool) {
        let r = CGRect(x: window.maxX - 38, y: window.maxY - 25, width: 28, height: 19)
        let shape = Path(roundedRect: r, cornerRadius: 5)
        ctx.drawLayer { layer in
            layer.addFilter(.shadow(color: .black.opacity(0.18), radius: 2, x: 0, y: 1))
            layer.fill(shape, with: .color(paper))
        }
        ctx.fill(shape, with: .color(held ? accent.opacity(0.18) : ink.opacity(0.04)))
        ctx.stroke(shape, with: .color(held ? accent.opacity(0.9) : ink.opacity(0.3)), lineWidth: 1)
        ctx.draw(Text("⌘").font(.system(size: 11, weight: .medium)).foregroundColor(held ? accent : ink.opacity(0.7)),
                 at: CGPoint(x: r.midX, y: r.midY))
    }

    /// In the title bar: the page ignored that.
    private func drawIgnoredBadge(alpha: Double) {
        let c = CGPoint(x: window.maxX - 12, y: window.minY + bar / 2)
        ctx.fill(Path(ellipseIn: CGRect(x: c.x - 5.5, y: c.y - 5.5, width: 11, height: 11)), with: .color(Color.secondary.opacity(0.85 * alpha)))
        var x = Path()
        x.move(to: CGPoint(x: c.x - 2.3, y: c.y - 2.3)); x.addLine(to: CGPoint(x: c.x + 2.3, y: c.y + 2.3))
        x.move(to: CGPoint(x: c.x + 2.3, y: c.y - 2.3)); x.addLine(to: CGPoint(x: c.x - 2.3, y: c.y + 2.3))
        ctx.stroke(x, with: .color(.white.opacity(alpha)), style: StrokeStyle(lineWidth: 1.4, lineCap: .round))
    }
}
