// The settings window as HTML. The same string goes into index.html at build time (vite.config.js,
// so the window shows without JavaScript) and is re-rendered by window.js. No DOM here.

import { ICONS } from "./icons.js";
import { setLanguage, t } from "./i18n.js";
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
  const title = t(PANES.find((p) => p.id === pane).title);
  const tabs = PANES.map((p) => renderTab(p, p.id === pane)).join("");
  const bodies = PANES.map(
    (p) =>
      `<div class="sw__body" id="sw-panel-${p.id}" role="tabpanel" aria-labelledby="sw-tab-${p.id}" data-body="${p.id}"${p.id === pane ? "" : " hidden"}>` +
      renderBody(p.id, state, { version, update }) +
      `</div>`,
  ).join("");
  return (
    `<div class="sw sw--${pane}" data-pane="${pane}" role="group" aria-label="${esc(t("CalmMouse Settings, to try"))}">` +
    `<div class="sw__titlebar"><span class="sw__lights" aria-hidden="true"><i></i><i></i><i></i></span>` +
    `<span class="sw__title" data-title>${esc(title)}</span></div>` +
    `<div class="sw__toolbar" role="tablist" aria-label="${esc(t("Settings"))}">${tabs}</div>` +
    bodies +
    `</div>`
  );
}

/** The window as it first appears, in `lang` ("en" or "zh"): what the build puts into each page. */
export function renderStaticWindow(version, lang = "en") {
  setLanguage(lang);
  return renderWindow({ state: createSettings(), pane: "devices", version });
}

function renderTab(p, selected) {
  return (
    `<button type="button" class="sw__tab${selected ? " is-selected" : ""}" role="tab" id="sw-tab-${p.id}" ` +
    `aria-controls="sw-panel-${p.id}" aria-selected="${selected}" tabindex="${selected ? 0 : -1}" data-tab="${p.id}">` +
    `${ICONS[p.icon]}<span>${esc(t(p.title))}</span></button>`
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
    `<div class="sw__stage" tabindex="0" role="img" aria-roledescription="interactive picture" aria-label="${esc(t("Magic Mouse. Drag or use the arrow keys to turn it; double-click or press Escape to put it back. Right-click for its colour."))}">` +
    `<picture class="sw__still"><source media="(prefers-color-scheme: dark)" srcset="${FRAMES}/black/home.webp">` +
    `<img src="${FRAMES}/white/home.webp" alt="" width="720" height="720"></picture></div>` +
    `<div class="sw__details"><p class="sw__name">${esc(t("Magic Mouse"))}</p><p class="sw__hint">${esc(t("Drag to turn · double-click to put back"))}</p></div>` +
    `</div><p class="sw__caption is-idle" data-caption-text aria-live="polite">${lines(t(IDLE_CAPTION))}</p></div></div>`
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
    heading = `<button type="button" class="sw__heading sw__heading--disclosure" data-toggle="${s.id}" aria-expanded="${s.expanded}"><span>${esc(t(s.title))}</span>${ICONS.chevronRight}</button>`;
  } else if (s.title) {
    heading = `<h3 class="sw__heading"${data("caption", t(s.caption))}><span>${esc(t(s.title))}</span></h3>`;
  }
  const group = s.rows.length ? `<div class="sw__group">${s.rows.map(renderRow).join("")}</div>` : "";
  return `<section class="sw__section" data-section="${s.id}">${heading}${group}</section>`;
}

