// The settings window as HTML. The same string goes into index.html at build time (vite.config.js,
// so the window shows without JavaScript) and is re-rendered by window.js. No DOM here.

import { ICONS } from "./icons.js";
import { IDLE_CAPTION, advancedSections, createSettings, devicesSections, generalSections } from "./model.js";

export const PANES = [
  { id: "general", title: "General", icon: "gear" },
  { id: "devices", title: "Devices", icon: "mouse" },
  { id: "advanced", title: "Advanced", icon: "gears" },
];
/** Where the app's mouse frames are served: FRAMES/{white,black}/{home,turn-NN}.webp. */
export const FRAMES = "/magic-mouse";

export const esc = (s) =>
  String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const lines = (s) => esc(s).replace(/\n/g, "<br>");
const data = (name, value) =>
  value == null ? "" : ` data-${name}="${esc(typeof value === "string" ? value : JSON.stringify(value))}"`;

export function renderWindow({ state, pane, version, update = "idle" }) {
  const title = PANES.find((p) => p.id === pane).title;
  const tabs = PANES.map((p) => renderTab(p, p.id === pane)).join("");
  const bodies = PANES.map(
    (p) =>
      `<div class="sw__body" id="sw-panel-${p.id}" role="tabpanel" aria-labelledby="sw-tab-${p.id}" data-body="${p.id}"${p.id === pane ? "" : " hidden"}>` +
      renderBody(p.id, state, { version, update }) +
      `</div>`,
  ).join("");
  return (
    `<div class="sw sw--${pane}" data-pane="${pane}" role="group" aria-label="CalmMouse Settings, to try">` +
    `<div class="sw__titlebar"><span class="sw__lights" aria-hidden="true"><i></i><i></i><i></i></span>` +
    `<span class="sw__title" data-title>${esc(title)}</span></div>` +
    `<div class="sw__toolbar" role="tablist" aria-label="Settings">${tabs}</div>` +
    bodies +
    `</div>`
  );
}

export const renderStaticWindow = (version) => renderWindow({ state: createSettings(), pane: "devices", version });

function renderTab(p, selected) {
  return (
    `<button type="button" class="sw__tab${selected ? " is-selected" : ""}" role="tab" id="sw-tab-${p.id}" ` +
    `aria-controls="sw-panel-${p.id}" aria-selected="${selected}" tabindex="${selected ? 0 : -1}" data-tab="${p.id}">` +
    `${ICONS[p.icon]}<span>${esc(p.title)}</span></button>`
  );
}

function renderBody(pane, state, ctx) {
  const form = `<div class="sw__form" data-form="${pane}">${renderForm(pane, state, ctx)}</div>`;
  return pane === "devices" ? `<div class="sw__devices">${renderColumn()}${form}</div>` : form;
}

function renderColumn() {
  return (
    `<div class="sw__column"><div class="sw__explainer"><div class="sw__showcase">` +
    `<canvas class="sw__canvas" aria-hidden="true"></canvas>` +
    `<div class="sw__stage" tabindex="0" role="img" aria-label="Magic Mouse. Drag to turn it. Double-click to put it back.">` +
    `<picture class="sw__still"><source media="(prefers-color-scheme: dark)" srcset="${FRAMES}/black/home.webp">` +
    `<img src="${FRAMES}/white/home.webp" alt="" width="720" height="720"></picture></div>` +
    `<div class="sw__details"><p class="sw__name">Magic Mouse</p><p class="sw__hint">Drag to turn · double-click to put back</p></div>` +
    `</div><p class="sw__caption is-idle" data-caption-text aria-live="polite">${lines(IDLE_CAPTION)}</p></div></div>`
  );
}

export function renderForm(pane, state, { version, update }) {
  const sections =
    pane === "devices" ? devicesSections(state) : pane === "general" ? generalSections(state, { version, update }) : advancedSections(state);
  return sections.map(renderSection).join("");
}

function renderSection(s) {
  let heading = "";
  if (s.disclosure) {
    heading = `<button type="button" class="sw__heading sw__heading--disclosure" data-toggle="${s.id}" aria-expanded="${s.expanded}"><span>${esc(s.title)}</span>${ICONS.chevronRight}</button>`;
  } else if (s.title) {
    heading = `<h3 class="sw__heading"${data("caption", s.caption)}><span>${esc(s.title)}</span></h3>`;
  }
  const group = s.rows.length ? `<div class="sw__group">${s.rows.map(renderRow).join("")}</div>` : "";
  return `<section class="sw__section" data-section="${s.id}">${heading}${group}</section>`;
}

