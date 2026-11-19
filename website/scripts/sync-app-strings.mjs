#!/usr/bin/env node
// Copies the app's Simplified Chinese strings (Resources/zh-Hans.lproj/Localizable.strings) into
// src/settings/strings-zh.js, so the website's settings window says exactly what the app says.
// Run from website/: `node scripts/sync-app-strings.mjs`. test/i18n.test.js fails when the copy
// falls behind the app's file.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { parseStrings, stringsModule } from "../src/settings/strings-format.js";

const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const source = readFileSync(here("../../Resources/zh-Hans.lproj/Localizable.strings"), "utf8");
writeFileSync(here("../src/settings/strings-zh.js"), stringsModule(parseStrings(source)));
console.log("wrote src/settings/strings-zh.js");
