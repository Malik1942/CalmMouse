// The English page (index.html) and the Chinese page (zh/index.html) are the same page in two
// languages, kept as two files. These checks catch one being changed without the other.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { formatDownloadCount } from "../src/download-count.js";

const read = (p) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), "utf8");
const en = read("../index.html");
const zh = read("../zh/index.html");
const all = (html, re) => [...html.matchAll(re)].map((m) => m[1]);

test("both pages have the same sections, in the same order", () => {
  const ids = (html) => all(html, /<(?:section|header)[^>]* id="([^"]+)"/g);
  assert.deepEqual(ids(zh), ids(en));
});

test("both pages list the same releases, with the same dates", () => {
  assert.deepEqual(all(zh, /class="changelog__version">([^<]+)</g), all(en, /class="changelog__version">([^<]+)</g));
  assert.deepEqual(all(zh, /<time class="changelog__date" datetime="([^"]+)"/g), all(en, /<time class="changelog__date" datetime="([^"]+)"/g));
});

test("both pages show the same pictures and link to the same places", () => {
  const srcs = (html) => all(html, /(?:src|srcset)="([^"]+)"/g).sort();
  const links = (html) =>
    all(html, /<a [^>]*href="([^"]+)"/g)
      .filter((h) => h !== "/" && h !== "/zh") // the language switch points at the other page
      .sort();
  assert.deepEqual(srcs(zh), srcs(en));
  assert.deepEqual(links(zh), links(en));
});

test("each page names its language, its own address, and the other page", () => {
  assert.match(en, /<html lang="en">/);
  assert.match(zh, /<html lang="zh-Hans">/);
  assert.match(en, /<link rel="canonical" href="https:\/\/calmmouse\.malikzhang\.com\/" \/>/);
  assert.match(zh, /<link rel="canonical" href="https:\/\/calmmouse\.malikzhang\.com\/zh" \/>/);
  for (const html of [en, zh]) {
    assert.match(html, /hreflang="en" href="https:\/\/calmmouse\.malikzhang\.com\/"/);
    assert.match(html, /hreflang="zh-Hans" href="https:\/\/calmmouse\.malikzhang\.com\/zh"/);
  }
  assert.match(en, /<a class="site-nav__link" href="\/zh" lang="zh-Hans"/);
  assert.match(zh, /<a class="site-nav__link" href="\/" lang="en"/);
});

test("both pages show all three kinds of device", () => {
  for (const html of [en, zh]) {
    assert.equal(all(html, /class="(devices__item)"/g).length, 3);
    for (const img of ["/magic-mouse/white/home.webp", "/devices/wheel-white.webp", "/devices/controller.webp"]) assert.ok(html.includes(img), img);
  }
});

test("the download count speaks the page's language", () => {
  assert.equal(formatDownloadCount(1284), "1,284 downloads");
  assert.equal(formatDownloadCount(1284, "zh-Hans"), "1,284 次下载");
});