function renderRow(r) {
  const attrs = `class="sw__row sw__row--${r.type}" data-row="${r.id}"${data("caption", t(r.caption))}${data("preview", r.preview)}`;
  const icon = r.type === "preset" ? `<span class="sw__icon">${ICONS[r.icon]}</span>` : "";
  const label = `<span class="sw__label" id="sw-l-${r.id}">${icon}${esc(t(r.label))}</span>`;
  switch (r.type) {
    case "preset":
      return `<div ${attrs}>${label}${
        r.active
          ? `<span class="sw__check" role="img" aria-label="${esc(t("In use"))}">${ICONS.checkCircle}</span>`
          : `<button type="button" class="sw__button" data-set="${r.id}" aria-label="${esc(t("Use %@").replace("%@", t(r.label)))}">${esc(t("Use"))}</button>`
      }</div>`;
    case "switch":
      return `<div ${attrs}>${label}<button type="button" class="sw__switch" role="switch" aria-checked="${r.on}" aria-labelledby="sw-l-${r.id}" data-set="${r.id}"></button></div>`;
    case "slider": {
      const p = (r.value - r.min) / (r.max - r.min);
      const valuetext = r.reading ? ` aria-valuetext="${esc(t(r.reading))}"` : "";
      const before = r.ends ? `<span class="sw__end">${esc(t(r.ends[0]))}</span>` : "";
      const after = r.ends
        ? `<span class="sw__end">${esc(t(r.ends[1]))}</span>`
        : `<span class="sw__reading" data-reading="${r.id}">${esc(t(r.reading))}</span>`;
      return (
        `<div ${attrs}>${label}<span class="sw__slider">${before}` +
        `<input type="range" min="${r.min}" max="${r.max}" step="${r.step ?? "any"}" value="${r.value}" style="--p:${p}"${valuetext} aria-labelledby="sw-l-${r.id}" data-set="${r.id}">` +
        `${after}</span></div>`
      );
    }
    case "popup": {
      const shown = r.options.find((o) => !o.divider && String(o.value) === String(r.value))?.label ?? "";
      const shownText = t(shown);
      const options = r.options
        .map((o) => (o.divider ? "<hr>" : `<option value="${esc(o.value)}"${String(o.value) === String(r.value) ? " selected" : ""}>${esc(t(o.label))}</option>`))
        .join("");
      return (
        `<div ${attrs}>${label}<span class="sw__popup"><span class="sw__popup-value">${esc(shownText)}</span>` +
        `<span class="sw__popup-chevrons">${ICONS.chevronsUpDown}</span>` +
        `<select aria-labelledby="sw-l-${r.id}" data-set="${r.id}">${options}</select></span></div>`
      );
    }
    case "button": {
      const inner = `${r.icon ? ICONS[r.icon] : ""}${esc(t(r.button))}`;
      const control = r.href
        ? `<a class="sw__button" href="${esc(r.href)}" target="_blank" rel="noopener">${inner}</a>`
        : `<button type="button" class="sw__button" data-action="${r.action}">${inner}</button>`;
      return `<div ${attrs}>${label}${control}</div>`;
    }
    case "version": {
      const controls =
        r.update === "checking"
          ? `<span class="sw__spinner" role="status" aria-label="${esc(t("Checking for updates"))}"></span>`
          : (r.update === "upToDate"
              ? `<span class="sw__check" role="img" aria-label="${t("You're up to date.")}" title="${t("You're up to date.")}">${ICONS.checkCircle}</span>`
              : "") + `<button type="button" class="sw__button" data-action="check">${esc(t("Check Now"))}</button>`;
      return `<div ${attrs}>${label}<span class="sw__controls">${controls}</span></div>`;
    }
    case "disclosure": {
      const icon = r.icon ? `<span class="sw__app-icon">${ICONS[r.icon]}</span>` : "";
      const paused = r.paused ? `<span class="sw__paused" role="img" aria-label="${esc(t("Paused"))}">${ICONS.pauseCircle}</span>` : "";
      return `<button type="button" ${attrs} data-toggle="${r.id}" aria-expanded="${r.expanded}"><span class="sw__label">${icon}${esc(t(r.label))}${paused}</span>${ICONS.chevronRight}</button>`;
    }
    case "remove":
      return `<div ${attrs}><button type="button" class="sw__button sw__button--destructive" data-action="remove">${esc(t(r.label))}</button></div>`;
    default:
      throw new Error(`No row type ${r.type}`);
  }
}
