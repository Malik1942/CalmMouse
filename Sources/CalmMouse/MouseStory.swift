import Foundation

/// A finger on the lid: `u` across (−1 left … 1 right), `v` along (−1 front … 1 back).
struct StoryTouch: Equatable {
    var u: Double
    var v: Double
    var alpha: Double
    var pressing = false
    var ignored = false
}

struct StoryRipple: Equatable {
    var u: Double
    var v: Double
    var progress: Double
}

enum StoryZone: Equatable {
    /// The front `depth` of the lid counts taps.
    case front(depth: Double)
    /// Right of `split` (in u) is the right button.
    case rightSide(split: Double)
}

/// The little page beside the mouse: what the setting does to what's on screen.
struct PageState: Equatable {
    struct Arrows: Equatable {
        enum Kind: Equatable { case straight, diagonal, flipped(Bool) }
        var kind: Kind
        var drift = 0.0
        var page = 0.0
    }
    var scrollY = 0.0
    var scrollX = 0.0
    var zoom = 1.0
    /// A button under the pointer, for the stories that click.
    var button = false
    var flash = 0.0
    var object: Double?
    var menu = 0.0
    var cross = 0.0
    var arrows: Arrows?
    var key: Double?
}

struct StoryFrame: Equatable {
    struct Dial: Equatable { var u: Double; var v: Double; var progress: Double }
    struct Mark: Equatable { var u: Double; var v: Double; var alpha: Double }
    var touches: [StoryTouch] = []
    var ripples: [StoryRipple] = []
    var zone: StoryZone?
    var dial: Dial?
    var crossOnLid: Mark?
    var pressed = false
    /// How far the whole mouse has slid to the right, in cm.
    var slide = 0.0
    var page = PageState()
}

/// The Devices column plays these on the rendered mouse. They are the same stories, with the same
/// timings, as the small top-view previews (PreviewScene): one px of that 62×106 top view is
/// 1/31 of `u` and 1/53 of `v`.
extension SettingPreview {
    func frame(at t: Double) -> StoryFrame {
        switch self {
        case .blockWhileClick: return Story.blockWhileClick(t)
        case .settleAfterRelease: return Story.settleAfterRelease(t)
        case .ignoreNudges: return Story.ignoreNudges(t)
        case .straightLines: return Story.straightLines(t)
        case .momentum(let on): return Story.momentum(t, on: on)
        case .ignoreSideways: return Story.ignoreSideways(t)
        case .tapToClick: return Story.tapToClick(t)
        case .tapRightClick(let doubleTap): return doubleTap ? Story.doubleTapRightClick(t) : Story.rightSideClick(t)
        case .tapZone(let depth): return Story.tapZone(t, depth: depth)
        case .tapAndDrag: return Story.tapAndDrag(t)
        case .twoFingerDrag: return Story.twoFingerDrag(t)
        case .keyZoom: return Story.keyZoom(t)
        case .reverseScroll(let on): return Story.reverseScroll(t, on: on)
        }
    }
}

private enum Story {
    /// Where a finger naturally rests: the middle, a little back.
    static let rest = (u: 0.0, v: 0.15)

    /// 0→1 smoothstep between two moments of the loop.
    static func seg(_ t: Double, _ a: Double, _ b: Double) -> Double {
        let x = min(max((t - a) / (b - a), 0), 1)
        return x * x * (3 - 2 * x)
    }
    static func easeOut(_ x: Double) -> Double { let c = min(max(x, 0), 1); return 1 - (1 - c) * (1 - c) }
    /// 1 inside [a,b] with soft edges, 0 outside.
    static func pulse(_ t: Double, _ a: Double, _ b: Double, fade: Double = 0.15) -> Double {
        seg(t, a, a + fade) * (1 - seg(t, b - fade, b))
    }
    static func finger(along px: Double = 0, across dx: Double = 0, alpha: Double, pressing: Bool = false) -> StoryTouch {
        StoryTouch(u: rest.u + dx / 31, v: rest.v - px / 53, alpha: alpha, pressing: pressing)
    }

