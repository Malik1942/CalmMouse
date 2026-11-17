import { test } from "node:test";
import assert from "node:assert/strict";
import fixture from "./fixtures/app.json" with { type: "json" };
import { LOOP, loopOf, storyAt, storyElapsed, sameStory, StoryFade, PresenceRing } from "../src/settings/stories.js";
import { assertClose } from "./helpers.js";

test("every kind of story is in the fixture", () => {
  assert.deepEqual([...new Set(fixture.stories.map((s) => s.preview.kind))].sort(), Object.keys(LOOP).sort());
});

test("every story is the app's, frame by frame, and loops the same", () => {
  for (const s of fixture.stories) {
    const name = JSON.stringify(s.preview);
    assert.equal(loopOf(s.preview), s.loop, `${name} loop`);
    for (const { t, frame } of s.frames) assertClose(storyAt(s.preview, t), frame, `${name} at ${t}`);
    for (const { elapsed, frame } of s.elapsed) assertClose(storyElapsed(s.preview, elapsed), frame, `${name} after ${elapsed}`);
  }
});

test("stories fade in, out and under each other as in the app", () => {
  for (const f of fixture.fade) assertClose({ in: StoryFade.fadeIn(f.age), out: StoryFade.fadeOut(f.age) }, { in: f.in, out: f.out }, `age ${f.age}`);
  for (const u of fixture.underlay) assertClose(StoryFade.underlay(u.outgoing, u.incoming), u.value, JSON.stringify(u));
});

test("the ring is the app's", () => {
  for (const r of fixture.ring) {
    const f = PresenceRing.frame(r.t);
    if (r.frame === null) {
      assert.equal(f, null, `t ${r.t}`);
      continue;
    }
    assertClose(f, r.frame, `ring at ${r.t}`);
    for (const k of r.ticks) assertClose(PresenceRing.tick(f, k.angle), { length: k.length, bloom: k.bloom, mix: k.mix }, `tick ${k.angle} at ${r.t}`);
  }
});

test("a story is the same story only with the same setting", () => {
  assert.ok(sameStory(null, null));
  assert.ok(sameStory({ kind: "momentum", on: true }, { kind: "momentum", on: true }));
  assert.ok(!sameStory({ kind: "momentum", on: true }, { kind: "momentum", on: false }));
  assert.ok(!sameStory({ kind: "tapZone", depth: 0.5 }, { kind: "tapZone", depth: 0.45 }));
  assert.ok(!sameStory(null, { kind: "tapToClick" }));
});
