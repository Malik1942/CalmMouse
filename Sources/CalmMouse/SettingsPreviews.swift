import SwiftUI

// MARK: - What a preview can show

/// Each case is one little looping animation — a page, a Magic Mouse seen from above,
/// and a finger — showing what a setting does, in the spirit of Apple's mouse pane.
enum SettingPreview {
    case blockWhileClick
    case settleAfterRelease
    case ignoreNudges
    case straightLines
    case momentum(on: Bool)
    case ignoreSideways
    case tapToClick
    case tapRightClick(doubleTap: Bool)
    case tapZone(depth: Double)
    case tapAndDrag
    case twoFingerDrag
    case keyZoom

    /// Loop length in seconds.
    var loop: Double {
        switch self {
        case .blockWhileClick:    return 3.6
        case .settleAfterRelease: return 3.4
        case .ignoreNudges:       return 3.4
        case .straightLines:      return 3.0
        case .momentum:           return 3.2
        case .ignoreSideways:     return 2.8
        case .tapToClick:         return 2.6
        case .tapRightClick:      return 3.4
        case .tapZone:            return 3.0
        case .tapAndDrag:         return 4.0
        case .twoFingerDrag:      return 3.8
        case .keyZoom:            return 3.2
        }
    }
}

// MARK: - Row wrapper

/// Wraps a settings row. Hovering the row — the toggle, the slider, or the ⓘ itself —
/// pops a small animated preview of what the setting does, plus a one-line caption.
struct ExplainedRow<Content: View>: View {
    let preview: SettingPreview
    let caption: String
    @ViewBuilder let content: () -> Content

    @State private var showPopover = false
    @State private var pending: DispatchWorkItem?

    var body: some View {
        HStack(spacing: 8) {
            content()
            Image(systemName: "info.circle")
                .foregroundStyle(.secondary)
                .popover(isPresented: $showPopover, arrowEdge: .trailing) {
                    VStack(alignment: .leading, spacing: 10) {
                        PreviewCanvas(preview: preview)
                            .frame(width: 250, height: 150)
                        Text(caption)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .padding(12)
                    .frame(width: 274)
                }
        }
        .onHover { hovering in
            pending?.cancel()
            if hovering {
                let work = DispatchWorkItem { showPopover = true }
                pending = work
                // Delay so scrubbing past rows doesn't churn popovers open and closed —
                // each present/dismiss is a burst of main-thread animation work.
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.55, execute: work)
            } else {
                showPopover = false
            }
        }
    }
}

// MARK: - Canvas

struct PreviewCanvas: View {
    let preview: SettingPreview

    var body: some View {
        // 30fps, not the display's full refresh rate: these are gentle looping sketches, and
        // every frame is main-thread work the event tap's run loop has to wait behind.
        TimelineView(.animation(minimumInterval: 1.0 / 30.0)) { timeline in
            Canvas { ctx, size in
                let t = timeline.date.timeIntervalSinceReferenceDate
                    .truncatingRemainder(dividingBy: preview.loop)
                PreviewScene(ctx: ctx, size: size).draw(preview, at: t)
            }
        }
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.04)))
    }
}

/// All drawing is a pure function of the loop time — no state, perfectly repeatable.
private struct PreviewScene {
    var ctx: GraphicsContext
    let size: CGSize

    // Palette (resolves against light/dark mode automatically).
    let ink = Color.primary.opacity(0.65)
    let faint = Color.primary.opacity(0.25)
    let paper = Color.primary.opacity(0.045)
    let accent = Color.accentColor
    let ignored = Color.secondary.opacity(0.85)

    // Standard layout: page on the left, mouse (top view, front = up) on the right.
    var pageRect: CGRect { CGRect(x: 16, y: 15, width: 102, height: 120) }
    var mouseRect: CGRect { CGRect(x: 158, y: 22, width: 62, height: 106) }
    /// Where a finger naturally rests on the mouse.
    var touchPoint: CGPoint { CGPoint(x: mouseRect.midX, y: mouseRect.midY + 8) }

    // MARK: Timing helpers

