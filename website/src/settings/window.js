// The working settings window: renders it, keeps its settings, and joins pointing at a row to
// the mouse on the left, as the app's Settings › Devices does.

import { IDLE_CAPTION, activePreset, change, createSettings, devicesSections, findRow } from "./model.js";
import { PANES, renderForm, renderWindow } from "./render.js";
import { createStage } from "./stage.js";
import { setLanguage, t } from "./i18n.js";

const CHECK_TIME = 900; // ms the update check spins before saying it's up to date
const POPOVER_DELAY = 550; // the app's delay before a heading's popover

export function mountSettingsWindow(frame) {
  setLanguage(document.documentElement.lang);
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const version = document.querySelector(".changelog__version")?.textContent.trim() ?? "";
  let state = createSettings();
  let pane = "devices";
  let update = "idle";
  let focusRow = null; // data-row of the row the left column explains
  let rerendering = false;
  let keyboardExplained = false; // the left column is explaining because of keyboard focus

  frame.innerHTML = renderWindow({ state, pane, version, update });
  const win = frame.querySelector(".sw");
  const caption = win.querySelector("[data-caption-text]");
  const showcase = win.querySelector(".sw__showcase");
  const form = (p) => win.querySelector(`[data-form="${p}"]`);
  const scale = () => Number(frame.style.getPropertyValue("--sw-scale")) || 1;

  const stage = createStage({
    canvas: win.querySelector(".sw__canvas"),
    hit: win.querySelector(".sw__stage"),
    reducedMotion,
    scale,
    onReady: () => {
      win.querySelector(".sw__still").hidden = true; // the canvas draws the mouse from here on
    },
  });

  // ---------- The left column ----------
  let captionText = null;
  let captionIdle = null;
  function setCaption(text, idle) {
    if (text === captionText && idle === captionIdle) return; // it's a live region: don't repeat it
    captionText = text;
    captionIdle = idle;
    caption.replaceChildren(...text.split("\n").flatMap((line, i) => (i ? [document.createElement("br"), line] : [line])));
    caption.classList.toggle("is-idle", idle);
  }
  function explain(row) {
    if (!row.dataset.caption) return; // a row with nothing to say leaves the last one showing
    focusRow = row.dataset.row;
    const preview = row.dataset.preview ? JSON.parse(row.dataset.preview) : null;
    setCaption(row.dataset.caption, false);
    showcase.classList.toggle("is-explaining", preview !== null && !reducedMotion);
    stage.setPreview(preview);
  }
  function clearExplanation() {
    focusRow = null;
    keyboardExplained = false;
    setCaption(t(IDLE_CAPTION), true);
    showcase.classList.remove("is-explaining");
    stage.setPreview(null);
  }

  // ---------- Re-rendering a form after a change ----------
  function keyOf(el) {
    for (const name of ["set", "toggle", "action"]) {
      if (el?.dataset?.[name]) return `[data-${name}="${CSS.escape(el.dataset[name])}"]`;
    }
    return null;
  }
  const FOCUSABLE = 'button, input, select, a[href], [tabindex]:not([tabindex="-1"])';
  /** Focuses a row (or the form) that isn't normally focusable: tabindex only while it has focus. */
  function focusPlain(node) {
    node.tabIndex = -1;
    node.addEventListener("blur", () => node.removeAttribute("tabindex"), { once: true });
    node.focus({ preventScroll: true });
  }
  function rerender(p = pane) {
    const el = form(p);
    const scroll = el.scrollTop;
    const active = document.activeElement;
    const had = el.contains(active); // never take focus from outside the form
    const key = had ? keyOf(active) : null;
    const rowId = had ? active.closest("[data-row]")?.dataset.row ?? null : null;
    const action = had ? active.dataset?.action : null;
    const onRow = had && active.matches("[data-row]"); // focus was on a row we focused ourselves
    // The controls after the focused one, in case it disappears.
    const all = had ? [...el.querySelectorAll(FOCUSABLE)] : [];
    const following = had ? all.slice(all.indexOf(active) + 1).map(keyOf).filter(Boolean) : [];
    rerendering = true;
    el.innerHTML = renderForm(p, state, { version, update });
    el.scrollTop = scroll;
    if (had) {
      const row = rowId && el.querySelector(`[data-row="${CSS.escape(rowId)}"]`);
      const same = key && el.querySelector(key);
      if (same) same.focus({ preventScroll: true });
      else if (action === "remove" && el.querySelector('[data-action="addApp"]')) el.querySelector('[data-action="addApp"]').focus({ preventScroll: true });
      else if (row && (onRow || rowId.startsWith("preset-") || action === "check")) focusPlain(row);
      else {
        const next = following.map((k) => el.querySelector(k)).find(Boolean);
        if (next) next.focus({ preventScroll: true });
        else focusPlain(el);
      }
    }
    rerendering = false;
    decorateHeadings(el);
    if (p === "devices" && focusRow) {
      // A change can change the story (Keep gliding on → off) or remove the row (Tap to click off).
      const row = el.querySelector(`[data-row="${CSS.escape(focusRow)}"]`);
      if (row) explain(row);
      else clearExplanation();
    }
  }

  // ---------- Tabs ----------
  let visible = true; // whether the window is on screen; the stage rests when it isn't
  function selectPane(next, { focus = false } = {}) {
    if (next === pane) return;
    pane = next;
    win.className = `sw sw--${pane}`;
    win.dataset.pane = pane;
    win.querySelector("[data-title]").textContent = t(PANES.find((p) => p.id === pane).title);
    for (const tab of win.querySelectorAll("[data-tab]")) {
      const on = tab.dataset.tab === pane;
      tab.classList.toggle("is-selected", on);
      tab.setAttribute("aria-selected", String(on));
      tab.tabIndex = on ? 0 : -1;
      if (on && focus) tab.focus();
    }
    for (const body of win.querySelectorAll("[data-body]")) body.hidden = body.dataset.body !== pane;
    hidePopover();
    stage.setActive(visible && pane === "devices");
    if (pane === "devices") {
      clearExplanation();
      stage.playRing(); // the app's ring plays each time Devices appears
    }
  }
  win.querySelector(".sw__toolbar").addEventListener("keydown", (e) => {
    const i = PANES.findIndex((p) => p.id === pane);
    const to = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: PANES.length - 1 }[e.key];
    if (to === undefined) return;
    e.preventDefault();
    selectPane(PANES[(to + PANES.length) % PANES.length].id, { focus: true });
  });

  // ---------- Controls ----------
  function check() {
    update = "checking";
    rerender("general");
    setTimeout(() => {
      // While the spinner shows, focus waits on its row; when Check Now returns, it goes back to it.
      const row = form("general").querySelector(".sw__spinner")?.closest("[data-row]");
      const stay = row && row === document.activeElement.closest?.("[data-row]");
      update = "upToDate";
      rerender("general");
      if (stay) form("general").querySelector('[data-action="check"]')?.focus({ preventScroll: true });
    }, CHECK_TIME);
  }

  /** Changes a setting. The app plays its ring whenever a preset becomes the active one. */
  function apply(id, value) {
    const before = activePreset(state)?.id ?? null;
    state = value === undefined ? change(state, id) : change(state, id, value);
    const after = activePreset(state)?.id ?? null;
    if (after && after !== before) stage.playRing();
  }

  win.addEventListener("click", (e) => {
    const t = e.target.closest("[data-tab], [data-set], [data-toggle], [data-action]");
    if (!t || !win.contains(t) || t.matches("select, input")) return;
    if (t.dataset.tab) return selectPane(t.dataset.tab);
    if (t.dataset.set) {
      const id = t.dataset.set;
      apply(id, id.startsWith("preset-") ? undefined : t.getAttribute("aria-checked") !== "true");
      return rerender();
    }
    if (t.dataset.toggle) {
      apply(t.dataset.toggle);
      return rerender();
    }
    switch (t.dataset.action) {
      case "check":
        return check();
      case "tour":
        return document.getElementById("problem")?.scrollIntoView({ behavior: reducedMotion ? "auto" : "smooth", block: "start" });
      case "addApp":
        apply("addApp");
        return rerender();
      case "remove":
        apply("rule.remove");
        return rerender();
    }
  });

  win.addEventListener("change", (e) => {
    const id = e.target.dataset?.set;
    if (!id) return;
    apply(id, e.target.value);
    rerender();
  });

  // Sliders update as they move; the form re-renders when they're let go (the change event).
  win.addEventListener("input", (e) => {
    const t = e.target;
    if (t.type !== "range" || !t.dataset.set) return;
    const id = t.dataset.set;
    apply(id, t.value);
    t.style.setProperty("--p", String((t.valueAsNumber - Number(t.min)) / (Number(t.max) - Number(t.min))));
    const row = findRow(devicesSections({ ...state, showFineTuning: true }), id);
    const reading = win.querySelector(`[data-reading="${CSS.escape(id)}"]`);
    if (reading && row?.reading) reading.textContent = t(row.reading);
    const el = t.closest("[data-row]");
    if (row?.preview && el) {
      el.dataset.preview = JSON.stringify(row.preview);
      if (focusRow === id) explain(el);
    }
  });

  // ---------- Pointing and focus explain a row ----------
  const devicesForm = form("devices");
  devicesForm.addEventListener("pointerover", (e) => {
    if (e.pointerType === "touch") return;
    const row = e.target.closest("[data-row]");
    if (row && row.dataset.row !== focusRow) {
      explain(row);
      keyboardExplained = false;
    }
  });
  devicesForm.addEventListener("pointerleave", (e) => {
    if (e.pointerType !== "touch") clearExplanation();
  });
  devicesForm.addEventListener("pointerdown", (e) => {
    if (e.pointerType !== "touch") return;
    const row = e.target.closest("[data-row]");
    if (row) {
      explain(row);
      keyboardExplained = false;
    }
  });
  // On touch nothing says the pointer left, so a tap anywhere else puts the explanation away.
  document.addEventListener("pointerdown", (e) => {
    if (e.pointerType === "touch" && focusRow && !devicesForm.contains(e.target)) clearExplanation();
  });
  devicesForm.addEventListener("focusin", (e) => {
    if (rerendering) return;
    const row = e.target.closest("[data-row]");
    if (row && e.target.matches(":focus-visible")) {
      explain(row);
      keyboardExplained = true;
    }
  });
  devicesForm.addEventListener("focusout", (e) => {
    if (!rerendering && keyboardExplained && !devicesForm.contains(e.relatedTarget)) clearExplanation();
  });

  // ---------- Popovers on Advanced's headings ----------
  const popover = document.createElement("div");
  popover.className = "sw__popover";
  popover.hidden = true;
  popover.setAttribute("role", "tooltip");
  win.append(popover);
  popover.id = "sw-popover";
  let popoverTimer = 0;
  let popoverFor = null;
  function hidePopover() {
    clearTimeout(popoverTimer);
    popover.hidden = true;
    popoverFor?.removeAttribute("aria-describedby");
    popoverFor = null;
  }
  function showPopover(heading) {
    const box = win.getBoundingClientRect();
    const text = heading.querySelector("span").getBoundingClientRect();
    const k = scale();
    popover.replaceChildren(...heading.dataset.caption.split("\n").flatMap((l, i) => (i ? [document.createElement("br"), l] : [l])));
    popover.style.left = `${(text.left - box.left) / k}px`;
    popover.style.top = `${(text.bottom - box.top) / k + 6}px`;
    popover.hidden = false;
    popoverFor = heading;
    heading.setAttribute("aria-describedby", popover.id);
  }
  // Headings with a caption can be reached from the keyboard, so their popover isn't pointer-only.
  function decorateHeadings(root = win) {
    for (const h of root.querySelectorAll(".sw__heading[data-caption]")) h.tabIndex = 0;
  }
  decorateHeadings();
  win.addEventListener("pointerover", (e) => {
    if (e.pointerType === "touch") return;
    const heading = e.target.closest(".sw__heading[data-caption]");
    if (!heading || !popover.hidden) return;
    clearTimeout(popoverTimer);
    popoverTimer = setTimeout(() => showPopover(heading), POPOVER_DELAY);
  });
  win.addEventListener("pointerout", (e) => {
    if (e.pointerType === "touch") return;
    const heading = e.target.closest(".sw__heading[data-caption]");
    if (heading && !heading.contains(e.relatedTarget)) hidePopover();
  });
  win.addEventListener("focusin", (e) => {
    const heading = e.target.closest?.(".sw__heading[data-caption]");
    if (!heading || !heading.matches(":focus-visible")) return;
    hidePopover();
    showPopover(heading);
  });
  win.addEventListener("focusout", (e) => {
    if (e.target.closest?.(".sw__heading[data-caption]")) hidePopover();
  });

  // ---------- Fitting the page ----------
  const parent = frame.parentElement;
  const phone = window.matchMedia("(max-width: 640px)");
  function fit() {
    const style = getComputedStyle(parent);
    const room = parent.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    frame.style.setProperty("--sw-scale", phone.matches ? "1" : String(Math.min(1, room / 800)));
  }
  new ResizeObserver(fit).observe(parent);
  fit();

  // The ring plays when the mouse first comes into view, as it does when it connects.
  const seen = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) {
      seen.disconnect();
      if (pane === "devices") stage.playRing();
    }
  }, { threshold: 0.4 });
  seen.observe(win.querySelector(".sw__stage"));

  // The stage rests while the window is off screen, however a row came to be explained.
  new IntersectionObserver((entries) => {
    const entry = entries[entries.length - 1];
    visible = entry.isIntersecting;
    stage.setActive(visible && pane === "devices");
  }, { threshold: 0 }).observe(win);
}
