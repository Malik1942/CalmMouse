// The Magic Mouse on the left of Devices: the app's rendered frames, turned by dragging, with the
// pointed-at setting's story on its lid and the page card under it. A port of the drawing in
// Sources/CalmMouse/MouseShowcase.swift (MouseStageView, MouseStageDrawing, PageCardDrawing);
// turn.js holds the motion. Coordinates are points: the stage's 290 × 226, as in the app.

import { t } from "./i18n.js";
import { HOME_YAW, deskCamera, surface, vec } from "./figure.js";
import { FRAMES } from "./render.js";
import { PresenceRing, StoryFade } from "./stories.js";
import { FRAME_COUNT, createTurn } from "./turn.js";

export const STAGE = Object.freeze({
  width: 290,
  height: 226,
  frameSide: 720 * 0.452,
  frameOriginX: 145 - 360 * 0.452,
  frameOriginY: 112 - 389.4 * 0.452,
  cardWidth: 250,
  cardHeight: 104,
  cardTop: 226 + 8,
  column: 320,
});
const CANVAS = { width: STAGE.column, height: STAGE.cardTop + STAGE.cardHeight };
const TOUCH_BLUE = { r: 0.33, g: 0.64, b: 0.98, a: 1 };
const GREY = { r: 0.5, g: 0.5, b: 0.5, a: 1 };
const WHITE = { r: 1, g: 1, b: 1, a: 1 };
const BLACK = { r: 0, g: 0, b: 0, a: 1 };

const rgba = (c, opacity = 1) =>
  `rgba(${Math.round(c.r * 255)}, ${Math.round(c.g * 255)}, ${Math.round(c.b * 255)}, ${c.a * opacity})`;

/** The window's colours, read from its --sw-* tokens, for the appearance being shown. */
function readColors(el) {
  const style = getComputedStyle(el);
  const probe = document.createElement("canvas").getContext("2d");
  const parse = (name) => {
    probe.fillStyle = "#000";
    probe.fillStyle = style.getPropertyValue(name).trim();
    const v = probe.fillStyle;
    if (v.startsWith("#")) {
      return { r: parseInt(v.slice(1, 3), 16) / 255, g: parseInt(v.slice(3, 5), 16) / 255, b: parseInt(v.slice(5, 7), 16) / 255, a: 1 };
    }
    const [r, g, b, a = 1] = v.match(/[\d.]+/g).map(Number);
    return { r: r / 255, g: g / 255, b: b / 255, a };
  };
  return {
    accent: parse("--sw-accent"),
    ink: parse("--sw-label"),
    paper: parse("--sw-paper"),
    secondary: parse("--sw-secondary"),
    font: style.getPropertyValue("--font-sans"),
  };
}

// ---------- Layers: Core Graphics' transparency layers, as offscreen canvases ----------
const pool = [];
/** Draws `body` at `alpha` as one layer, so overlapping shapes inside don't show through each
 * other. `depth` picks the offscreen canvas, so layers can nest. */
function withLayer(g, alpha, body, depth = 0) {
  if (alpha <= 0.001) return;
  if (alpha >= 0.999) {
    body(g, depth);
    return;
  }
  const target = g.canvas;
  const layer = (pool[depth] ??= document.createElement("canvas"));
  if (layer.width !== target.width || layer.height !== target.height) {
    layer.width = target.width;
    layer.height = target.height;
  }
  const h = layer.getContext("2d");
  h.setTransform(1, 0, 0, 1, 0, 0);
  h.clearRect(0, 0, layer.width, layer.height);
  h.setTransform(g.getTransform());
  body(h, depth + 1);
  g.save();
  g.setTransform(1, 0, 0, 1, 0, 0);
  g.globalAlpha = alpha;
  g.drawImage(layer, 0, 0);
  g.restore();
}

