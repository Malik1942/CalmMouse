// The settings window's language. English is the source: the model and the app's Swift both say
// it in English, and Chinese comes from the app's own strings (strings-zh.js), so the website's
// window reads exactly like the app's. setLanguage() is called once per page: by vite.config.js
// when it renders the window into the HTML, and by window.js when the page starts.

import { APP_ZH } from "./strings-zh.js";

/** Strings only the website shows (labels for screen readers, mostly), in the app's voice. */
const SITE_ZH = {
  "CalmMouse Settings, to try": "CalmMouse 设置，可以试试",
  Settings: "设置",
  "In use": "正在使用",
  "Use %@": "使用“%@”",
  "Checking for updates": "正在检查更新",
  Paused: "已暂停",
  Preview: "预览",
  "Magic Mouse. Drag or use the arrow keys to turn it; double-click or press Escape to put it back. Right-click for its colour.":
    "妙控鼠标。拖动或用方向键可旋转，双击或按 Esc 复原。右键点按可换颜色。",
};
const TABLES = { zh: { ...APP_ZH, ...SITE_ZH } };

let lang = "en";
export const setLanguage = (l) => {
  lang = l && String(l).toLowerCase().startsWith("zh") ? "zh" : "en";
};
export const language = () => lang;

const FORMAT = /%(?:@|l?d|%)/;
const formatsCache = new Map();
function formats(table) {
  if (!formatsCache.has(table)) {
    const list = [];
    for (const [key, value] of Object.entries(table)) {
      if (!FORMAT.test(key)) continue;
      const pattern = key
        .split(/(%@|%l?d|%%)/)
        .map((part) => (part === "%@" ? "(.+?)" : part === "%d" || part === "%ld" ? "(\\d+)" : part === "%%" ? "%" : part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")))
        .join("");
      list.push([new RegExp(`^${pattern}$`), value]);
    }
    formatsCache.set(table, list);
  }
  return formatsCache.get(table);
}

function lookup(table, s) {
  if (s in table) return table[s];
  for (const [re, value] of formats(table)) {
    const m = s.match(re);
    if (!m) continue;
    let i = 1;
    return value.replace(/%(?:@|l?d|%)/g, (spec) => (spec === "%%" ? "%" : m[i++]));
  }
  return null;
}

/** A caption the app builds from several lines: translate it piece by piece, longest pieces first. */
function lookupLines(table, s) {
  const lines = s.split("\n");
  if (lines.length < 2) return null;
  const best = new Array(lines.length + 1).fill(null);
  best[lines.length] = [];
  for (let i = lines.length - 1; i >= 0; i--) {
    for (let j = lines.length; j > i; j--) {
      const piece = lookup(table, lines.slice(i, j).join("\n"));
      if (piece != null && best[j]) {
        best[i] = [piece, ...best[j]];
        break;
      }
    }
  }
  return best[0] ? best[0].join("\n") : null;
}

/** `s` in the page's language; English, or anything without a translation, comes back as it is. */
export function t(s) {
  if (lang === "en" || s == null || s === "") return s;
  const table = TABLES[lang];
  return lookup(table, s) ?? lookupLines(table, s) ?? s;
}