    /// 0→1 smoothstep between two moments of the loop.
    func seg(_ t: Double, _ a: Double, _ b: Double) -> Double {
        let x = min(max((t - a) / (b - a), 0), 1)
        return x * x * (3 - 2 * x)
    }
    func easeOut(_ x: Double) -> Double { let c = min(max(x, 0), 1); return 1 - (1 - c) * (1 - c) }
    /// 1 inside [a,b] with soft edges, 0 outside.
    func pulse(_ t: Double, _ a: Double, _ b: Double, fade: Double = 0.15) -> Double {
        seg(t, a, a + fade) * (1 - seg(t, b - fade, b))
    }

    // MARK: Primitives

    func drawPage(_ r: CGRect, scrollY: CGFloat = 0, scrollX: CGFloat = 0, zoom: CGFloat = 1,
                  buttonFlash: Double = 0, objectAt: CGPoint? = nil) {
        var c = ctx
        c.fill(Path(roundedRect: r, cornerRadius: 6), with: .color(paper))
        c.stroke(Path(roundedRect: r, cornerRadius: 6), with: .color(faint), lineWidth: 1)
        c.clip(to: Path(roundedRect: r.insetBy(dx: 2, dy: 2), cornerRadius: 5))

        let spacing = 13.0 * zoom
        let phase = scrollY.truncatingRemainder(dividingBy: spacing)
        var i = 0
        var y = r.minY + 10 - phase - spacing
        while y < r.maxY {
            // Vary line lengths a bit so movement is visible.
            let inset: CGFloat = [10, 22, 14, 30][i & 3]
            var line = Path()
            line.move(to: CGPoint(x: r.minX + 10 - scrollX, y: y))
            line.addLine(to: CGPoint(x: r.maxX - inset - scrollX, y: y))
            c.stroke(line, with: .color(ink.opacity(0.5)), style: StrokeStyle(lineWidth: 3 * zoom, lineCap: .round))
            y += spacing
            i += 1
        }
        if buttonFlash > 0 {
            let b = CGRect(x: r.minX + 12, y: r.midY - 8, width: 44, height: 16)
            c.fill(Path(roundedRect: b, cornerRadius: 4), with: .color(accent.opacity(0.55 * buttonFlash)))
        }
        if let p = objectAt {
            let o = CGRect(x: p.x - 11, y: p.y - 11, width: 22, height: 22)
            c.fill(Path(roundedRect: o, cornerRadius: 5), with: .color(accent.opacity(0.75)))
        }
    }

    func drawMouse(_ r: CGRect, pressed: Bool = false, zoneDepth: CGFloat? = nil,
                   rightSplit: CGFloat? = nil) {
        let body = Path(roundedRect: r, cornerRadius: r.width * 0.42)
        ctx.fill(body, with: .color(Color.primary.opacity(pressed ? 0.12 : 0.05)))
        if let depth = zoneDepth {
            // Front of the mouse points up in this top view.
            var c = ctx
            c.clip(to: body)
            let zone = CGRect(x: r.minX, y: r.minY, width: r.width, height: r.height * depth)
            c.fill(Path(zone), with: .color(accent.opacity(0.14)))
            var boundary = Path()
            boundary.move(to: CGPoint(x: r.minX + 4, y: zone.maxY))
            boundary.addLine(to: CGPoint(x: r.maxX - 4, y: zone.maxY))
            c.stroke(boundary, with: .color(accent.opacity(0.7)),
                     style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
        }
        if let split = rightSplit {
            // The right-click strip along the right side of the surface.
            var c = ctx
            c.clip(to: body)
            let zone = CGRect(x: r.minX + r.width * split, y: r.minY,
                              width: r.width * (1 - split), height: r.height)
            c.fill(Path(zone), with: .color(accent.opacity(0.14)))
            var boundary = Path()
            boundary.move(to: CGPoint(x: zone.minX, y: r.minY + 4))
            boundary.addLine(to: CGPoint(x: zone.minX, y: r.maxY - 4))
            c.stroke(boundary, with: .color(accent.opacity(0.7)),
                     style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
        }
        ctx.stroke(body, with: .color(pressed ? accent.opacity(0.9) : ink), lineWidth: pressed ? 2 : 1.5)
    }

    func drawFinger(at p: CGPoint, pressing: Bool = false, alpha: Double = 1, color: Color? = nil) {
        guard alpha > 0.01 else { return }
        let base = color ?? accent
        let r: CGFloat = pressing ? 9 : 8
        let dot = Path(ellipseIn: CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r))
        ctx.fill(dot, with: .color(base.opacity(0.85 * alpha)))
        if pressing {
            let inner = Path(ellipseIn: CGRect(x: p.x - 3, y: p.y - 3, width: 6, height: 6))
            ctx.fill(inner, with: .color(.white.opacity(0.9 * alpha)))
        }
    }

    func drawRipple(at p: CGPoint, progress: Double) {
        guard progress > 0, progress < 1 else { return }
        let r = 8 + 16 * progress
        let ring = Path(ellipseIn: CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r))
        ctx.stroke(ring, with: .color(accent.opacity(1 - progress)), lineWidth: 2)
    }