// ---------- Helpers ----------
function polyline(g, points, closed) {
  g.beginPath();
  points.forEach((p, i) => (i ? g.lineTo(p.x, p.y) : g.moveTo(p.x, p.y)));
  if (closed) g.closePath();
}
function line(g, x0, y0, x1, y1, style, width, cap = "butt") {
  g.save();
  g.beginPath();
  g.moveTo(x0, y0);
  g.lineTo(x1, y1);
  g.strokeStyle = style;
  g.lineWidth = width;
  g.lineCap = cap;
  g.stroke();
  g.restore();
}
function rounded(g, r, radius) {
  g.beginPath();
  if (typeof g.roundRect === "function") {
    g.roundRect(r.x, r.y, r.w, r.h, radius);
    return;
  }
  // Safari 15 and Firefox before 112 have no roundRect.
  const k = Math.min(radius, r.w / 2, r.h / 2);
  g.moveTo(r.x + k, r.y);
  g.arcTo(r.x + r.w, r.y, r.x + r.w, r.y + r.h, k);
  g.arcTo(r.x + r.w, r.y + r.h, r.x, r.y + r.h, k);
  g.arcTo(r.x, r.y + r.h, r.x, r.y, k);
  g.arcTo(r.x, r.y, r.x + r.w, r.y, k);
  g.closePath();
}
/** A drop shadow as the app's Pen.shadowed sets it: blur `radius × 2`, `y` points down. Canvas
 * shadows ignore the transform, so they're scaled by the pixel ratio here. */
function shadow(g, pen, color, opacity, radius, y) {
  g.shadowColor = rgba(color, opacity);
  g.shadowBlur = radius * 2 * pen.dpr;
  g.shadowOffsetX = 0;
  g.shadowOffsetY = y * pen.dpr;
}
/** `rect` filled with `color` fading to clear from y0 to y1. */
function fade(g, rect, color, y0, y1) {
  const gradient = g.createLinearGradient(0, y0, 0, y1);
  gradient.addColorStop(0, rgba(color));
  gradient.addColorStop(1, rgba(color, 0));
  g.save();
  g.fillStyle = gradient;
  g.fillRect(rect.x, rect.y, rect.w, rect.h);
  g.restore();
}

// ---------- The mouse and its lid ----------
function drawStage(g, state, image, pen) {
  const yaw = (state.turnFrame ?? 0) * 5;
  const camera = deskCamera({ yaw, frame: STAGE.frameSide, originX: STAGE.frameOriginX, originY: STAGE.frameOriginY });
  // A row just left takes the mouse back home with its fade.
  const slide = (state.story?.slide ?? 0) + (state.outgoing?.slide ?? 0) * state.outgoingAlpha;
  const origin = vec(0, 0, 0);
  const dx = slide === 0 ? 0 : camera.project(origin, slide).x - camera.project(origin).x;
  if (state.ring) drawRing(g, state.ring, camera, yaw);
  if (image) {
    g.imageSmoothingQuality = "high";
    g.drawImage(image, STAGE.frameOriginX + dx, STAGE.frameOriginY + (state.story?.pressed ? 0.8 : 0), STAGE.frameSide, STAGE.frameSide);
  }
  if (state.outgoing) withLayer(g, state.outgoingAlpha, (h) => drawMarks(h, state.outgoing, camera, slide, pen));
  if (state.story) withLayer(g, state.storyAlpha, (h) => drawMarks(h, state.story, camera, slide, pen));
}

function drawMarks(g, f, camera, slide, pen) {
  drawZone(g, f, camera, slide, pen);
  for (const r of f.ripples) if (r.progress > 0 && r.progress < 1) drawRipple(g, r, camera, slide);
  if (f.dial && f.dial.progress > 0 && f.dial.progress < 1) {
    const ring = camera.disc(f.dial.u, f.dial.v, 0.42, slide, 40);
    g.save();
    polyline(g, ring.slice(0, Math.max(2, Math.round(40 * f.dial.progress))), false);
    g.strokeStyle = rgba(pen.colors.accent);
    g.lineWidth = 1.8;
    g.lineCap = "round";
    g.stroke();
    g.restore();
  }
  for (const t of f.touches) if (t.alpha > 0.01) drawTouch(g, t, camera, slide);
  if (f.crossOnLid && f.crossOnLid.alpha > 0.01) drawIgnoredMark(g, f.crossOnLid, camera, slide, pen);
}

