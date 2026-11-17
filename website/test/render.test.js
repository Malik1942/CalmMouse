import { test } from "node:test";
import assert from "node:assert/strict";
import { createSettings, change } from "../src/settings/model.js";
import { esc, renderWindow, renderForm, renderStaticWindow } from "../src/settings/render.js";

const ctx = { version: "1.2.2", update: "idle" };

test("the window in index.html is the window a visitor first sees", () => {
  assert.equal(renderStaticWindow("1.2.2"), renderWindow({ state: createSettings(), pane: "devices", version: "1.2.2" }));
});

test("Devices opens selected, with General and Advanced rendered but hidden", () => {
  const html = renderWindow({ state: createSettings(), pane: "devices", version: "1.2.2" });
  assert.match(html, /class="sw sw--devices"/);
  assert.match(html, /aria-selected="true" tabindex="0" data-tab="devices"/);
  assert.match(html, /aria-selected="false" tabindex="-1" data-tab="general"/);
  assert.match(html, /data-body="general" hidden/);
  assert.match(html, /data-body="advanced" hidden/);
  assert.doesNotMatch(html, /data-body="devices" hidden/);
});

test("text is escaped", () => {
  assert.equal(esc(`<a "b">&'`), "&lt;a &quot;b&quot;&gt;&amp;&#39;");
});

test("rows carry their caption and story for the left column", () => {
  const html = renderForm("devices", createSettings(), ctx);
  assert.match(html, /data-row="blockScrollWhileClicked" data-caption="The page stays put while you click." data-preview="\{&quot;kind&quot;:&quot;blockWhileClick&quot;\}"/);
});

test("the preset in use shows the check, the others Use", () => {
  const html = renderForm("devices", createSettings(), ctx);
  assert.match(html, /data-row="preset-justFixClicking"[^]*?aria-label="In use"/);
  assert.match(html, /data-set="preset-extraSteady"[^>]*>Use</);
  assert.match(html, /data-set="preset-trackpadFeel"[^>]*>Use</);
});

test("switches say whether they are on", () => {
  const html = renderForm("devices", change(createSettings(), "tapToClick", true), ctx);
  assert.match(html, /role="switch" aria-checked="true" aria-labelledby="sw-l-tapToClick" data-set="tapToClick"/);
});

test("Check Now spins, then shows the green check beside it", () => {
  assert.match(renderForm("general", createSettings(), { ...ctx, update: "checking" }), /class="sw__spinner"/);
  const done = renderForm("general", createSettings(), { ...ctx, update: "upToDate" });
  assert.match(done, /title="You're up to date\."/);
  assert.match(done, /data-action="check">Check Now</);
});

test("Fine-tuning is a heading that opens", () => {
  const html = renderForm("devices", createSettings(), ctx);
  assert.match(html, /data-toggle="fineTuning" aria-expanded="false"/);
});

test("a popup shows the chosen option and lists the rest", () => {
  const html = renderForm("devices", change(createSettings(), "direction", "1"), ctx);
  assert.match(html, /<span class="sw__popup-value">Straight lines<\/span>/);
  assert.match(html, /<option value="1" selected>Straight lines<\/option>/);
});
