// The settings the website's window shows and their rules, taken from the app's
// Sources/CalmMouse/SettingsWindow.swift and Sources/CalmMouseCore/Presets.swift. No DOM:
// render.js draws what this describes and window.js changes it through change().

/** The app's factory configuration: what PresetValues() is. */
export const FACTORY = Object.freeze({
  blockScrollWhileClicked: true,
  releaseGraceMs: 200,
  deadZone: 0,
  axisLock: false,
  axisLockThreshold: 10,
  momentumEnabled: true,
  blockHorizontalScroll: false,
  tapToClick: false,
  tapSensitivity: 0.5,
  tapRightClick: false,
  tapRightClickMode: "rightSide",
  tapAndDrag: false,
  twoFingerDrag: false,
  tapZoneEnabled: false,
  tapZoneDepth: 0.5,
});
const PRESET_KEYS = Object.keys(FACTORY);

export const PRESETS = [
  {
    id: "justFixClicking", name: "Just fix clicking", icon: "cursorClick", story: { kind: "blockWhileClick" },
    summary: "Pages hold still while you click.\nNothing else changes.",
    values: { ...FACTORY },
  },
  {
    id: "extraSteady", name: "Extra steady", icon: "scope", story: { kind: "straightLines" },
    summary: "Ignores nudges and diagonal drift.\nThe page stops when your finger does.",
    values: { ...FACTORY, releaseGraceMs: 300, deadZone: 8, axisLock: true, momentumEnabled: false },
  },
  {
    id: "trackpadFeel", name: "Trackpad feel", icon: "handTap", story: { kind: "tapToClick" },
    summary: "Tap to click and right-click.\nDouble Tap Drag to move things.",
    values: { ...FACTORY, tapToClick: true, tapRightClick: true, tapAndDrag: true },
  },
];

export const BATTERY_LEVELS = [10, 15, 20, 30, 50];
export const SCROLL_ACTIONS = [
  ["normal", "Scroll normally"],
  ["horizontal", "Scroll sideways"],
  ["invert", "Scroll in reverse"],
  ["zoomCommand", "Zoom in apps"],
  ["zoomControl", "Zoom the whole screen"],
  ["block", "Do nothing"],
];
export const SINGLE_KEYS = ["⌘", "⌥", "⌃", "⇧"];
export const KEY_COMBINATIONS = ["⌥⌘", "⇧⌘", "⌃⌥"];
/** The app Choose… adds, so visitors can see what a rule looks like. */
export const EXAMPLE_APP = "Preview";
export const REPOSITORY = "https://github.com/Malik1942/CalmMouse";
export const NEW_ISSUE = "https://github.com/Malik1942/CalmMouse/issues/new/choose";
export const IDLE_CAPTION = "Point at a setting\nto see what it does.";

const RULE_OVERRIDES = [
  ["disableScrollEntirely", "Ignore Magic Mouse scrolling"],
  ["blockScrollWhileClicked", "Don't scroll while clicking"],
  ["blockHorizontalScroll", "Ignore sideways scrolling"],
  ["momentumEnabled", "Keep gliding after a swipe"],
  ["axisLock", "Scroll in straight lines"],
];
const TRI = [{ value: 0, label: "Default" }, { value: 1, label: "On" }, { value: 2, label: "Off" }];
const tri = (value) => (value == null ? 0 : value ? 1 : 2);

export function createSettings() {
  return {
    ...FACTORY,
    reverseMagicMouseScroll: false,
    trackingSpeed: 0,
    treatAs: "automatic",
    showFineTuning: false,
    enabled: true,
    launchAtLogin: true,
    batteryWarning: 15,
    modifierActions: {},
    showCombinations: false,
    rule: null,
    ruleExpanded: false,
  };
}

export const activePreset = (state) => PRESETS.find((p) => PRESET_KEYS.every((k) => state[k] === p.values[k])) ?? null;