/** The ring around the whole mouse, lying on the desk, under the mouse. */
function drawRing(g, ring, camera, yaw) {
  const n = 144;
  const shown = Math.round(n * ring.sweep);
  if (shown <= 0) return;
  const start = ((-90 - HOME_YAW - yaw) * Math.PI) / 180; // the side nearest you
  const at = (r, a) => camera.project(vec(r * Math.cos(a), r * Math.sin(a), 0));
  g.save();
  g.lineCap = "round";
  g.lineWidth = 1.1;
  for (let i = 0; i < shown; i++) {
    const a = start + (2 * Math.PI * i) / n;
    const tick = PresenceRing.tick(ring, a);
    const outer = at(ring.radius, a);
    const edge = Math.min(1, Math.max(0, Math.min(outer.x, STAGE.width - outer.x) / 18));
    const alpha = ring.alpha * edge;
    if (alpha <= 0.01) continue;
    const color = { r: 0.16 + 0.09 * tick.mix, g: 0.42 + 0.42 * tick.mix, b: 1 - 0.06 * tick.mix, a: 1 };
    const strength = 0.5 + (0.5 * (tick.length - 0.1)) / 0.32;
    const inner = at(ring.radius - tick.length, a);
    g.strokeStyle = rgba(color, 0.92 * alpha * strength);
    g.beginPath();
    g.moveTo(outer.x, outer.y);
    g.lineTo(inner.x, inner.y);
    g.stroke();
    const rows = Math.floor(tick.bloom / 0.11);
    for (let j = 1; j <= rows; j++) {
      const p = at(ring.radius + j * 0.11, a);
      const k = 1 - j / (rows + 1);
      g.fillStyle = rgba(color, 0.85 * alpha * k);
      g.beginPath();
      g.ellipse(p.x, p.y, 0.65, 0.65, 0, 0, 2 * Math.PI);
      g.fill();
    }
  }
  g.restore();
}

/** The part of the lid a setting is about: a light tint and a thin dashed edge. */
function drawZone(g, f, camera, slide, pen) {
  if (!f.zone) return;
  const region = [];
  const edge = [];
  const walk = (u0, v0, u1, v1, n, isEdge = false) => {
    for (let i = 0; i <= n; i++) {
      const k = i / n;
      const q = camera.project(surface(u0 + (u1 - u0) * k, v0 + (v1 - v0) * k), slide);
      region.push(q);
      if (isEdge) edge.push(q);
    }
  };
  const e = 0.985;
  if (f.zone.kind === "front") {
    const vb = -1 + 2 * f.zone.depth;
    walk(-e, vb, -e, -e, 20);
    walk(-e, -e, e, -e, 30);
    walk(e, -e, e, vb, 20);
    walk(e, vb, -e, vb, 30, true);
  } else {
    const s = f.zone.split;
    walk(s, -e, e, -e, 16);
    walk(e, -e, e, e, 40);
    walk(e, e, s, e, 16);
    walk(s, e, s, -e, 40, true);
  }
  polyline(g, region, true);
  g.fillStyle = rgba(pen.colors.accent, 0.12);
  g.fill();
  g.save();
  polyline(g, edge, false);
  g.strokeStyle = rgba(pen.colors.accent, 0.7);
  g.lineWidth = 1;
  g.setLineDash([3, 3]);
  g.stroke();
  g.restore();
}

/** A tap: a soft flat disc that swells a little and fades. */
function drawRipple(g, r, camera, slide) {
  const p = r.progress;
  polyline(g, camera.disc(r.u, r.v, 0.5 + 0.55 * p, slide), true);
  g.fillStyle = rgba(TOUCH_BLUE, 0.3 * (1 - p) * (1 - p));
  g.fill();
}

/** A fingertip: a flat, translucent light-blue disc; firmer while it presses; grey if ignored. */
function drawTouch(g, t, camera, slide) {
  polyline(g, camera.disc(t.u, t.v, 0.5, slide), true);
  g.fillStyle = rgba(t.ignored ? GREY : TOUCH_BLUE, (t.pressing ? 0.68 : 0.46) * t.alpha);
  g.fill();
}

/** A grip finger that doesn't count: a small cross beside it. */
function drawIgnoredMark(g, mark, camera, slide, pen) {
  const c = camera.project(surface(mark.u, mark.v), slide);
  const r = 3.5;
  g.save();
  g.beginPath();
  g.moveTo(c.x - r, c.y - r);
  g.lineTo(c.x + r, c.y + r);
  g.moveTo(c.x + r, c.y - r);
  g.lineTo(c.x - r, c.y + r);
  g.strokeStyle = rgba(pen.colors.secondary, mark.alpha);
  g.lineWidth = 1.6;
  g.lineCap = "round";
  g.stroke();
  g.restore();
}