function renderRow(r) {
  const attrs = `class="sw__row sw__row--${r.type}" data-row="${r.id}"${data("caption", r.caption)}${data("preview", r.preview)}`;
  const icon = r.type === "preset" ? `<span class="sw__icon">${ICONS[r.icon]}</span>` : "";
  const label = `<span class="sw__label" id="sw-l-${r.id}">${icon}${esc(r.label)}</span>`;
  switch (r.type) {
    case "preset":
      return `<div ${attrs}>${label}${
        r.active
          ? `<span class="sw__check" role="img" aria-label="In use">${ICONS.checkCircle}</span>`
          : `<button type="button" class="sw__button" data-set="${r.id}" aria-label="Use ${esc(r.label)}">Use</button>`
      }</div>`;
    case "switch":
      return `<div ${attrs}>${label}<button type="button" class="sw__switch" role="switch" aria-checked="${r.on}" aria-labelledby="sw-l-${r.id}" data-set="${r.id}"></button></div>`;
    case "slider": {
      const p = (r.value - r.min) / (r.max - r.min);
      const before = r.ends ? `<span class="sw__end">${esc(r.ends[0])}</span>` : "";
      const after = r.ends
        ? `<span class="sw__end">${esc(r.ends[1])}</span>`
        : `<span class="sw__reading" data-reading="${r.id}">${esc(r.reading)}</span>`;
      return (
        `<div ${attrs}>${label}<span class="sw__slider">${before}` +
        `<input type="range" min="${r.min}" max="${r.max}" step="${r.step ?? "any"}" value="${r.value}" style="--p:${p}" aria-labelledby="sw-l-${r.id}" data-set="${r.id}">` +
        `${after}</span></div>`
      );
    }
    case "popup": {
      const shown = r.options.find((o) => !o.divider && String(o.value) === String(r.value))?.label ?? "";
      const options = r.options
        .map((o) => (o.divider ? "<hr>" : `<option value="${esc(o.value)}"${String(o.value) === String(r.value) ? " selected" : ""}>${esc(o.label)}</option>`))
        .join("");
      return (
        `<div ${attrs}>${label}<span class="sw__popup"><span class="sw__popup-value">${esc(shown)}</span>` +
        `<span class="sw__popup-chevrons">${ICONS.chevronsUpDown}</span>` +
        `<select aria-labelledby="sw-l-${r.id}" data-set="${r.id}">${options}</select></span></div>`
      );
    }
    case "button": {
      const inner = `${r.icon ? ICONS[r.icon] : ""}${esc(r.button)}`;
      const control = r.href
        ? `<a class="sw__button" href="${esc(r.href)}" target="_blank" rel="noopener">${inner}</a>`
        : `<button type="button" class="sw__button" data-action="${r.action}">${inner}</button>`;
      return `<div ${attrs}>${label}${control}</div>`;
    }
    case "version": {
      const controls =
        r.update === "checking"
          ? `<span class="sw__spinner" role="status" aria-label="Checking for updates"></span>`
          : (r.update === "upToDate"
              ? `<span class="sw__check" role="img" aria-label="You're up to date." title="You're up to date.">${ICONS.checkCircle}</span>`
              : "") + `<button type="button" class="sw__button" data-action="check">Check Now</button>`;
      return `<div ${attrs}>${label}<span class="sw__controls">${controls}</span></div>`;
    }
    case "disclosure": {
      const icon = r.icon ? `<span class="sw__app-icon">${ICONS[r.icon]}</span>` : "";
      const paused = r.paused ? `<span class="sw__paused" role="img" aria-label="Paused">${ICONS.pauseCircle}</span>` : "";
      return `<button type="button" ${attrs} data-toggle="${r.id}" aria-expanded="${r.expanded}"><span class="sw__label">${icon}${esc(r.label)}${paused}</span>${ICONS.chevronRight}</button>`;
    }
    case "remove":
      return `<div ${attrs}><button type="button" class="sw__button sw__button--destructive" data-action="remove">${esc(r.label)}</button></div>`;
    default:
      throw new Error(`No row type ${r.type}`);
  }
}