const direction = (s) => (s.blockHorizontalScroll ? 2 : s.axisLock ? 1 : 0);
const rightClickChoice = (s) => (!s.tapRightClick ? 0 : s.tapRightClickMode === "rightSide" ? 1 : 2);
const percent = (x) => `${Math.round(x * 100)}%`;
const seconds = (ms) => `${Number((ms / 1000).toPrecision(2))} s`;

const switchRow = (id, label, on, caption, preview = null) => ({ type: "switch", id, label, on, caption, preview });
const slider = (id, label, value, { min, max, step = null, ends = null, reading = null, caption, preview = null }) =>
  ({ type: "slider", id, label, value, min, max, step, ends, reading, caption, preview });
const popup = (id, label, value, options, caption = null, preview = null) => ({ type: "popup", id, label, value, options, caption, preview });

export function devicesSections(s) {
  const active = activePreset(s);
  const sections = [];
  sections.push({
    id: "presets", title: "Presets",
    rows: PRESETS.map((p) => ({
      type: "preset", id: `preset-${p.id}`, preset: p.id, label: p.name, icon: p.icon,
      active: active?.id === p.id, caption: p.summary, preview: p.story,
    })),
  });
  sections.push({
    id: "pointer", title: "Pointer",
    rows: [slider("trackingSpeed", "Tracking speed", s.trackingSpeed, {
      min: -1, max: 1, step: 0.25, ends: ["Slow", "Fast"],
      caption: "How far the pointer goes\nwhen you move the Magic Mouse.",
    })],
  });
  sections.push({
    id: "scrolling", title: "Scrolling",
    rows: [
      switchRow("blockScrollWhileClicked", "Don't scroll while clicking", s.blockScrollWhileClicked,
        "The page stays put while you click.", { kind: "blockWhileClick" }),
      popup("direction", "Direction", direction(s),
        [{ value: 0, label: "Any direction" }, { value: 1, label: "Straight lines" }, { value: 2, label: "Up and down only" }],
        "Straight lines: a swipe can't drift.\nUp and down only: no sideways at all.",
        { kind: s.blockHorizontalScroll ? "ignoreSideways" : "straightLines" }),
      switchRow("momentumEnabled", "Keep gliding after a swipe", s.momentumEnabled,
        "On: the page glides after a swipe.\nOff: it stops when your finger does.", { kind: "momentum", on: s.momentumEnabled }),
      switchRow("reverseMagicMouseScroll", "Reverse direction", s.reverseMagicMouseScroll,
        "Flips only the Magic Mouse.\nThe trackpad stays as it is.", { kind: "reverseScroll", on: s.reverseMagicMouseScroll }),
    ],
  });
  const clicking = [switchRow("tapToClick", "Tap to click", s.tapToClick,
    "A light tap clicks.\nPressing the mouse still works.", { kind: "tapToClick" })];
  if (s.tapToClick) {
    if (s.tapRightClick && s.tapRightClickMode === "doubleTap") {
      clicking.push(popup("rightClick", "Tap to right-click", rightClickChoice(s),
        [{ value: 0, label: "Off" }, { value: 1, label: "A tap on the right side" }, { value: 2, label: "A double-tap" }],
        "Tap twice quickly to right-click.\nPress the mouse to double-click.", { kind: "tapRightClick", doubleTap: true }));
    } else {
      clicking.push(switchRow("tapsRightSide", "Tap the right side to right-click", s.tapRightClick,
        "Tap the right side to right-click.\nTaps elsewhere are normal clicks.", { kind: "tapRightClick", doubleTap: false }));
    }
    const rightSide = s.tapRightClick && s.tapRightClickMode === "rightSide";
    clicking.push(switchRow("tapAndDrag", "Double Tap Drag", s.tapAndDrag,
      "Tap twice and leave your finger down.\nMove the mouse right away to drag.\nLift your finger to drop."
        + (rightSide ? "\nUse the left or middle of the mouse." : ""), { kind: "tapAndDrag" }));
    clicking.push(switchRow("tapZoneEnabled", "Front of the mouse only", s.tapZoneEnabled,
      "Only taps near the front count.\nYour grip can't click by accident.", { kind: "tapZone", depth: s.tapZoneDepth }));
  }
  sections.push({ id: "clicking", title: "Clicking", rows: clicking });

  const fine = [];
  if (s.showFineTuning) {
    if (s.blockScrollWhileClicked) {
      fine.push(slider("releaseGraceMs", "Hold still after you let go", s.releaseGraceMs, {
        min: 0, max: 800, step: 50, reading: s.releaseGraceMs === 0 ? "Off" : seconds(s.releaseGraceMs),
        caption: "A short pause after you let go,\nso a lifting finger can't scroll.", preview: { kind: "settleAfterRelease" },
      }));
    }
    fine.push(slider("deadZone", "Ignore small finger nudges", s.deadZone, {
      min: 0, max: 40, step: 1, reading: s.deadZone === 0 ? "Off" : `${Math.trunc(s.deadZone)} pt`,
      caption: "Scrolling waits for this much travel.\nA resting finger can't move the page.", preview: { kind: "ignoreNudges" },
    }));
    if (s.axisLock && !s.blockHorizontalScroll) {
      fine.push(slider("axisLockThreshold", "Lock direction after sliding", s.axisLockThreshold, {
        min: 2, max: 40, step: 1, reading: `${Math.trunc(s.axisLockThreshold)} pt`,
        caption: "How far a swipe goes before it locks.",
      }));
    }
    if (s.tapToClick) {
      fine.push(slider("tapSensitivity", "Tap firmness", s.tapSensitivity, {
        min: 0, max: 1, ends: ["Firm", "Light"],
        caption: "Firm: only quick, deliberate taps.\nLight: gentler, slower taps too.", preview: { kind: "tapToClick" },
      }));
      if (s.tapZoneEnabled) {
        fine.push(slider("tapZoneDepth", "Size of the tap area", s.tapZoneDepth, {
          min: 0.25, max: 0.75, step: 0.05, reading: percent(s.tapZoneDepth),
          caption: "How far back from the front\na tap still counts.", preview: { kind: "tapZone", depth: s.tapZoneDepth },
        }));
      }
      fine.push(switchRow("twoFingerDrag", "Two-finger drag", s.twoFingerDrag,
        "Rest two fingers and hold a moment.\nMove the mouse to drag.\nLift your fingers to drop.", { kind: "twoFingerDrag" }));
    }
  }
  sections.push({ id: "fineTuning", title: "Fine-tuning", disclosure: true, expanded: s.showFineTuning, rows: fine });

  sections.push({
    id: "treatAs",
    rows: [popup("treatAs", "Treat as", s.treatAs,
      [{ value: "automatic", label: "Automatic" }, { divider: true }, { value: "magicMouse", label: "Magic Mouse" }, { value: "wheelMouse", label: "Wheel mouse" }],
      "Only if CalmMouse took this mouse\nfor the wrong kind.")],
  });
  return sections;
}

