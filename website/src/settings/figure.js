// The Magic Mouse lid and the camera the Settings renders were made with: a line-by-line port of
// Sources/CalmMouseCore/MouseFigure.swift. test/figure.test.js holds it to the app's numbers.
// Units: 1 = 1 cm. x across (right +), y along with the front toward +y, z up from the desk.

const HALF_WIDTH_MAX = 2.855;
const HALF_LENGTH = 5.675;
const HEIGHT = 2.16;
const STRAIGHT_TO = 0.55 * HALF_LENGTH;
const END_POWER = 3.0;
const FRONT_TAPER = 0.015;
const SEAM_MID = 1.26;
const SEAM_TIP = 0.5;
const EDGE_LAND = 0.08;
const BLEND_FROM = 0.35;
const BLEND_TO = 0.92;

/** The mouse turned 25° in every render, plus the turn. */
export const HOME_YAW = 25;

const clamp = (x, a, b) => Math.min(Math.max(x, a), b);
export const vec = (x, y, z) => ({ x, y, z });
const add = (a, b) => vec(a.x + b.x, a.y + b.y, a.z + b.z);
const sub = (a, b) => vec(a.x - b.x, a.y - b.y, a.z - b.z);
const times = (a, s) => vec(a.x * s, a.y * s, a.z * s);
const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross = (a, b) => vec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
const normalized = (a) => {
  const l = Math.sqrt(dot(a, a));
  return l > 0 ? times(a, 1 / l) : a;
};

/** Half the width of the outline at `l` along the mouse (front −, back +). */
function halfWidth(l) {
  const t = clamp(-l / HALF_LENGTH, 0, 1);
  const a = HALF_WIDTH_MAX * (1 - FRONT_TAPER * t * t);
  const al = Math.abs(l);
  if (al <= STRAIGHT_TO) return a;
  const v = Math.min((al - STRAIGHT_TO) / (HALF_LENGTH - STRAIGHT_TO), 1);
  return a * Math.pow(Math.max(1 - Math.pow(v, END_POWER), 0), 1 / END_POWER);
}

/** Height of the seam where the white lid meets the silver tray. */
function seam(l) {
  const u = Math.min(Math.abs(l) / HALF_LENGTH, 1);
  return SEAM_TIP + (SEAM_MID - SEAM_TIP) * Math.pow(Math.max(1 - u * u, 0), 0.95);
}

/** The lid's height at fraction `r` of the way out to the outline, on the ray meeting it at `bl`. */
function dome(r, bl) {
  const edge = seam(bl) + EDGE_LAND;
  const w = Math.min((bl / HALF_LENGTH) * (bl / HALF_LENGTH), 1);
  const k = 0.8 + 0.08 * w;
  const q = 40 + 50 * w;
  const g = Math.pow(Math.max(1 - r * r, 0), k) * Math.pow(Math.max(1 - Math.pow(r, q), 0), 0.5);
  return edge + (HEIGHT - edge) * g;
}

/** How far out (x, l) sits on its ray from the middle, and where that ray meets the outline. */
function gauge(x, l) {
  if (Math.abs(x) < 1e-9 && Math.abs(l) < 1e-9) return { r: 0, bl: HALF_LENGTH };
  const inside = (s) => Math.abs(l) * s < HALF_LENGTH && halfWidth(l * s) > Math.abs(x) * s;
  let lo = 1;
  let hi = 1;
  while (inside(hi)) hi *= 1.5;
  for (let i = 0; i < 50; i++) {
    const mid = (lo + hi) / 2;
    if (inside(mid)) lo = mid;
    else hi = mid;
  }
  const s = (lo + hi) / 2;
  return { r: 1 / s, bl: l * s };
}

/** The measured ridge along the middle, front to back. */
function ridge(l) {
  const u = Math.min(Math.abs(l) / HALF_LENGTH, 1);
  const tip = SEAM_TIP + EDGE_LAND;
  return tip + (HEIGHT - tip) * Math.pow(Math.max(1 - u * u, 0), 0.85);
}

/** A smooth crown from the two measured profiles: the ridge along, the section across. */
function crown(x, l) {
  const w = halfWidth(l);
  const t = w > 1e-6 ? Math.min(Math.abs(x) / w, 1) : 1;
  const edge = seam(l) + EDGE_LAND;
  return edge + (ridge(l) - edge) * Math.pow(Math.max(1 - t * t, 0), 0.62);
}

/** The lid's height over (x, l): the crown in the middle, blending into the dome near the rim. */
function lidHeight(x, l) {
  const g = gauge(x, l);
  const r = Math.min(g.r, 1);
  const k = clamp((r - BLEND_FROM) / (BLEND_TO - BLEND_FROM), 0, 1);
  const w = k * k * k * (k * (6 * k - 15) + 10);
  return crown(x, l) * (1 - w) + dome(r, g.bl) * w;
}

/** The point on the lid at `u` across (−1 left … 1 right) and `v` along (−1 front … 1 back). */
export function surface(u, v) {
  const l = clamp(v, -1, 1) * HALF_LENGTH;
  const x = clamp(u, -1, 1) * halfWidth(l);
  return vec(x, -l, lidHeight(x, l));
}

/** Which way the lid faces at (u, v). */
export function surfaceNormal(u, v) {
  const e = 0.02;
  const du = sub(surface(u + e, v), surface(u - e, v));
  const dv = sub(surface(u, v + e), surface(u, v - e));
  const n = normalized(cross(du, dv));
  return n.z < 0 ? times(n, -1) : n;
}

/** The desk renders' camera: behind the mouse at 40°, 39 cm away, a 105 mm lens on a 36 mm
 * sensor, the mouse turned 25° plus `yaw`. `frame` is the square render's side in points, drawn
 * with its top-left corner at (originX, originY). */
export function deskCamera({ yaw = 0, frame, originX = 0, originY = 0 }) {
  const elevation = (40 * Math.PI) / 180;
  const distance = 39;
  const target = vec(0, 0, 0.75);
  const eye = vec(target.x, target.y - distance * Math.cos(elevation), target.z + distance * Math.sin(elevation));
  const forward = normalized(sub(target, eye));
  const right = normalized(cross(forward, vec(0, 0, 1)));
  const up = cross(right, forward);
  const focal = (105 / 36) * frame;
  const centreX = originX + frame / 2;
  const centreY = originY + frame / 2;
  const a = ((HOME_YAW + yaw) * Math.PI) / 180;
  const cosA = Math.cos(a);
  const sinA = Math.sin(a);

  /** Where the mouse's point `p` lands, with the whole mouse slid `slide` cm to the right. */
  function project(p, slide = 0) {
    const world = vec(p.x * cosA - p.y * sinA + slide, p.x * sinA + p.y * cosA, p.z);
    const d = sub(world, eye);
    const depth = dot(d, forward);
    return { x: centreX + (focal * dot(d, right)) / depth, y: centreY - (focal * dot(d, up)) / depth };
  }

  /** A circle of `radius` cm lying on the lid at (u, v), as seen. */
  function disc(u, v, radius, slide = 0, steps = 28) {
    const p = surface(u, v);
    const n = surfaceNormal(u, v);
    const t1 = normalized(cross(n, Math.abs(n.z) > 0.9 ? vec(1, 0, 0) : vec(0, 0, 1)));
    const t2 = cross(n, t1);
    const points = [];
    for (let i = 0; i < steps; i++) {
      const angle = (2 * Math.PI * i) / steps;
      points.push(project(add(add(p, times(t1, radius * Math.cos(angle))), times(t2, radius * Math.sin(angle))), slide));
    }
    return points;
  }

  return { project, disc };
}