    /// Click, then slide the finger: the page holds still. Release and swipe: it scrolls.
    static func blockWhileClick(_ t: Double) -> StoryFrame {
        let pressed = t > 0.5 && t < 2.0
        let slide = 16 * seg(t, 0.8, 1.6), swipe = 24 * seg(t, 2.4, 3.1)
        var f = StoryFrame(pressed: pressed)
        f.touches = [finger(along: pressed ? slide : swipe, alpha: seg(t, 0, 0.3) * (1 - seg(t, 3.3, 3.6)), pressing: pressed)]
        f.page.scrollY = 26 * seg(t, 2.45, 3.15)
        f.page.cross = pulse(t, 0.9, 1.9)
        return f
    }

    /// Release the click; the little flick as the finger leaves is ignored too.
    static func settleAfterRelease(_ t: Double) -> StoryFrame {
        let pressed = t > 0.3 && t < 1.0
        let flick = 8 * seg(t, 1.0, 1.25), lift = seg(t, 1.2, 1.45), swipe = 22 * seg(t, 2.3, 3.0)
        let alpha = seg(t, 0, 0.25) * (1 - lift) + seg(t, 2.1, 2.3) * (1 - seg(t, 3.1, 3.4))
        var f = StoryFrame(pressed: pressed)
        f.touches = [finger(along: t < 2 ? flick : swipe, alpha: alpha, pressing: pressed)]
        f.page.scrollY = 26 * seg(t, 2.35, 3.05)
        f.page.cross = pulse(t, 1.05, 1.9)
        return f
    }

    /// Tiny wiggles do nothing; a real swipe scrolls.
    static func ignoreNudges(_ t: Double) -> StoryFrame {
        let wiggle = 3 * sin(t * 12) * pulse(t, 0.2, 1.5), swipe = 30 * seg(t, 2.0, 2.9)
        var f = StoryFrame()
        f.touches = [finger(along: swipe - wiggle, alpha: seg(t, 0, 0.25) * (1 - seg(t, 3.1, 3.4)))]
        f.page.scrollY = max(0, swipe - 8) * 1.3
        f.page.cross = pulse(t, 0.5, 1.7)
        return f
    }

    /// The finger drifts diagonally, but the page moves in a straight line.
    static func straightLines(_ t: Double) -> StoryFrame {
        let s = seg(t, 0.4, 1.9), shown = pulse(t, 0.5, 2.6)
        var f = StoryFrame()
        f.touches = [finger(along: 24 * s, across: 10 * s, alpha: seg(t, 0, 0.3) * (1 - seg(t, 2.6, 2.9)))]
        f.page.scrollY = 26 * s
        f.page.arrows = .init(kind: .straight, drift: 0.6 * shown, page: shown)
        return f
    }

    /// A quick flick: then the page either glides to a stop, or stops dead.
    static func momentum(_ t: Double, on: Bool) -> StoryFrame {
        let flick = 20 * seg(t, 0.3, 0.6), glide = on ? 40 * easeOut(seg(t, 0.6, 2.4)) : 0
        var f = StoryFrame()
        f.touches = [finger(along: flick, alpha: seg(t, 0, 0.25) * (1 - seg(t, 0.6, 0.8)))]
        f.page.scrollY = flick * 1.4 + glide
        f.page.cross = on ? 0 : pulse(t, 0.8, 1.8)
        return f
    }

    /// Sideways swipes do nothing; up and down still works.
    static func ignoreSideways(_ t: Double) -> StoryFrame {
        let side = 14 * seg(t, 0.4, 1.1), up = 20 * seg(t, 1.8, 2.5)
        var f = StoryFrame()
        f.touches = [finger(along: up, across: side - 7, alpha: seg(t, 0, 0.25) * (1 - seg(t, 2.6, 2.8)))]
        f.page.scrollY = 24 * seg(t, 1.85, 2.55)
        f.page.cross = pulse(t, 0.7, 1.6)
        return f
    }

