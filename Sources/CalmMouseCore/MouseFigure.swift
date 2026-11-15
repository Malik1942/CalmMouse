import Foundation

/// A point or direction in the mouse's own space: 1 unit = 1 cm, x across (right +), y along with
/// the front toward +y, z up from the desk.
public struct Vec3: Equatable {
    public var x, y, z: Double
    public init(_ x: Double, _ y: Double, _ z: Double) { self.x = x; self.y = y; self.z = z }

    static func + (a: Vec3, b: Vec3) -> Vec3 { Vec3(a.x + b.x, a.y + b.y, a.z + b.z) }
    static func - (a: Vec3, b: Vec3) -> Vec3 { Vec3(a.x - b.x, a.y - b.y, a.z - b.z) }
    static func * (a: Vec3, s: Double) -> Vec3 { Vec3(a.x * s, a.y * s, a.z * s) }
    func dot(_ b: Vec3) -> Double { x * b.x + y * b.y + z * b.z }
    func cross(_ b: Vec3) -> Vec3 { Vec3(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x) }
    var normalized: Vec3 { let l = (x * x + y * y + z * z).squareRoot(); return l > 0 ? self * (1 / l) : self }
}

/// The Magic Mouse lid as the Settings renders were modelled in Blender (magicmouse.py): the
/// published 113.5 × 57.1 × 21.6 mm, its outline and side profile measured off Apple's
/// orthographic product views. Touches in Settings are drawn on this surface.
public enum MagicMouseFigure {
    static let halfWidthMax = 2.855, halfLength = 5.675, height = 2.16
    static let straightTo = 0.55 * halfLength, endPower = 3.0, frontTaper = 0.015
    static let seamMid = 1.26, seamTip = 0.50, edgeLand = 0.08

    /// Half the width of the outline at `l` along the mouse (front −, back +).
    static func halfWidth(_ l: Double) -> Double {
        let t = min(max(-l / halfLength, 0), 1)
        let a = halfWidthMax * (1 - frontTaper * t * t)
        let al = abs(l)
        if al <= straightTo { return a }
        let v = min((al - straightTo) / (halfLength - straightTo), 1)
        return a * pow(max(1 - pow(v, endPower), 0), 1 / endPower)
    }

    /// Height of the seam where the white lid meets the silver tray.
    static func seam(_ l: Double) -> Double {
        let u = min(abs(l) / halfLength, 1)
        return seamTip + (seamMid - seamTip) * pow(max(1 - u * u, 0), 0.95)
    }

    /// The lid's height at fraction `r` of the way from the middle out to the outline, on the ray
    /// that meets the outline at `bl` along. Rolls over evenly into a narrow edge land.
    static func dome(_ r: Double, _ bl: Double) -> Double {
        let edge = seam(bl) + edgeLand
        let w = min((bl / halfLength) * (bl / halfLength), 1)
        let k = 0.80 + 0.08 * w, q = 40 + 50 * w
        let g = pow(max(1 - r * r, 0), k) * pow(max(1 - pow(r, q), 0), 0.5)
        return edge + (height - edge) * g
    }

    /// How far out (x, l) sits on its ray from the middle, and where that ray meets the outline.
    static func gauge(_ x: Double, _ l: Double) -> (r: Double, bl: Double) {
        if abs(x) < 1e-9 && abs(l) < 1e-9 { return (0, halfLength) }
        func inside(_ s: Double) -> Bool { abs(l) * s < halfLength && halfWidth(l * s) > abs(x) * s }
        var lo = 1.0, hi = 1.0
        while inside(hi) { hi *= 1.5 }
        for _ in 0..<50 {
            let mid = (lo + hi) / 2
            if inside(mid) { lo = mid } else { hi = mid }
        }
        let s = (lo + hi) / 2
        return (1 / s, l * s)
    }

    /// The point on the lid at `u` across (−1 left … 1 right) and `v` along (−1 front … 1 back).
    public static func surface(u: Double, v: Double) -> Vec3 {
        let l = min(max(v, -1), 1) * halfLength
        let x = min(max(u, -1), 1) * halfWidth(l)
        let g = gauge(x, l)
        return Vec3(x, -l, dome(min(g.r, 1), g.bl))
    }

    /// Which way the lid faces at (u, v).
    public static func surfaceNormal(u: Double, v: Double) -> Vec3 {
        let e = 0.02
        let du = surface(u: u + e, v: v) - surface(u: u - e, v: v)
        let dv = surface(u: u, v: v + e) - surface(u: u, v: v - e)
        let n = du.cross(dv).normalized
        return n.z < 0 ? n * -1 : n
    }
}

/// The camera of the desk renders: behind the mouse at 40°, 39 cm away, a 105 mm lens on a 36 mm
/// sensor, the mouse turned 25° plus `yaw` (degrees, counter-clockwise seen from above). `frame`
/// is the square render's side in points, drawn with its top-left corner at the origin.
public struct DeskCamera {
    public static let homeYaw = 25.0
    let eye, forward, right, up: Vec3
    let focal, centreX, centreY, cosA, sinA: Double

    public init(yaw: Double = 0, frame: Double, originX: Double = 0, originY: Double = 0) {
        let elevation = 40.0 * .pi / 180, distance = 39.0
        let target = Vec3(0, 0, 0.75)
        eye = Vec3(target.x, target.y - distance * cos(elevation), target.z + distance * sin(elevation))
        forward = (target - eye).normalized
        right = forward.cross(Vec3(0, 0, 1)).normalized
        up = right.cross(forward)
        focal = 105.0 / 36.0 * frame
        centreX = originX + frame / 2
        centreY = originY + frame / 2
        let a = (Self.homeYaw + yaw) * .pi / 180
        cosA = cos(a); sinA = sin(a)
    }

    /// Where the mouse's point `p` lands, with the whole mouse slid `slide` cm to the right.
    public func project(_ p: Vec3, slide: Double = 0) -> (x: Double, y: Double) {
        let world = Vec3(p.x * cosA - p.y * sinA + slide, p.x * sinA + p.y * cosA, p.z)
        let d = world - eye, depth = d.dot(forward)
        return (centreX + focal * d.dot(right) / depth, centreY - focal * d.dot(up) / depth)
    }

    /// A circle of `radius` cm lying on the lid at (u, v), as seen: an ellipse, foreshortened.
    public func disc(u: Double, v: Double, radius: Double, slide: Double = 0, steps: Int = 28) -> [(x: Double, y: Double)] {
        let p = MagicMouseFigure.surface(u: u, v: v), n = MagicMouseFigure.surfaceNormal(u: u, v: v)
        let t1 = n.cross(abs(n.z) > 0.9 ? Vec3(1, 0, 0) : Vec3(0, 0, 1)).normalized, t2 = n.cross(t1)
        return (0..<steps).map { i in
            let a = 2 * Double.pi * Double(i) / Double(steps)
            return project(p + t1 * (radius * cos(a)) + t2 * (radius * sin(a)), slide: slide)
        }
    }
}
