import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { parseStrings, stringsModule } from "../src/settings/strings-format.js";
import { setLanguage, t } from "../src/settings/i18n.js";
import { change, createSettings } from "../src/settings/model.js";
import { renderForm, renderWindow } from "../src/settings/render.js";

const here = (p) => fileURLToPath(new URL(p, import.meta.url));

test("the website's Chinese strings are the app's, up to date", () => {
  const app = readFileSync(here("../../Resources/zh-Hans.lproj/Localizable.strings"), "utf8");
  const copy = readFileSync(here("../src/settings/strings-zh.js"), "utf8");
  assert.equal(copy, stringsModule(parseStrings(app)), "run `node scripts/sync-app-strings.mjs` in website/");
});

test("t() finds whole strings, formats, and captions built from several lines", () => {
  setLanguage("zh");
  try {
    assert.equal(t("Off"), "关");
    assert.equal(t("Below 15%"), "低于 15%");
    assert.equal(t("Version 1.4.1"), "版本 1.4.1");
    assert.equal(t("While holding ⌥⌘"), "按住 ⌥⌘ 时");
    // Double Tap Drag's caption gains a line when right-side taps right-click.
    const base = "Tap twice and leave your finger down.\nMove the mouse right away to drag.\nLift your finger to drop.";
    assert.equal(t(`${base}\nUse the left or middle of the mouse.`), `${t(base)}\n请使用鼠标的左侧或中间。`);
    assert.equal(t("No such string"), "No such string");
  } finally {
    setLanguage("en");
  }
  assert.equal(t("Off"), "Off");
});

// Every state that shows rows the default state hides.
function states() {
  let s = createSettings();
  const all = [s];
  s = change(s, "fineTuning");
  s = change(s, "tapToClick", true);
  s = change(s, "axisLock", true);
  s = change(s, "tapZoneEnabled", true);
  all.push(s);
  all.push(change(s, "rightClick", 2)); // the double-tap right-click popup
  let a = change(createSettings(), "addApp");
  a = change(a, "combinations");
  all.push(a);
  return all;
}

// What's allowed to stay in Latin letters in the Chinese window: names, units and keys.
const ALLOWED = new Set(["CalmMouse", "GitHub", "App", "Esc", "pt", "s"]);

function leftovers(html) {
  const text = [];
  for (const m of html.matchAll(/>([^<]+)</g)) text.push(m[1]);
  for (const m of html.matchAll(/(?:aria-label|title|data-caption)="([^"]*)"/g)) text.push(m[1]);
  const words = text.join(" ").replace(/&[a-z#0-9]+;/gi, " ").match(/[A-Za-z][A-Za-z'’-]*/g) ?? [];
  return [...new Set(words.filter((w) => !ALLOWED.has(w)))];
}

test("the Chinese settings window has no English left, in every pane and state", () => {
  setLanguage("zh");
  try {
    for (const state of states()) {
      for (const pane of ["general", "devices", "advanced"]) {
        const html = renderWindow({ state, pane, version: "1.4.1" }) + renderForm(pane, state, { version: "1.4.1", update: "upToDate" });
        assert.deepEqual(leftovers(html), [], `${pane}: untranslated words`);
      }
    }
  } finally {
    setLanguage("en");
  }
});