// ---------- The page card ----------
const BAR = 15;
const objectRect = (content, move) => ({ x: content.x + 18 + move * 2.6, y: content.y + content.h / 2 - 12, w: 24, h: 24 });

function faded(g, page, depth, body) {
  if (page.fade <= 0.001) return;
  if (page.fade >= 0.999) body(g, depth);
  else withLayer(g, page.fade, body, depth);
}

function drawCard(g, page, pen, depth) {
  const { ink, paper } = pen.colors;
  const win = { x: 14, y: 5, w: STAGE.cardWidth - 28, h: STAGE.cardHeight - 13 };
  const content = { x: win.x + 12, y: win.y + BAR + 1, w: win.w - 24, h: win.h - BAR - 1 };
  const button = { x: content.x, y: content.y + 42, w: 54, h: 17 };
  const pointer = page.button
    ? { x: button.x + button.w / 2 + 10, y: button.y + button.h / 2 + 3 }
    : { x: content.x + 70, y: content.y + content.h / 2 + 14 };

  g.save();
  shadow(g, pen, BLACK, 0.13, 7, 2);
  rounded(g, win, 9);
  g.fillStyle = rgba(paper);
  g.fill();
  g.restore();
  rounded(g, win, 9);
  g.strokeStyle = rgba(ink, 0.1);
  g.lineWidth = 0.5;
  g.stroke();
  for (let i = 0; i < 3; i++) {
    g.beginPath();
    g.arc(win.x + 10 + i * 8, win.y + BAR / 2, 2.3, 0, 2 * Math.PI);
    g.fillStyle = rgba(ink, 0.16);
    g.fill();
  }
  line(g, win.x, win.y + BAR, win.x + win.w, win.y + BAR, rgba(ink, 0.07), 0.5);

  g.save();
  rounded(g, win, 9);
  g.clip();
  g.beginPath();
  g.rect(win.x, content.y, win.w, content.h);
  g.clip();
  faded(g, page, depth, (h, d) => {
    if (page.button) drawDialog(h, page, content, button, pen);
    else drawText(h, page, content, pen);
    if (page.object !== null) drawObject(h, objectRect(content, page.object), pen);
  });
  // Text running off the page fades instead of being cut.
  fade(g, { x: win.x, y: content.y, w: win.w, h: 9 }, paper, content.y, content.y + 9);
  fade(g, { x: win.x, y: content.y + content.h - 12, w: win.w, h: 12 }, paper, content.y + content.h, content.y + content.h - 12);
  if (page.arrows) drawArrows(g, page.arrows, content, pen);
  g.restore();
  if (page.button || page.menu > 0.01 || page.object !== null) {
    const at = page.object !== null
      ? (({ x, y, w, h }) => ({ x: x + w / 2 + 4, y: y + h / 2 + 4 }))(objectRect(content, page.object))
      : pointer;
    faded(g, page, depth, (h) => drawCursor(h, at, pen));
  }
  if (page.menu > 0.01) drawMenu(g, page.menu, pointer, win, pen, depth);
  if (page.key !== null) drawKey(g, page.key > 0.5, win, pen);
  if (page.cross > 0.01) drawIgnoredBadge(g, page.cross, win, pen);
}

/** A page of text: headings, paragraphs and pictures, scrolling by scrollY and zooming. */
function drawText(g, page, content, pen) {
  const { ink } = pen.colors;
  g.save();
  if (page.zoom !== 1) {
    g.translate(content.x, content.y);
    g.scale(page.zoom, page.zoom);
    g.translate(-content.x, -content.y);
  }
  const period = 100;
  const scroll = page.scrollY * 1.2;
  const shift = page.scrollX * 1.2;
  const width = content.w - (page.arrows === null ? 0 : 30);
  const first = Math.floor(scroll / period) - 1;
  for (let n = first; n <= first + 3; n++) {
    const top = content.y + n * period - scroll + 6;
    const ln = (y, x, w, thick = 2.6, alpha = 0.15) => line(g, x - shift, top + y, x + w - shift, top + y, rgba(ink, alpha), thick, "round");
    const x0 = content.x;
    ln(4, x0, width * 0.5, 4.5, 0.28);
    [0.94, 0.86, 0.9, 0.55].forEach((w, i) => ln(16 + i * 9, x0, width * w));
    rounded(g, { x: x0 - shift, y: top + 56, w: 46, h: 28 }, 4);
    g.fillStyle = rgba(ink, 0.07);
    g.fill();
    [0.62, 0.55, 0.38].forEach((w, i) => ln(60 + i * 9, x0 + 56, (width - 56) * w));
  }
  g.restore();
}

