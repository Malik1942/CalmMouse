// What each setting's story shows at time t: a port of Sources/CalmMouse/MouseStory.swift and
// SettingPreview.loop (SettingsPreviews.swift). Plain data; stage.js draws it.
// test/stories.test.js holds every story to the app's numbers.

/** Loop length of each story, in seconds. */
export const LOOP = {
  blockWhileClick: 3.6,
  settleAfterRelease: 3.4,
  ignoreNudges: 3.4,
  straightLines: 3.0,
  momentum: 3.2,
  ignoreSideways: 2.8,
  tapToClick: 2.6,
  tapRightClick: 3.4,
  tapZone: 3.0,
  tapAndDrag: 4.3,
  twoFingerDrag: 4.2,
  keyZoom: 3.3,
  reverseScroll: 3.0,
};

export const loopOf = (preview) => LOOP[preview.kind];

/** Whether two previews are the same story: the same kind with the same setting. */
export function sameStory(a, b) {
  if (a === b) return true;
  if (!a || !b || a.kind !== b.kind) return false;
  return a.on === b.on && a.doubleTap === b.doubleTap && a.depth === b.depth;
}

/** Where a finger naturally rests: the middle, a little back. */
const REST = { u: 0, v: 0.15 };

/** 0→1 smoothstep between two moments of the loop. */
function seg(t, a, b) {
  const x = Math.min(Math.max((t - a) / (b - a), 0), 1);
  return x * x * (3 - 2 * x);
}
function easeOut(x) {
  const c = Math.min(Math.max(x, 0), 1);
  return 1 - (1 - c) * (1 - c);
}
/** 1 inside [a, b] with soft edges, 0 outside. */
function pulse(t, a, b, fade = 0.15) {
  return seg(t, a, a + fade) * (1 - seg(t, b - fade, b));
}
const touch = (u, v, alpha, { pressing = false, ignored = false } = {}) => ({ u, v, alpha, pressing, ignored });
/** A finger `along` px (of the 62×106 top view) forward of rest and `across` px to the right. */
const finger = ({ along = 0, across = 0, alpha, pressing = false }) =>
  touch(REST.u + across / 31, REST.v - along / 53, alpha, { pressing });
const ripple = (u, v, progress) => ({ u, v, progress });
const page = () => ({
  scrollY: 0, scrollX: 0, zoom: 1, button: false, flash: 0, object: null,
  menu: 0, cross: 0, arrows: null, key: null, fade: 1,
});
const frame = ({ pressed = false, slide = 0, zone = null } = {}) => ({
  touches: [], ripples: [], zone, dial: null, crossOnLid: null, pressed, slide, page: page(),
});

function rightSideClick(t) {
  const right = { u: 19 / 31, v: -21 / 53 };
  const left = { u: -15 / 31, v: -21 / 53 };
  const f = frame({ zone: { kind: "rightSide", split: 0.1 } });
  f.touches = [touch(right.u, right.v, pulse(t, 0.4, 0.62, 0.08)), touch(left.u, left.v, pulse(t, 2.2, 2.42, 0.08))];
  f.ripples = [ripple(right.u, right.v, seg(t, 0.5, 0.95)), ripple(left.u, left.v, seg(t, 2.3, 2.75))];
  f.page.menu = seg(t, 0.55, 0.75) * (1 - seg(t, 1.8, 2.0));
  f.page.button = true;
  f.page.flash = pulse(t, 2.35, 2.75);
  return f;
}

function doubleTapRightClick(t) {
  const f = frame();
  f.touches = [finger({ alpha: Math.max(pulse(t, 0.3, 0.5, 0.07), pulse(t, 0.7, 0.9, 0.07)) })];
  f.ripples = [ripple(REST.u, REST.v, seg(t, 0.4, 0.75)), ripple(REST.u, REST.v, seg(t, 0.8, 1.15))];
  f.page.menu = seg(t, 0.95, 1.15) * (1 - seg(t, 2.9, 3.2));
  return f;
}