    /// A little context menu popping open on the page — the visible result of a right click.
    func drawMenu(at p: CGPoint, alpha: Double) {
        guard alpha > 0.01 else { return }
        let r = CGRect(x: p.x, y: p.y, width: 46, height: 36)
        ctx.fill(Path(roundedRect: r, cornerRadius: 5),
                 with: .color(Color.primary.opacity(0.10 * alpha)))
        ctx.stroke(Path(roundedRect: r, cornerRadius: 5), with: .color(ink.opacity(alpha)), lineWidth: 1)
        for (i, inset) in [8.0, 14.0, 11.0].enumerated() {
            var line = Path()
            let y = r.minY + 9 + Double(i) * 9
            line.move(to: CGPoint(x: r.minX + 6, y: y))
            line.addLine(to: CGPoint(x: r.maxX - inset, y: y))
            ctx.stroke(line, with: .color((i == 0 ? accent : ink).opacity((i == 0 ? 0.9 : 0.5) * alpha)),
                       style: StrokeStyle(lineWidth: 3, lineCap: .round))
        }
    }

    /// "Nothing happens" mark.
    func drawCross(at p: CGPoint, alpha: Double) {
        guard alpha > 0.01 else { return }
        var path = Path()
        path.move(to: CGPoint(x: p.x - 6, y: p.y - 6)); path.addLine(to: CGPoint(x: p.x + 6, y: p.y + 6))
        path.move(to: CGPoint(x: p.x + 6, y: p.y - 6)); path.addLine(to: CGPoint(x: p.x - 6, y: p.y + 6))
        ctx.stroke(path, with: .color(ignored.opacity(alpha)), style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
    }

    func drawArrow(from a: CGPoint, to b: CGPoint, color: Color, alpha: Double, dashed: Bool = false) {
        guard alpha > 0.01 else { return }
        var shaft = Path()
        shaft.move(to: a); shaft.addLine(to: b)
        let style = StrokeStyle(lineWidth: 2, lineCap: .round, dash: dashed ? [4, 3] : [])
        ctx.stroke(shaft, with: .color(color.opacity(alpha)), style: style)
        // Head
        let angle = atan2(b.y - a.y, b.x - a.x)
        var head = Path()
        for side in [-1.0, 1.0] {
            head.move(to: b)
            head.addLine(to: CGPoint(x: b.x - 8 * cos(angle + side * 0.5),
                                     y: b.y - 8 * sin(angle + side * 0.5)))
        }
        ctx.stroke(head, with: .color(color.opacity(alpha)), style: StrokeStyle(lineWidth: 2, lineCap: .round))
    }

    // MARK: Scenes

    func draw(_ preview: SettingPreview, at t: Double) {
        switch preview {
        case .blockWhileClick:    blockWhileClick(t)
        case .settleAfterRelease: settleAfterRelease(t)
        case .ignoreNudges:       ignoreNudges(t)
        case .straightLines:      straightLines(t)
        case .momentum(let on):   momentum(t, on: on)
        case .ignoreSideways:     ignoreSideways(t)
        case .tapToClick:         tapToClick(t)
        case .tapRightClick(let d): tapRightClick(t, doubleTap: d)
        case .tapZone(let d):     tapZone(t, depth: d)
        case .tapAndDrag:         tapAndDrag(t)
        case .twoFingerDrag:      twoFingerDrag(t)
        case .keyZoom:            keyZoom(t)
        }
    }

    /// Click, then slide the finger — the page holds still. Release, swipe — it scrolls.
    private func blockWhileClick(_ t: Double) {
        let pressed = t > 0.5 && t < 2.0
        let slide = 16 * seg(t, 0.8, 1.6)
        let swipe = 24 * seg(t, 2.4, 3.1)
        let scroll = 26 * seg(t, 2.45, 3.15)
        let alpha = seg(t, 0.0, 0.3) * (1 - seg(t, 3.3, 3.6))

        drawPage(pageRect, scrollY: scroll)
        drawMouse(mouseRect, pressed: pressed)
        let y = touchPoint.y - (pressed ? slide : swipe)
        drawFinger(at: CGPoint(x: touchPoint.x, y: y), pressing: pressed, alpha: alpha)
        drawCross(at: CGPoint(x: pageRect.maxX + 14, y: pageRect.minY + 12),
                  alpha: pulse(t, 0.9, 1.9))
    }

    /// Release the click; the little trailing flick as the finger leaves is ignored too.
    private func settleAfterRelease(_ t: Double) {
        let pressed = t > 0.3 && t < 1.0
        let flick = 8 * seg(t, 1.0, 1.25)
        let lift = seg(t, 1.2, 1.45)
        let swipe = 22 * seg(t, 2.3, 3.0)
        let scroll = 26 * seg(t, 2.35, 3.05)
        let alpha = seg(t, 0.0, 0.25) * (1 - lift) + seg(t, 2.1, 2.3) * (1 - seg(t, 3.1, 3.4))

        drawPage(pageRect, scrollY: scroll)
        drawMouse(mouseRect, pressed: pressed)
        let y = touchPoint.y - (t < 2 ? flick : swipe)
        drawFinger(at: CGPoint(x: touchPoint.x, y: y), pressing: pressed, alpha: alpha)
        drawCross(at: CGPoint(x: pageRect.maxX + 14, y: pageRect.minY + 12),
                  alpha: pulse(t, 1.05, 1.9))
    }

    /// Tiny wiggles do nothing; a real swipe scrolls.
    private func ignoreNudges(_ t: Double) {
        let wiggling = pulse(t, 0.2, 1.5)
        let wiggle = 3 * sin(t * 12) * wiggling
        let swipe = 30 * seg(t, 2.0, 2.9)
        let scroll = max(0, swipe - 8) * 1.3

        drawPage(pageRect, scrollY: scroll)
        drawMouse(mouseRect)
        drawFinger(at: CGPoint(x: touchPoint.x, y: touchPoint.y + wiggle - swipe),
                   alpha: seg(t, 0.0, 0.25) * (1 - seg(t, 3.1, 3.4)))
        drawCross(at: CGPoint(x: pageRect.maxX + 14, y: pageRect.minY + 12),
                  alpha: pulse(t, 0.5, 1.7))
    }

    /// The finger drifts diagonally, but the page moves in a straight line.
    private func straightLines(_ t: Double) {
        let s = seg(t, 0.4, 1.9)
        drawPage(pageRect, scrollY: 26 * s)
        drawMouse(mouseRect)
        let p = CGPoint(x: touchPoint.x + 10 * s, y: touchPoint.y - 24 * s)
        drawFinger(at: p, alpha: seg(t, 0.0, 0.3) * (1 - seg(t, 2.6, 2.9)))
        // The messy real path vs. what the page does.
        let mid = pulse(t, 0.5, 2.6)
        drawArrow(from: CGPoint(x: pageRect.maxX + 16, y: pageRect.midY + 20),
                  to: CGPoint(x: pageRect.maxX + 26, y: pageRect.midY - 14),
                  color: ignored, alpha: 0.6 * mid, dashed: true)
        drawArrow(from: CGPoint(x: pageRect.maxX + 34, y: pageRect.midY + 20),
                  to: CGPoint(x: pageRect.maxX + 34, y: pageRect.midY - 16),
                  color: accent, alpha: mid)
    }

    /// A quick flick — then the page either glides to a stop, or stops dead.
    private func momentum(_ t: Double, on: Bool) {
        let flick = 20 * seg(t, 0.3, 0.6)
        let glide = on ? 40 * easeOut(seg(t, 0.6, 2.4)) : 0
        drawPage(pageRect, scrollY: flick * 1.4 + glide)
        drawMouse(mouseRect)
        drawFinger(at: CGPoint(x: touchPoint.x, y: touchPoint.y - flick),
                   alpha: seg(t, 0.0, 0.25) * (1 - seg(t, 0.6, 0.8)))
        if !on {
            drawCross(at: CGPoint(x: pageRect.maxX + 14, y: pageRect.minY + 12),
                      alpha: pulse(t, 0.8, 1.8))
        }
    }

    /// Sideways swipes do nothing; up and down still works.
    private func ignoreSideways(_ t: Double) {
        let side = 14 * seg(t, 0.4, 1.1)
        let up = 20 * seg(t, 1.8, 2.5)
        drawPage(pageRect, scrollY: 24 * seg(t, 1.85, 2.55))
        drawMouse(mouseRect)
        drawFinger(at: CGPoint(x: touchPoint.x + side - 7, y: touchPoint.y - up),
                   alpha: seg(t, 0.0, 0.25) * (1 - seg(t, 2.6, 2.8)))
        drawCross(at: CGPoint(x: pageRect.maxX + 14, y: pageRect.minY + 12),
                  alpha: pulse(t, 0.7, 1.6))
    }

    /// A light tap clicks — no need to press the mouse down.
    private func tapToClick(_ t: Double) {
        func tap(_ start: Double) -> (finger: Double, ripple: Double, flash: Double) {
            (pulse(t, start, start + 0.22, fade: 0.08),
             seg(t, start + 0.1, start + 0.6),
             pulse(t, start + 0.12, start + 0.5))
        }
        let first = tap(0.4), second = tap(1.5)
        drawPage(pageRect, buttonFlash: max(first.flash, second.flash))
        drawMouse(mouseRect)
        for tapState in [first, second] {
            drawFinger(at: touchPoint, alpha: tapState.finger)
            drawRipple(at: touchPoint, progress: tapState.ripple)
        }
    }

    /// Right side of the surface (or a double-tap) opens the right-click menu.
    private func tapRightClick(_ t: Double, doubleTap: Bool) {
        let menuSpot = CGPoint(x: pageRect.minX + 30, y: pageRect.midY - 20)
        if doubleTap {
            // Two quick taps in the same spot, then the menu pops.
            let menu = seg(t, 0.95, 1.15) * (1 - seg(t, 2.9, 3.2))
            drawPage(pageRect)
            drawMenu(at: menuSpot, alpha: menu)
            drawMouse(mouseRect)
            drawFinger(at: touchPoint, alpha: max(pulse(t, 0.3, 0.5, fade: 0.07),
                                                  pulse(t, 0.7, 0.9, fade: 0.07)))
            drawRipple(at: touchPoint, progress: seg(t, 0.4, 0.75))
            drawRipple(at: touchPoint, progress: seg(t, 0.8, 1.15))
        } else {
            // A tap on the right strip opens the menu; a tap elsewhere is a normal click.
            let menu = seg(t, 0.55, 0.75) * (1 - seg(t, 1.8, 2.0))
            let leftClick = pulse(t, 2.35, 2.75)
            drawPage(pageRect, buttonFlash: leftClick)
            drawMenu(at: menuSpot, alpha: menu)
            drawMouse(mouseRect, rightSplit: 0.55)
            let right = CGPoint(x: mouseRect.maxX - 12, y: mouseRect.minY + 32)
            drawFinger(at: right, alpha: pulse(t, 0.4, 0.62, fade: 0.08))
            drawRipple(at: right, progress: seg(t, 0.5, 0.95))
            let left = CGPoint(x: mouseRect.minX + 16, y: mouseRect.minY + 32)
            drawFinger(at: left, alpha: pulse(t, 2.2, 2.42, fade: 0.08))
            drawRipple(at: left, progress: seg(t, 2.3, 2.75))
        }
    }

    /// Taps count in the front area; grip fingers near the back don't click.
    private func tapZone(_ t: Double, depth: Double) {
        // Mouse-only layout, larger.
        let r = CGRect(x: size.width / 2 - 42, y: 10, width: 84, height: 130)
        drawMouse(r, zoneDepth: depth)
        let front = CGPoint(x: r.midX + 8, y: r.minY + r.height * depth * 0.5)
        let back = CGPoint(x: r.minX + 12, y: r.maxY - 26)

        let goodFinger = pulse(t, 0.4, 0.65, fade: 0.08)
        drawFinger(at: front, alpha: goodFinger)
        drawRipple(at: front, progress: seg(t, 0.5, 1.0))

        let badFinger = pulse(t, 1.6, 2.3, fade: 0.1)
        drawFinger(at: back, alpha: badFinger, color: .secondary)
        drawCross(at: CGPoint(x: back.x, y: back.y - 18), alpha: pulse(t, 1.8, 2.4))
    }

    /// Tap, touch again, move the mouse — the object comes along; lift to drop.
    private func tapAndDrag(_ t: Double) {
        let move = 22 * seg(t, 1.3, 2.4)
        let holding = t > 1.35 && t < 2.9
        let secondTouch = seg(t, 0.8, 0.95) * (1 - seg(t, 2.9, 3.1))

        drawPage(pageRect, objectAt: CGPoint(x: pageRect.minX + 28 + move, y: pageRect.midY))
        let mr = mouseRect.offsetBy(dx: move * 0.6, dy: 0)
        drawMouse(mr, pressed: holding)
        // First tap
        let firstTap = pulse(t, 0.3, 0.5, fade: 0.08)
        drawFinger(at: CGPoint(x: mr.midX, y: mr.midY + 8), pressing: holding,
                   alpha: max(firstTap, secondTouch))
        drawRipple(at: CGPoint(x: mr.midX, y: mr.midY + 8), progress: seg(t, 0.4, 0.8))
    }

    /// Rest two fingers, hold a moment, then move to drag.
    private func twoFingerDrag(_ t: Double) {
        let move = 22 * seg(t, 1.5, 2.5)
        let holding = t > 1.2 && t < 3.0
        let fingers = seg(t, 0.3, 0.5) * (1 - seg(t, 3.0, 3.2))

        drawPage(pageRect, objectAt: CGPoint(x: pageRect.minX + 28 + move, y: pageRect.midY))
        let mr = mouseRect.offsetBy(dx: move * 0.6, dy: 0)
        drawMouse(mr, pressed: holding)
        let mid = CGPoint(x: mr.midX, y: mr.midY + 6)
        drawFinger(at: CGPoint(x: mid.x - 9, y: mid.y), pressing: holding, alpha: fingers)
        drawFinger(at: CGPoint(x: mid.x + 9, y: mid.y), pressing: holding, alpha: fingers)
        // The "hold a moment" dial.
        let progress = seg(t, 0.5, 1.2)
        if progress > 0, progress < 1 {
            var arc = Path()
            arc.addArc(center: CGPoint(x: mid.x, y: mid.y - 24), radius: 8,
                       startAngle: .degrees(-90), endAngle: .degrees(-90 + 360 * progress),
                       clockwise: false)
            ctx.stroke(arc, with: .color(accent), style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
        }
    }

    /// Hold a key and scroll — the page zooms instead.
    private func keyZoom(_ t: Double) {
        let held = t > 0.3 && t < 2.6
        let zoom = 1 + 0.5 * seg(t, 0.7, 1.9)
        drawPage(pageRect, zoom: zoom)
        drawMouse(mouseRect)
        drawFinger(at: CGPoint(x: touchPoint.x, y: touchPoint.y - 18 * seg(t, 0.7, 1.9)),
                   alpha: seg(t, 0.4, 0.6) * (1 - seg(t, 2.4, 2.6)))
        // Key cap.
        let key = CGRect(x: mouseRect.midX - 16, y: size.height - 26, width: 32, height: 22)
        ctx.fill(Path(roundedRect: key, cornerRadius: 5),
                 with: .color(held ? accent.opacity(0.25) : Color.primary.opacity(0.06)))
        ctx.stroke(Path(roundedRect: key, cornerRadius: 5),
                   with: .color(held ? accent.opacity(0.9) : ink), lineWidth: 1.5)
        ctx.draw(Text("⌘").font(.system(size: 13, weight: .medium)).foregroundColor(held ? accent : ink),
                 at: CGPoint(x: key.midX, y: key.midY))
    }
}