/** For the stories that click: a short message with a button under the pointer. */
function drawDialog(g, page, content, button, pen) {
  const { ink, accent } = pen.colors;
  const ln = (y, w, thick = 2.6, alpha = 0.15) => line(g, content.x, content.y + y, content.x + w, content.y + y, rgba(ink, alpha), thick, "round");
  ln(10, content.w * 0.45, 4.5, 0.28);
  ln(22, content.w * 0.9);
  ln(31, content.w * 0.68);
  const flash = page.flash;
  const ix = flash > 0.5 ? 0.6 : 0;
  const iy = flash > 0.5 ? 0.4 : 0;
  const b = { x: button.x + ix, y: button.y + iy, w: button.w - 2 * ix, h: button.h - 2 * iy };
  rounded(g, b, b.h / 2);
  g.fillStyle = rgba(accent, 0.12 + 0.88 * flash);
  g.fill();
  rounded(g, b, b.h / 2);
  g.strokeStyle = rgba(accent, 0.45 + 0.5 * flash);
  g.lineWidth = 0.8;
  g.stroke();
  const mx = b.x + b.w / 2;
  const my = b.y + b.h / 2;
  line(g, mx - 11, my, mx + 11, my, flash > 0.3 ? rgba(WHITE, 0.95) : rgba(accent, 0.75), 2.6, "round");
}

/** The thing being dragged, lifted off the page. */
function drawObject(g, r, pen) {
  g.save();
  shadow(g, pen, BLACK, 0.25, 4, 2);
  rounded(g, r, 6);
  g.fillStyle = rgba(pen.colors.accent, 0.9);
  g.fill();
  g.restore();
  rounded(g, r, 6);
  g.strokeStyle = rgba(WHITE, 0.35);
  g.lineWidth = 0.8;
  g.stroke();
}

/** The right-click menu, popping up at the pointer. */
function drawMenu(g, alpha, at, win, pen, depth) {
  const { paper, ink, accent } = pen.colors;
  const r = { x: at.x + 2, y: Math.max(win.y + BAR + 2, at.y - 50), w: 76, h: 46 };
  withLayer(g, alpha, (h) => {
    h.save();
    shadow(h, pen, BLACK, 0.22, 8, 3);
    rounded(h, r, 6);
    h.fillStyle = rgba(paper);
    h.fill();
    h.restore();
  }, depth);
  rounded(g, r, 6);
  g.strokeStyle = rgba(ink, 0.12 * alpha);
  g.lineWidth = 0.5;
  g.stroke();
  rounded(g, { x: r.x + 4, y: r.y + 4, w: r.w - 8, h: 12 }, 3);
  g.fillStyle = rgba(accent, alpha);
  g.fill();
  [36, 28, 40].forEach((w, i) => {
    const y = r.y + 10 + i * 13;
    line(g, r.x + 10, y, r.x + 10 + w, y, i === 0 ? rgba(WHITE, alpha) : rgba(ink, 0.3 * alpha), 2.4, "round");
  });
}

/** The arrow pointer, as macOS draws it. */
function drawCursor(g, at, pen) {
  const pts = [[0, 0], [0, 13.5], [3.4, 10.4], [5.8, 15.6], [7.7, 14.8], [5.4, 9.6], [9.8, 9.6]];
  const path = () => polyline(g, pts.map(([x, y]) => ({ x: at.x + x, y: at.y + y })), true);
  g.save();
  shadow(g, pen, BLACK, 0.3, 1.5, 1);
  path();
  g.fillStyle = rgba(BLACK);
  g.fill();
  g.restore();
  g.save();
  path();
  g.strokeStyle = rgba(WHITE);
  g.lineWidth = 1;
  g.lineJoin = "round";
  g.stroke();
  g.restore();
}