export function generalSections(s, { version, update }) {
  return [
    {
      id: "calmmouse", title: "CalmMouse",
      rows: [
        switchRow("enabled", "Enable CalmMouse", s.enabled),
        switchRow("launchAtLogin", "Launch at login", s.launchAtLogin),
        popup("batteryWarning", "Low battery warning", s.batteryWarning,
          [{ value: 0, label: "Off" }, { divider: true }, ...BATTERY_LEVELS.map((n) => ({ value: n, label: `Below ${n}%` }))]),
      ],
    },
    {
      id: "about", title: "About",
      rows: [
        { type: "version", id: "version", label: `Version ${version}`, update },
        { type: "button", id: "tour", label: "Welcome Tour", button: "Show", action: "tour" },
      ],
    },
    {
      id: "support", title: "Support",
      rows: [
        { type: "button", id: "feedback", label: "Something not right?", button: "Send Feedback…", href: NEW_ISSUE },
        { type: "button", id: "star", label: "Like CalmMouse?", button: "Star on GitHub", icon: "star", href: REPOSITORY },
      ],
    },
  ];
}

export function advancedSections(s) {
  const apps = [];
  if (s.rule) {
    apps.push({ type: "disclosure", id: "rule", label: EXAMPLE_APP, icon: "appDashed", expanded: s.ruleExpanded, paused: !s.rule.enabled });
    if (s.ruleExpanded) {
      apps.push(switchRow("rule.enabled", "Rule enabled", s.rule.enabled));
      for (const [key, label] of RULE_OVERRIDES) apps.push(popup(`rule.${key}`, label, tri(s.rule[key]), TRI));
      apps.push({ type: "remove", id: "rule.remove", label: "Remove" });
    }
  }
  apps.push({ type: "button", id: "addApp", label: "Add an App", button: "Choose…", action: "addApp" });

  const actions = SCROLL_ACTIONS.map(([value, label]) => ({ value, label }));
  const shortcut = (combo) => popup(`key.${combo}`, `While holding ${combo}`, s.modifierActions[combo] ?? "normal", actions);
  const shortcuts = SINGLE_KEYS.map(shortcut);
  shortcuts.push({ type: "disclosure", id: "combinations", label: "More key combinations", expanded: s.showCombinations });
  if (s.showCombinations) shortcuts.push(...KEY_COMBINATIONS.map(shortcut));

  return [
    { id: "apps", title: "Apps", caption: "Rules apply while that app is in front.\n“Default” follows the Magic Mouse page.", rows: apps },
    { id: "shortcuts", title: "Shortcuts", caption: "Hold a key while you scroll\nto do the action you picked.", rows: shortcuts },
  ];
}