const STORIES = {
  /** Click, then slide the finger: the page holds still. Release and swipe: it scrolls. */
  blockWhileClick(t) {
    const pressed = t > 0.5 && t < 2.0;
    const slide = 16 * seg(t, 0.8, 1.6);
    const swipe = 24 * seg(t, 2.4, 3.1);
    const f = frame({ pressed });
    f.touches = [finger({ along: pressed ? slide : swipe, alpha: seg(t, 0, 0.3) * (1 - seg(t, 3.3, 3.6)), pressing: pressed })];
    f.page.scrollY = 26 * seg(t, 2.45, 3.15);
    f.page.cross = pulse(t, 0.9, 1.9);
    return f;
  },
  /** Release the click; the little flick as the finger leaves is ignored too. */
  settleAfterRelease(t) {
    const pressed = t > 0.3 && t < 1.0;
    const flick = 8 * seg(t, 1.0, 1.25);
    const lift = seg(t, 1.2, 1.45);
    const swipe = 22 * seg(t, 2.3, 3.0);
    const alpha = seg(t, 0, 0.25) * (1 - lift) + seg(t, 2.1, 2.3) * (1 - seg(t, 3.1, 3.4));
    const f = frame({ pressed });
    f.touches = [finger({ along: t < 2 ? flick : swipe, alpha, pressing: pressed })];
    f.page.scrollY = 26 * seg(t, 2.35, 3.05);
    f.page.cross = pulse(t, 1.05, 1.9);
    return f;
  },
  /** Tiny wiggles do nothing; a real swipe scrolls. */
  ignoreNudges(t) {
    const wiggle = 3 * Math.sin(t * 12) * pulse(t, 0.2, 1.5);
    const swipe = 30 * seg(t, 2.0, 2.9);
    const f = frame();
    f.touches = [finger({ along: swipe - wiggle, alpha: seg(t, 0, 0.25) * (1 - seg(t, 3.1, 3.4)) })];
    f.page.scrollY = Math.max(0, swipe - 8) * 1.3;
    f.page.cross = pulse(t, 0.5, 1.7);
    return f;
  },
  /** The finger drifts diagonally, but the page moves in a straight line. */
  straightLines(t) {
    const s = seg(t, 0.4, 1.9);
    const shown = pulse(t, 0.5, 2.6);
    const f = frame();
    f.touches = [finger({ along: 24 * s, across: 10 * s, alpha: seg(t, 0, 0.3) * (1 - seg(t, 2.6, 2.9)) })];
    f.page.scrollY = 26 * s;
    f.page.arrows = { kind: "straight", drift: 0.6 * shown, page: shown };
    return f;
  },
  /** A quick flick: then the page either glides to a stop, or stops dead. */
  momentum(t, { on }) {
    const flick = 20 * seg(t, 0.3, 0.6);
    const glide = on ? 40 * easeOut(seg(t, 0.6, 2.4)) : 0;
    const f = frame();
    f.touches = [finger({ along: flick, alpha: seg(t, 0, 0.25) * (1 - seg(t, 0.6, 0.8)) })];
    f.page.scrollY = flick * 1.4 + glide;
    f.page.cross = on ? 0 : pulse(t, 0.8, 1.8);
    return f;
  },
  /** Sideways swipes do nothing; up and down still works. */
  ignoreSideways(t) {
    const side = 14 * seg(t, 0.4, 1.1);
    const up = 20 * seg(t, 1.8, 2.5);
    const f = frame();
    f.touches = [finger({ along: up, across: side - 7, alpha: seg(t, 0, 0.25) * (1 - seg(t, 2.6, 2.8)) })];
    f.page.scrollY = 24 * seg(t, 1.85, 2.55);
    f.page.cross = pulse(t, 0.7, 1.6);
    return f;
  },
  /** A light tap clicks: no need to press the mouse down. */
  tapToClick(t) {
    const f = frame();
    f.page.button = true;
    let alpha = 0;
    for (const start of [0.4, 1.5]) {
      alpha = Math.max(alpha, pulse(t, start, start + 0.22, 0.08));
      f.ripples.push(ripple(REST.u, REST.v, seg(t, start + 0.1, start + 0.6)));
      f.page.flash = Math.max(f.page.flash, pulse(t, start + 0.12, start + 0.5));
    }
    f.touches = [finger({ alpha })];
    return f;
  },
  tapRightClick(t, { doubleTap }) {
    return doubleTap ? doubleTapRightClick(t) : rightSideClick(t);
  },
  /** Taps count in the front area; grip fingers near the back don't click. */
  tapZone(t, { depth }) {
    const front = { u: 8 / 42, v: -1 + depth };
    const back = { u: -30 / 42, v: 1 - 52 / 130 };
    const f = frame({ zone: { kind: "front", depth } });
    f.touches = [touch(front.u, front.v, pulse(t, 0.4, 0.65, 0.08)), touch(back.u, back.v, pulse(t, 1.6, 2.3, 0.1), { ignored: true })];
    f.ripples = [ripple(front.u, front.v, seg(t, 0.5, 1.0))];
    f.crossOnLid = { u: back.u, v: back.v - 36 / 130, alpha: pulse(t, 1.8, 2.4) };
    f.page.button = true;
    f.page.flash = pulse(t, 0.55, 0.95);
    return f;
  },
  /** Double tap, stay down on the second, move the mouse: the object comes along; lift to drop. */
  tapAndDrag(t) {
    const move = 22 * seg(t, 1.3, 2.4);
    const holding = t > 1.35 && t < 2.9;
    const second = seg(t, 0.8, 0.95) * (1 - seg(t, 2.9, 3.1));
    const first = pulse(t, 0.3, 0.5, 0.08);
    const f = frame({ pressed: holding, slide: move * 0.06 * (1 - seg(t, 3.2, 3.8)) });
    f.touches = [finger({ alpha: Math.max(first, second), pressing: holding })];
    f.ripples = [ripple(REST.u, REST.v, seg(t, 0.4, 0.8)), ripple(REST.u, REST.v, seg(t, 0.85, 1.25))];
    f.page.object = move;
    f.page.fade = seg(t, 0, 0.3) * (1 - seg(t, 3.85, 4.15));
    return f;
  },
  /** Rest two fingers, hold a moment, then move to drag. */
  twoFingerDrag(t) {
    const move = 22 * seg(t, 1.5, 2.5);
    const holding = t > 1.2 && t < 3.0;
    const alpha = seg(t, 0.3, 0.5) * (1 - seg(t, 3.0, 3.2));
    const v = 6 / 53;
    const f = frame({ pressed: holding, slide: move * 0.06 * (1 - seg(t, 3.25, 3.8)) });
    f.touches = [touch(-9 / 31, v, alpha, { pressing: holding }), touch(9 / 31, v, alpha, { pressing: holding })];
    f.dial = { u: 0, v: v - 24 / 53, progress: seg(t, 0.5, 1.2) };
    f.page.object = move;
    f.page.fade = seg(t, 0, 0.3) * (1 - seg(t, 3.85, 4.15));
    return f;
  },
  /** Hold a key and scroll: the page zooms instead. */
  keyZoom(t) {
    const held = t > 0.3 && t < 2.6;
    const s = seg(t, 0.7, 1.9);
    const f = frame();
    f.touches = [finger({ along: 18 * s, alpha: seg(t, 0.4, 0.6) * (1 - seg(t, 2.4, 2.6)) })];
    f.page.zoom = 1 + 0.5 * s;
    f.page.key = held ? 1 : 0;
    f.page.fade = seg(t, 0, 0.3) * (1 - seg(t, 2.9, 3.2));
    return f;
  },
  /** The same swipe scrolls the page the other way. */
  reverseScroll(t, { on }) {
    const s = seg(t, 0.5, 1.5);
    const f = frame();
    f.touches = [finger({ along: 26 * s - 6, alpha: seg(t, 0.1, 0.4) * (1 - seg(t, 1.6, 1.9)) })];
    f.page.scrollY = (on ? -26 : 26) * seg(t, 0.55, 1.6);
    f.page.arrows = { kind: "flipped", on, drift: 0, page: pulse(t, 0.6, 2.7) };
    return f;
  },
};