    /// A light tap clicks: no need to press the mouse down.
    static func tapToClick(_ t: Double) -> StoryFrame {
        var f = StoryFrame()
        f.page.button = true
        var alpha = 0.0
        for start in [0.4, 1.5] {
            alpha = max(alpha, pulse(t, start, start + 0.22, fade: 0.08))
            f.ripples.append(StoryRipple(u: rest.u, v: rest.v, progress: seg(t, start + 0.1, start + 0.6)))
            f.page.flash = max(f.page.flash, pulse(t, start + 0.12, start + 0.5))
        }
        f.touches = [finger(alpha: alpha)]
        return f
    }

    /// A tap on the right strip opens the menu; a tap elsewhere is a normal click.
    static func rightSideClick(_ t: Double) -> StoryFrame {
        let right = (u: 19.0 / 31, v: -21.0 / 53), left = (u: -15.0 / 31, v: -21.0 / 53)
        var f = StoryFrame(zone: .rightSide(split: 0.1))
        f.touches = [StoryTouch(u: right.u, v: right.v, alpha: pulse(t, 0.4, 0.62, fade: 0.08)),
                     StoryTouch(u: left.u, v: left.v, alpha: pulse(t, 2.2, 2.42, fade: 0.08))]
        f.ripples = [StoryRipple(u: right.u, v: right.v, progress: seg(t, 0.5, 0.95)),
                     StoryRipple(u: left.u, v: left.v, progress: seg(t, 2.3, 2.75))]
        f.page.menu = seg(t, 0.55, 0.75) * (1 - seg(t, 1.8, 2.0))
        f.page.button = true
        f.page.flash = pulse(t, 2.35, 2.75)
        return f
    }

    /// Two quick taps in the same spot, then the menu pops.
    static func doubleTapRightClick(_ t: Double) -> StoryFrame {
        var f = StoryFrame()
        f.touches = [finger(alpha: max(pulse(t, 0.3, 0.5, fade: 0.07), pulse(t, 0.7, 0.9, fade: 0.07)))]
        f.ripples = [StoryRipple(u: rest.u, v: rest.v, progress: seg(t, 0.4, 0.75)),
                     StoryRipple(u: rest.u, v: rest.v, progress: seg(t, 0.8, 1.15))]
        f.page.menu = seg(t, 0.95, 1.15) * (1 - seg(t, 2.9, 3.2))
        return f
    }

    /// Taps count in the front area; grip fingers near the back don't click.
    static func tapZone(_ t: Double, depth: Double) -> StoryFrame {
        let front = (u: 8.0 / 42, v: -1 + depth), back = (u: -30.0 / 42, v: 1 - 52.0 / 130)
        var f = StoryFrame(zone: .front(depth: depth))
        f.touches = [StoryTouch(u: front.u, v: front.v, alpha: pulse(t, 0.4, 0.65, fade: 0.08)),
                     StoryTouch(u: back.u, v: back.v, alpha: pulse(t, 1.6, 2.3, fade: 0.1), ignored: true)]
        f.ripples = [StoryRipple(u: front.u, v: front.v, progress: seg(t, 0.5, 1.0))]
        f.crossOnLid = .init(u: back.u, v: back.v - 36.0 / 130, alpha: pulse(t, 1.8, 2.4))
        f.page.button = true
        f.page.flash = pulse(t, 0.55, 0.95)
        return f
    }

    /// Double tap, stay down on the second, move the mouse: the object comes along; lift to drop.
    static func tapAndDrag(_ t: Double) -> StoryFrame {
        let move = 22 * seg(t, 1.3, 2.4), holding = t > 1.35 && t < 2.9
        let second = seg(t, 0.8, 0.95) * (1 - seg(t, 2.9, 3.1)), first = pulse(t, 0.3, 0.5, fade: 0.08)
        var f = StoryFrame(pressed: holding, slide: move * 0.06)
        f.touches = [finger(alpha: max(first, second), pressing: holding)]
        f.ripples = [StoryRipple(u: rest.u, v: rest.v, progress: seg(t, 0.4, 0.8)),
                     StoryRipple(u: rest.u, v: rest.v, progress: seg(t, 0.85, 1.25))]
        f.page.object = move
        return f
    }