export const findRow = (sections, id) => sections.flatMap((s) => s.rows).find((r) => r.id === id) ?? null;

const newRule = () => ({
  enabled: true, disableScrollEntirely: false, blockScrollWhileClicked: null,
  blockHorizontalScroll: null, momentumEnabled: null, axisLock: null,
});

/** The settings after the control `id` is set to `value` (or pressed, for buttons and toggles). */
export function change(s, id, value) {
  switch (id) {
    case "direction": {
      const choice = Number(value);
      const next = { ...s, blockHorizontalScroll: choice === 2 };
      if (choice !== 2) next.axisLock = choice === 1;
      return next;
    }
    case "rightClick": {
      const choice = Number(value);
      const next = { ...s, tapRightClick: choice !== 0 };
      if (choice === 1) next.tapRightClickMode = "rightSide";
      if (choice === 2) next.tapRightClickMode = "doubleTap";
      return next;
    }
    case "tapsRightSide":
      return value ? { ...s, tapRightClick: true, tapRightClickMode: "rightSide" } : { ...s, tapRightClick: false };
    case "fineTuning":
      return { ...s, showFineTuning: !s.showFineTuning };
    case "combinations":
      return { ...s, showCombinations: !s.showCombinations };
    case "addApp":
      return s.rule ? { ...s, ruleExpanded: true } : { ...s, rule: newRule(), ruleExpanded: true };
    case "rule":
      return { ...s, ruleExpanded: !s.ruleExpanded };
    case "rule.remove":
      return { ...s, rule: null, ruleExpanded: false };
    case "rule.enabled":
      return { ...s, rule: { ...s.rule, enabled: Boolean(value) } };
    case "treatAs":
      return { ...s, treatAs: value };
  }
  if (id.startsWith("preset-")) {
    const preset = PRESETS.find((p) => p.id === id.slice("preset-".length));
    return { ...s, ...preset.values };
  }
  if (id.startsWith("rule.")) {
    const n = Number(value);
    return { ...s, rule: { ...s.rule, [id.slice("rule.".length)]: n === 0 ? null : n === 1 } };
  }
  if (id.startsWith("key.")) {
    const actions = { ...s.modifierActions };
    if (value === "normal") delete actions[id.slice("key.".length)];
    else actions[id.slice("key.".length)] = value;
    return { ...s, modifierActions: actions };
  }
  if (id in s) return { ...s, [id]: typeof s[id] === "number" ? Number(value) : Boolean(value) };
  throw new Error(`No setting called ${id}`);
}