/** What the page does, beside it: the straight line it keeps, or which way it now goes. */
function drawArrows(g, arrows, content, pen) {
  const { accent, secondary } = pen.colors;
  const x = content.x + content.w - 10;
  const top = content.y + 10;
  const bottom = content.y + content.h - 10;
  const arrow = (a, b, color, alpha, dashed = false) => {
    if (alpha <= 0.01) return;
    const angle = Math.atan2(b.y - a.y, b.x - a.x);
    g.save();
    g.beginPath();
    g.moveTo(a.x, a.y);
    g.lineTo(b.x, b.y);
    for (const side of [-1, 1]) {
      g.moveTo(b.x, b.y);
      g.lineTo(b.x - 6 * Math.cos(angle + side * 0.55), b.y - 6 * Math.sin(angle + side * 0.55));
    }
    g.strokeStyle = rgba(color, alpha);
    g.lineWidth = 1.8;
    g.lineCap = "round";
    g.lineJoin = "round";
    g.setLineDash(dashed ? [3, 3] : []);
    g.stroke();
    g.restore();
  };
  if (arrows.kind === "straight") {
    arrow({ x: x - 14, y: bottom }, { x: x - 5, y: top }, secondary, arrows.drift, true);
    arrow({ x: x + 3, y: bottom }, { x: x + 3, y: top }, accent, arrows.page);
  } else if (arrows.kind === "diagonal") {
    arrow({ x: x - 10, y: bottom }, { x: x + 3, y: top }, accent, arrows.page);
  } else {
    arrow({ x, y: arrows.on ? top : bottom }, { x, y: arrows.on ? bottom : top }, accent, arrows.page);
  }
}

/** The key held while scrolling. */
function drawKey(g, held, win, pen) {
  const { paper, accent, ink } = pen.colors;
  const r = { x: win.x + win.w - 38, y: win.y + win.h - 25, w: 28, h: 19 };
  g.save();
  shadow(g, pen, BLACK, 0.18, 2, 1);
  rounded(g, r, 5);
  g.fillStyle = rgba(paper);
  g.fill();
  g.restore();
  rounded(g, r, 5);
  g.fillStyle = held ? rgba(accent, 0.18) : rgba(ink, 0.04);
  g.fill();
  rounded(g, r, 5);
  g.strokeStyle = held ? rgba(accent, 0.9) : rgba(ink, 0.3);
  g.lineWidth = 1;
  g.stroke();
  g.fillStyle = held ? rgba(accent) : rgba(ink, 0.7);
  g.font = `500 11px ${pen.colors.font}`;
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillText("⌘", r.x + r.w / 2, r.y + r.h / 2);
}

/** In the title bar: the page ignored that. */
function drawIgnoredBadge(g, alpha, win, pen) {
  const c = { x: win.x + win.w - 12, y: win.y + BAR / 2 };
  g.beginPath();
  g.arc(c.x, c.y, 5.5, 0, 2 * Math.PI);
  g.fillStyle = rgba(pen.colors.secondary, 0.85 * alpha);
  g.fill();
  g.save();
  g.beginPath();
  g.moveTo(c.x - 2.3, c.y - 2.3);
  g.lineTo(c.x + 2.3, c.y + 2.3);
  g.moveTo(c.x + 2.3, c.y - 2.3);
  g.lineTo(c.x - 2.3, c.y + 2.3);
  g.strokeStyle = rgba(WHITE, alpha);
  g.lineWidth = 1.4;
  g.lineCap = "round";
  g.stroke();
  g.restore();
}

// ---------- The frames ----------
function createFrames() {
  const sets = {};
  const of = (finish) =>
    (sets[finish] ??= { rest: null, restPromise: null, turns: new Array(FRAME_COUNT).fill(null), requested: false, settled: 0 });
  const load = (src) =>
    new Promise((resolve) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => resolve(null);
      img.src = src;
    });
  return {
    /** The home frame; one request per finish, however many callers ask. */
    rest(finish) {
      const s = of(finish);
      s.restPromise ??= load(`${FRAMES}/${finish}/home.webp`).then((img) => {
        s.rest = img;
        if (!img) s.restPromise = null; // a failed load may be tried again
        return img;
      });
      return s.restPromise;
    },
    restNow: (finish) => of(finish).rest,
    /** The 72 turn frames load the first time someone turns the mouse. */
    loadTurns(finish, onFrame) {
      const s = of(finish);
      if (s.requested) return;
      s.requested = true;
      for (let i = 0; i < FRAME_COUNT; i++) {
        load(`${FRAMES}/${finish}/turn-${String(i).padStart(2, "0")}.webp`).then((img) => {
          s.turns[i] = img;
          s.settled++;
          onFrame();
        });
      }
    },
    /** Whether turn frames of this finish can be drawn: one has arrived, or all have given up. */
    turnsReady(finish) {
      const s = of(finish);
      return s.settled === FRAME_COUNT || s.turns.some(Boolean);
    },
    /** Frame `index`, or the nearest one loaded so far. */
    turn(finish, index) {
      const t = of(finish).turns;
      for (let d = 0; d <= FRAME_COUNT / 2; d++) {
        const hit = t[(index + d) % FRAME_COUNT] ?? t[(index - d + FRAME_COUNT) % FRAME_COUNT];
        if (hit) return hit;
      }
      return null;
    },
  };
}