    /// Rest two fingers, hold a moment, then move to drag.
    static func twoFingerDrag(_ t: Double) -> StoryFrame {
        let move = 22 * seg(t, 1.5, 2.5), holding = t > 1.2 && t < 3.0
        let alpha = seg(t, 0.3, 0.5) * (1 - seg(t, 3.0, 3.2)), v = 6.0 / 53
        var f = StoryFrame(pressed: holding, slide: move * 0.06)
        f.touches = [StoryTouch(u: -9.0 / 31, v: v, alpha: alpha, pressing: holding),
                     StoryTouch(u: 9.0 / 31, v: v, alpha: alpha, pressing: holding)]
        f.dial = .init(u: 0, v: v - 24.0 / 53, progress: seg(t, 0.5, 1.2))
        f.page.object = move
        return f
    }

    /// Hold a key and scroll: the page zooms instead.
    static func keyZoom(_ t: Double) -> StoryFrame {
        let held = t > 0.3 && t < 2.6, s = seg(t, 0.7, 1.9)
        var f = StoryFrame()
        f.touches = [finger(along: 18 * s, alpha: seg(t, 0.4, 0.6) * (1 - seg(t, 2.4, 2.6)))]
        f.page.zoom = 1 + 0.5 * s
        f.page.key = held ? 1 : 0
        return f
    }

    /// The same swipe scrolls the page the other way.
    static func reverseScroll(_ t: Double, on: Bool) -> StoryFrame {
        let s = seg(t, 0.5, 1.5)
        var f = StoryFrame()
        f.touches = [finger(along: 26 * s - 6, alpha: seg(t, 0.1, 0.4) * (1 - seg(t, 1.6, 1.9)))]
        f.page.scrollY = (on ? -26 : 26) * seg(t, 0.55, 1.6)
        f.page.arrows = .init(kind: .flipped(on), page: pulse(t, 0.6, 2.7))
        return f
    }
}

/// The big ring around the whole mouse: a one-time moment, never a loop. Its motion follows
/// Apple's AirPods rings: it draws itself round, lobes of longer ticks travel round it while the
/// colour drifts between cyan and blue the other way, and once in the middle it blooms out into
/// a halo of dots before settling and fading.
enum PresenceRing {
    struct Frame: Equatable {
        /// How much of the ring has been drawn, from the front round.
        var sweep: Double
        /// In cm, on the desk around the mouse's middle: the ticks' outer ends.
        var radius: Double
        var alpha: Double
        /// How far the lobes and colours have travelled, in radians.
        var phase: Double
        /// 0…1: how far the ring has bloomed out into dots.
        var bloom: Double
    }

    struct Tick: Equatable {
        /// In cm, inward from the ring.
        var length: Double
        /// In cm, outward from the ring: the bloom's dots.
        var bloom: Double
        /// 0 deep blue … 1 cyan.
        var mix: Double
    }

    static let duration = 3.0

    static func frame(at t: Double) -> Frame? {
        guard t >= 0, t <= duration else { return nil }
        func seg(_ a: Double, _ b: Double) -> Double {
            let x = min(max((t - a) / (b - a), 0), 1)
            return x * x * (3 - 2 * x)
        }
        let open = 1 - pow(1 - min(t, 1), 2)
        return Frame(sweep: seg(0, 0.8), radius: 5.05 + 0.25 * open, alpha: seg(0, 0.3) * (1 - seg(2.4, 3.0)),
                     phase: 2.4 * t, bloom: seg(1.0, 1.6) * (1 - seg(1.9, 2.5)))
    }

    /// One tick of the ring at `angle` (radians round it).
    static func tick(_ f: Frame, angle: Double) -> Tick {
        let lobe = pow(0.5 + 0.5 * cos(2 * (angle - f.phase)), 1.6)
        return Tick(length: 0.1 + 0.32 * lobe,
                    bloom: f.bloom * (0.25 + 0.38 * lobe),
                    mix: 0.5 + 0.5 * cos(angle + 1.3 * f.phase + 0.8))
    }
}