/** The story `preview` shows `t` seconds into its loop. */
export const storyAt = (preview, t) => STORIES[preview.kind](t, preview);

/** The story `elapsed` seconds after it began, looping. Each loop starts where the last left the
 * page, so it scrolls on instead of jumping back (its text repeats every 100 pt). */
export function storyElapsed(preview, elapsed) {
  const loop = loopOf(preview);
  const n = Math.max(Math.floor(elapsed / loop), 0);
  const f = storyAt(preview, elapsed - n * loop);
  if (n > 0) f.page.scrollY += n * storyAt(preview, loop).page.scrollY;
  return f;
}

/** How a story's layer fades in, out, and hands over to the next row's. */
export const StoryFade = {
  duration: 0.2,
  fadeIn(age) {
    const k = Math.min(Math.max(age / StoryFade.duration, 0), 1);
    return 1 - (1 - k) * (1 - k);
  },
  fadeOut(age) {
    return 1 - StoryFade.fadeIn(age);
  },
  /** The opacity for an opaque card under another drawn at `incoming`, so the pair never lets the
   * background through. */
  underlay(outgoing, incoming) {
    return incoming >= 1 ? 0 : Math.min(outgoing / (1 - incoming), 1);
  },
};

/** The big ring around the whole mouse: a one-time moment, never a loop. */
export const PresenceRing = {
  duration: 3,
  frame(t) {
    if (!(t >= 0 && t <= PresenceRing.duration)) return null;
    const s = (a, b) => seg(t, a, b);
    const open = 1 - Math.pow(1 - Math.min(t, 1), 2);
    return {
      sweep: s(0, 0.8),
      radius: 5.05 + 0.25 * open,
      alpha: s(0, 0.3) * (1 - s(2.4, 3.0)),
      phase: 2.4 * t,
      bloom: s(1.0, 1.6) * (1 - s(1.9, 2.5)),
    };
  },
  /** One tick of the ring at `angle` (radians round it). */
  tick(f, angle) {
    const lobe = Math.pow(0.5 + 0.5 * Math.cos(2 * (angle - f.phase)), 1.6);
    return {
      length: 0.1 + 0.32 * lobe,
      bloom: f.bloom * (0.25 + 0.38 * lobe),
      mix: 0.5 + 0.5 * Math.cos(angle + 1.3 * f.phase + 0.8),
    };
  },
};