// ---------- The stage ----------
export function createStage({ canvas, hit, reducedMotion = false, scale = () => 1, onReady = () => {} }) {
  const ctx = canvas.getContext("2d");
  const turn = createTurn({ inertia: !reducedMotion });
  const frames = createFrames();
  const dark = window.matchMedia("(prefers-color-scheme: dark)");
  let chosenFinish = null;
  let finish = dark.matches ? "black" : "white";
  let colors = readColors(canvas);
  let preview = null;
  let active = true;
  let raf = 0;
  let homeTimer = 0;
  let pendingFinish = null;
  let dpr = 0;

  function draw(state) {
    const ratio = window.devicePixelRatio || 1;
    if (ratio !== dpr) {
      dpr = ratio;
      canvas.width = Math.round(CANVAS.width * dpr);
      canvas.height = Math.round(CANVAS.height * dpr);
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const pen = { dpr, colors };
    const image = (state.turnFrame !== null ? frames.turn(finish, state.turnFrame) : null) ?? frames.restNow(finish);
    ctx.save();
    ctx.translate((CANVAS.width - STAGE.width) / 2, 0);
    drawStage(ctx, state, image, pen);
    ctx.restore();
    // The page card: the row just left fades out under the one pointed at now.
    const incoming = state.story ? state.storyAlpha : 0;
    const card = (page, alpha) => {
      if (alpha <= 0.001) return;
      ctx.save();
      ctx.translate((CANVAS.width - STAGE.cardWidth) / 2, STAGE.cardTop);
      withLayer(ctx, alpha, (h, depth) => drawCard(h, page, pen, depth));
      ctx.restore();
    };
    if (state.outgoing) card(state.outgoing.page, StoryFade.underlay(state.outgoingAlpha, incoming));
    if (state.story) card(state.story.page, state.storyAlpha);
  }

  function tick() {
    raf = 0;
    const now = performance.now() / 1000;
    draw(turn.advance(now, preview));
    if (active && (preview || turn.animating)) raf = requestAnimationFrame(tick);
    else scheduleHome(now);
  }
  function start() {
    if (!raf && active) raf = requestAnimationFrame(tick);
  }
  /** A mouse left turned goes back by itself; the clock is stopped meanwhile. */
  function scheduleHome(now) {
    clearTimeout(homeTimer);
    if (turn.homeAt === null) return;
    homeTimer = setTimeout(() => {
      if (turn.homeAt !== null && !turn.dragging) {
        turn.goHome();
        start();
      }
    }, Math.max(0, (turn.homeAt - now) * 1000));
  }

  /** Turn frames for what is shown, and for the finish about to be shown. */
  function loadTurns() {
    frames.loadTurns(finish, onTurnFrame);
    if (pendingFinish) frames.loadTurns(pendingFinish, onTurnFrame);
  }
  function onTurnFrame() {
    commitFinish();
    start();
  }

  // Dragging turns it, double-clicking puts it back, arrows turn it from the keyboard.
  let dragFrom = null;
  hit.addEventListener("pointerdown", (e) => {
    if (e.button !== 0 || e.ctrlKey) return; // ctrl-click is macOS's secondary click
    dragFrom = e.clientX;
    hit.setPointerCapture(e.pointerId);
  });
  hit.addEventListener("pointermove", (e) => {
    if (dragFrom === null) return;
    const translation = (e.clientX - dragFrom) / scale();
    if (!turn.dragging && Math.abs(translation) < 1) return;
    if (!turn.dragging) loadTurns(); // the 72 frames come with the first real drag, not a click
    hit.classList.add("is-dragging");
    turn.drag(translation, e.timeStamp / 1000);
    start();
  });
  const end = (e) => {
    if (dragFrom === null) return;
    dragFrom = null;
    hit.classList.remove("is-dragging");
    if (turn.dragging) {
      turn.release(e.timeStamp / 1000);
      start();
    }
  };
  hit.addEventListener("pointerup", end);
  hit.addEventListener("pointercancel", end);
  hit.addEventListener("dblclick", () => {
    turn.goHome();
    start();
  });
  hit.addEventListener("keydown", (e) => {
    if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
      e.preventDefault();
      loadTurns();
      turn.turnBy(e.key === "ArrowRight" ? 15 : -15);
      start();
    } else if (e.key === "Home" || e.key === "Escape") {
      turn.goHome();
      start();
    }
  });

  // Right-click: White or Black, as in the app.
  const win = hit.closest(".sw") ?? hit.parentElement;
  const menu = document.createElement("div");
  menu.className = "sw__menu";
  menu.hidden = true;
  menu.setAttribute("role", "menu");
  menu.setAttribute("aria-label", t("Mouse color"));
  menu.innerHTML =
    `<p class="sw__menu-title" role="presentation">${t("Mouse color")}</p>` +
    `<button type="button" role="menuitemradio" data-finish="white">${t("White")}</button>` +
    `<button type="button" role="menuitemradio" data-finish="black">${t("Black")}</button>`;
  win.append(menu);
  const closeMenu = () => {
    menu.hidden = true;
  };
  hit.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    const box = win.getBoundingClientRect();
    const k = scale();
    // From the keyboard (Shift+F10, the context-menu key) there is no pointer: use the stage's centre.
    const fromKeyboard = e.clientX === 0 && e.clientY === 0;
    const at = fromKeyboard
      ? (({ left, top, width, height }) => ({ x: left + width / 2, y: top + height / 2 }))(hit.getBoundingClientRect())
      : { x: e.clientX, y: e.clientY };
    menu.style.left = `${(at.x - box.left) / k}px`;
    menu.style.top = `${(at.y - box.top) / k}px`;
    for (const item of menu.querySelectorAll("[data-finish]")) item.setAttribute("aria-checked", String(item.dataset.finish === finish));
    menu.hidden = false;
    menu.querySelector(`[data-finish="${finish}"]`).focus();
  });
  menu.addEventListener("click", (e) => {
    const item = e.target.closest("[data-finish]");
    if (!item) return;
    chosenFinish = item.dataset.finish;
    setFinish(chosenFinish);
    closeMenu();
    hit.focus();
  });
  menu.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      closeMenu();
      hit.focus();
    } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const items = [...menu.querySelectorAll("[data-finish]")];
      const i = items.indexOf(document.activeElement);
      const step = e.key === "ArrowDown" ? 1 : -1;
      items[(i + step + items.length) % items.length].focus();
    }
  });
  // Focus leaving for another element closes it (a click elsewhere is handled by pointerdown below).
  menu.addEventListener("focusout", (e) => {
    if (e.relatedTarget && !menu.contains(e.relatedTarget)) closeMenu();
  });
  document.addEventListener("pointerdown", (e) => {
    if (!menu.hidden && !menu.contains(e.target)) closeMenu();
  });

  /** Shows `next` once its home frame (and, when turned, a turn frame) is in: until then the old
   * finish keeps being drawn, so the mouse is never blank. */
  function commitFinish() {
    const next = pendingFinish;
    if (!next || !frames.restNow(next)) return;
    if ((turn.yaw !== 0 || turn.dragging) && !frames.turnsReady(next)) return;
    pendingFinish = null;
    finish = next;
    start();
  }
  function setFinish(next) {
    if (next === finish) {
      pendingFinish = null;
      return;
    }
    pendingFinish = next;
    frames.rest(next).then(() => {
      if (pendingFinish !== next) return;
      if (turn.yaw !== 0 || turn.dragging) frames.loadTurns(next, onTurnFrame);
      commitFinish();
    });
  }
  dark.addEventListener("change", () => {
    colors = readColors(canvas);
    if (!chosenFinish) setFinish(dark.matches ? "black" : "white");
    else start();
  });

  frames.rest(finish).then(() => {
    start();
    onReady();
  });

  return {
    setPreview(next) {
      preview = reducedMotion ? null : next;
      start();
    },
    playRing() {
      if (reducedMotion) return;
      turn.playRing();
      start();
    },
    setActive(on) {
      active = on;
      if (on) start();
    },
  };
}
