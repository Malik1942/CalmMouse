// How the stage's mouse moves: dragging, inertia, going back home, the presence ring and which
// story shows. A port of MouseStageModel in Sources/CalmMouse/MouseShowcase.swift, with the time
// passed in (seconds), so test/turn.test.js can drive it.

import { PresenceRing, StoryFade, sameStory, storyElapsed } from "./stories.js";

export const FRAME_COUNT = 72;
/** A mouse left turned goes back by itself after this many seconds. */
export const AUTO_HOME_AFTER = 7;

/** Swift's `rounded()`: halves away from zero. */
const roundAway = (x) => Math.sign(x) * Math.round(Math.abs(x));

export function createTurn({ inertia = true } = {}) {
  let yaw = 0; // degrees from rest, counter-clockwise seen from above
  let velocity = 0; // degrees per second
  let homing = false;
  let dragging = false;
  let lastTranslation = 0;
  let lastDragTime = null;
  let lastTick = null;
  let storyPreview = null;
  let storyStart = null;
  let ringRequested = false;
  let ringStart = null;
  let lastStory = null;
  let outgoing = null;
  let outgoingSince = null;
  let homeAt = null;
  let animating = false;

  const atRest = () => !dragging && !homing && velocity === 0 && yaw === 0;
  const wake = () => {
    lastTick = null;
    animating = true;
  };

  /** Stop the clock when nothing moves; a mouse left turned is due home AUTO_HOME_AFTER later. */
  function settle(now) {
    if (dragging || homing || velocity !== 0 || ringStart !== null || ringRequested || outgoingSince !== null) return;
    if (yaw !== 0 && homeAt === null) homeAt = now + AUTO_HOME_AFTER;
    if (yaw === 0) homeAt = null;
    animating = false;
  }

  return {
    get animating() { return animating; },
    get dragging() { return dragging; },
    get homeAt() { return homeAt; },
    get yaw() { return yaw; },

    /** `translation` is the pointer's total sideways travel since the drag began, in points. */
    drag(translation, time) {
      if (!dragging) {
        dragging = true;
        lastTranslation = 0;
        homing = false;
        velocity = 0;
        homeAt = null;
        wake();
      }
      const dx = translation - lastTranslation;
      lastTranslation = translation;
      yaw += dx * 0.8;
      if (lastDragTime !== null) {
        const dt = Math.max(time - lastDragTime, 0.008);
        velocity = 0.5 * velocity + 0.5 * ((dx * 0.8) / dt);
      }
      lastDragTime = time;
    },

    release(time) {
      if (!inertia || (lastDragTime !== null && time - lastDragTime > 0.09)) velocity = 0;
      dragging = false;
      lastDragTime = null;
      wake();
    },

    /** Turns it by `degrees` at once: the keyboard's way to drag. */
    turnBy(degrees) {
      yaw += degrees;
      homing = false;
      velocity = 0;
      homeAt = null;
      wake();
    },

    goHome() {
      homing = true;
      homeAt = null;
      wake();
    },

    playRing() {
      ringRequested = true;
      wake();
    },

    /** Steps the turn to `now` and works out what to draw. */
    advance(now, preview) {
      const dt = Math.min(Math.max(now - (lastTick ?? now), 0), 0.05);
      lastTick = now;
      if (!sameStory(preview, storyPreview)) {
        if (lastStory) {
          // The row just left fades out from where it was; keep the clock going until it has.
          outgoing = lastStory;
          outgoingSince = now;
          animating = true;
        }
        lastStory = null;
        storyPreview = preview;
        storyStart = null;
        if (preview && !atRest()) homing = true;
      }
      if (dragging) {
        // the pointer turns it
      } else if (homing) {
        const target = roundAway(yaw / 360) * 360;
        yaw += (target - yaw) * (1 - Math.exp(-dt * 8));
        velocity = 0;
        if (Math.abs(target - yaw) < 0.35) {
          yaw = 0;
          homing = false;
        }
      } else if (Math.abs(velocity) > 4) {
        yaw += velocity * dt;
        velocity *= Math.exp(-dt * 3.6);
      } else if (velocity !== 0) {
        velocity = 0;
      }
      if (preview && atRest() && storyStart === null) storyStart = now;
      if (ringRequested) {
        ringStart = now;
        ringRequested = false;
      }

      const state = { turnFrame: null, story: null, storyAlpha: 1, outgoing: null, outgoingAlpha: 0, ring: null };
      if (!atRest()) state.turnFrame = ((roundAway(yaw / 5) % FRAME_COUNT) + FRAME_COUNT) % FRAME_COUNT;
      if (preview && storyStart !== null) {
        const story = storyElapsed(preview, now - storyStart);
        state.story = story;
        state.storyAlpha = StoryFade.fadeIn(now - storyStart);
        lastStory = story;
      }
      if (outgoingSince !== null) {
        const alpha = StoryFade.fadeOut(now - outgoingSince);
        if (alpha > 0) {
          state.outgoing = outgoing;
          state.outgoingAlpha = alpha;
        } else {
          outgoing = null;
          outgoingSince = null;
        }
      }
      if (ringStart !== null) {
        state.ring = PresenceRing.frame(now - ringStart);
        if (!state.ring) ringStart = null;
      }
      settle(now);
      return state;
    },
  };
}
