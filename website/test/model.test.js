import { test } from "node:test";
import assert from "node:assert/strict";
import fixture from "./fixtures/app.json" with { type: "json" };
import {
  PRESETS, createSettings, activePreset, change, devicesSections, generalSections, advancedSections, findRow,
} from "../src/settings/model.js";

const ids = (sections, id) => sections.find((s) => s.id === id).rows.map((r) => r.id);

test("the presets are the app's", () => {
  assert.equal(PRESETS.length, fixture.presets.length);
  PRESETS.forEach((p, i) => {
    assert.equal(p.name, fixture.presets[i].name);
    assert.equal(p.summary, fixture.presets[i].summary);
    assert.deepEqual(p.values, fixture.presets[i].values);
  });
});

test("a new window is a fresh install: Just fix clicking, and Clicking holds only Tap to click", () => {
  const s = createSettings();
  assert.equal(activePreset(s).id, "justFixClicking");
  assert.deepEqual(ids(devicesSections(s), "clicking"), ["tapToClick"]);
});

test("Tap to click brings the rest of Clicking", () => {
  const s = change(createSettings(), "tapToClick", true);
  assert.deepEqual(ids(devicesSections(s), "clicking"), ["tapToClick", "tapsRightSide", "tapAndDrag", "tapZoneEnabled"]);
});

test("Use applies a preset, and changing a setting by hand can leave none in use", () => {
  let s = change(createSettings(), "preset-trackpadFeel");
  assert.equal(activePreset(s).id, "trackpadFeel");
  assert.equal(s.tapToClick, true);
  const rows = devicesSections(s).find((x) => x.id === "presets").rows;
  assert.deepEqual(rows.map((r) => r.active), [false, false, true]);
  s = change(s, "tapAndDrag", false);
  assert.equal(activePreset(s), null);
});

test("Direction is one choice for two settings", () => {
  let s = change(createSettings(), "direction", "1");
  assert.deepEqual([s.axisLock, s.blockHorizontalScroll], [true, false]);
  s = change(s, "direction", "2");
  assert.deepEqual([s.axisLock, s.blockHorizontalScroll], [true, true]);
  assert.equal(findRow(devicesSections(s), "direction").preview.kind, "ignoreSideways");
  s = change(s, "direction", "0");
  assert.deepEqual([s.axisLock, s.blockHorizontalScroll], [false, false]);
  assert.equal(findRow(devicesSections(s), "direction").preview.kind, "straightLines");
});

test("a double-tap right click keeps the three-way menu", () => {
  let s = { ...change(createSettings(), "tapToClick", true), tapRightClick: true, tapRightClickMode: "doubleTap" };
  const row = findRow(devicesSections(s), "rightClick");
  assert.equal(row.type, "popup");
  assert.equal(row.value, 2);
  assert.deepEqual(row.preview, { kind: "tapRightClick", doubleTap: true });
  s = change(s, "rightClick", "1");
  assert.equal(s.tapRightClickMode, "rightSide");
  assert.equal(findRow(devicesSections(s), "tapsRightSide").type, "switch");
});

test("Double Tap Drag says where to tap while the right side right-clicks", () => {
  let s = change(createSettings(), "tapToClick", true);
  assert.ok(!findRow(devicesSections(s), "tapAndDrag").caption.includes("left or middle"));
  s = change(s, "tapsRightSide", true);
  assert.ok(findRow(devicesSections(s), "tapAndDrag").caption.endsWith("\nUse the left or middle of the mouse."));
});

test("Fine-tuning shows only the sliders that mean something", () => {
  let s = createSettings();
  assert.deepEqual(ids(devicesSections(s), "fineTuning"), []);
  s = change(s, "fineTuning");
  assert.deepEqual(ids(devicesSections(s), "fineTuning"), ["releaseGraceMs", "deadZone"]);
  s = change(change(change(s, "direction", "1"), "tapToClick", true), "tapZoneEnabled", true);
  assert.deepEqual(ids(devicesSections(s), "fineTuning"),
    ["releaseGraceMs", "deadZone", "axisLockThreshold", "tapSensitivity", "tapZoneDepth", "twoFingerDrag"]);
});

test("slider readings read like the app's", () => {
  const reading = (s, id) => findRow(devicesSections({ ...s, showFineTuning: true }), id).reading;
  const s = createSettings();
  assert.equal(reading({ ...s, releaseGraceMs: 0 }, "releaseGraceMs"), "Off");
  assert.equal(reading(s, "releaseGraceMs"), "0.2 s");
  assert.equal(reading({ ...s, releaseGraceMs: 250 }, "releaseGraceMs"), "0.25 s");
  assert.equal(reading(s, "deadZone"), "Off");
  assert.equal(reading({ ...s, deadZone: 8 }, "deadZone"), "8 pt");
  assert.equal(reading({ ...s, tapToClick: true, tapZoneEnabled: true, tapZoneDepth: 0.45 }, "tapZoneDepth"), "45%");
});

test("every Devices row explains itself", () => {
  const s = { ...createSettings(), tapToClick: true, tapRightClick: true, tapZoneEnabled: true, axisLock: true, showFineTuning: true };
  for (const section of devicesSections(s)) {
    for (const row of section.rows) assert.ok(row.caption, `${row.id} has no caption`);
  }
});

test("General shows the version, the update check and the battery choices", () => {
  const sections = generalSections(createSettings(), { version: "1.2.2", update: "idle" });
  assert.equal(findRow(sections, "version").label, "Version 1.2.2");
  assert.deepEqual(findRow(sections, "batteryWarning").options.filter((o) => !o.divider).map((o) => o.label),
    ["Off", "Below 10%", "Below 15%", "Below 20%", "Below 30%", "Below 50%"]);
  assert.equal(findRow(sections, "batteryWarning").value, 15);
});

test("Advanced: Choose… adds the example app's rule and Remove takes it away", () => {
  let s = change(createSettings(), "addApp");
  assert.deepEqual(ids(advancedSections(s), "apps"),
    ["rule", "rule.enabled", "rule.disableScrollEntirely", "rule.blockScrollWhileClicked", "rule.blockHorizontalScroll",
      "rule.momentumEnabled", "rule.axisLock", "rule.remove", "addApp"]);
  assert.equal(findRow(advancedSections(s), "rule.disableScrollEntirely").value, 2, "Off, as the app sets it");
  s = change(s, "rule.momentumEnabled", "1");
  assert.equal(s.rule.momentumEnabled, true);
  s = change(s, "rule.enabled", false);
  assert.equal(findRow(advancedSections(s), "rule").paused, true);
  s = change(s, "rule.remove");
  assert.deepEqual(ids(advancedSections(s), "apps"), ["addApp"]);
});

test("a shortcut set back to Scroll normally is forgotten", () => {
  let s = change(createSettings(), "key.⌘", "zoomCommand");
  assert.deepEqual(s.modifierActions, { "⌘": "zoomCommand" });
  s = change(s, "key.⌘", "normal");
  assert.deepEqual(s.modifierActions, {});
  assert.deepEqual(ids(advancedSections(s), "shortcuts"), ["key.⌘", "key.⌥", "key.⌃", "key.⇧", "combinations"]);
  s = change(s, "combinations");
  assert.deepEqual(ids(advancedSections(s), "shortcuts").slice(-3), ["key.⌥⌘", "key.⇧⌘", "key.⌃⌥"]);
});

test("change never edits the state it was given", () => {
  const s = createSettings();
  const frozen = JSON.stringify(s);
  change(change(change(s, "tapToClick", true), "direction", "2"), "addApp");
  assert.equal(JSON.stringify(s), frozen);
});
