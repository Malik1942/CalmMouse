import { test } from "node:test";
import assert from "node:assert/strict";
import { createTurn, AUTO_HOME_AFTER, FRAME_COUNT } from "../src/settings/turn.js";

const P = { kind: "tapToClick" };
const Q = { kind: "momentum", on: true };

/** Steps `turn` at 60 fps from `from` for `seconds`; returns the last state and time. */
function run(turn, from, seconds, preview = null) {
  let state;
  let now = from;
  for (let i = 1; i <= Math.round(seconds * 60); i++) {
    now = from + i / 60;
    state = turn.advance(now, preview);
  }
  return { state, now };
}

test("at rest it shows the still and stops", () => {
  const turn = createTurn();
  const s = turn.advance(10, null);
  assert.equal(s.turnFrame, null);
  assert.equal(s.story, null);
  assert.equal(turn.animating, false);
});

test("dragging turns 0.8° per point, one frame per 5°", () => {
  const turn = createTurn();
  turn.drag(10, 1.0);
  turn.drag(50, 1.1);
  const s = turn.advance(1.1, null);
  assert.equal(turn.yaw, 40);
  assert.equal(s.turnFrame, 8);
});

test("a flick keeps it turning, slowing to a stop, and it goes home 7 s later", () => {
  const turn = createTurn();
  turn.drag(10, 1.0);
  turn.drag(50, 1.1);
  turn.release(1.12);
  turn.advance(1.12, null);
  let now = 1.12;
  let settledAt = null;
  for (let i = 1; i <= 4 * 60; i++) {
    now = 1.12 + i / 60;
    turn.advance(now, null);
    if (settledAt === null && !turn.animating) settledAt = now;
  }
  assert.ok(turn.yaw > 40 && turn.yaw < 40 + 160 / 3.6 + 1, `yaw ${turn.yaw}`);
  assert.equal(turn.animating, false);
  assert.notEqual(turn.homeAt, null, "a settled mouse is due home");
  assert.ok(Math.abs(turn.homeAt - (settledAt + AUTO_HOME_AFTER)) < 1e-9);
  turn.goHome();
  run(turn, now, 2);
  assert.equal(turn.yaw, 0);
  assert.equal(turn.homeAt, null);
});

test("without inertia a release stops it where it is", () => {
  const turn = createTurn({ inertia: false });
  turn.drag(10, 1.0);
  turn.drag(50, 1.1);
  turn.release(1.11);
  run(turn, 1.11, 1);
  assert.equal(turn.yaw, 40);
});

test("pointing at a setting turns the mouse back first, then plays", () => {
  const turn = createTurn();
  turn.drag(25, 0);
  turn.release(1);
  turn.advance(1, null);
  let s = turn.advance(1.05, P);
  assert.equal(s.story, null, "no story while it is turned");
  const { state, now } = run(turn, 1.05, 2, P);
  assert.equal(turn.yaw, 0);
  assert.ok(state.story, "plays once home");
  s = turn.advance(now + 0.25, P);
  assert.equal(s.storyAlpha, 1);
});

test("a row left fades out over 0.2 s under the next", () => {
  const turn = createTurn();
  turn.advance(0, P);
  turn.advance(1, P);
  let s = turn.advance(1.05, Q);
  assert.equal(s.outgoingAlpha, 1);
  assert.equal(s.storyAlpha, 0);
  s = turn.advance(1.15, Q);
  assert.ok(Math.abs(s.outgoingAlpha - 0.25) < 1e-9);
  s = turn.advance(1.3, Q);
  assert.equal(s.outgoing, null);
});

test("the ring plays once, for 3 s", () => {
  const turn = createTurn();
  turn.playRing();
  assert.ok(turn.advance(5, null).ring);
  assert.ok(turn.advance(7.5, null).ring);
  assert.equal(turn.advance(8.01, null).ring, null);
  assert.equal(turn.animating, false);
});

test("turning left wraps to the frames before 0", () => {
  assert.equal(FRAME_COUNT, 72);
  const turn = createTurn();
  turn.drag(-30, 0);
  assert.equal(turn.yaw, -24);
  assert.equal(turn.advance(0, null).turnFrame, 67);
});

test("home is the nearest whole turn", () => {
  const turn = createTurn();
  turn.turnBy(400);
  turn.goHome();
  run(turn, 0, 0.1);
  assert.ok(turn.yaw > 360 && turn.yaw < 400, `yaw ${turn.yaw}`);
  run(turn, 0.1, 3);
  assert.equal(turn.yaw, 0);
});
