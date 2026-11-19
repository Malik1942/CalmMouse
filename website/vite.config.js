import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";
import { renderStaticWindow } from "./src/settings/render.js";

// Two pages: English at / and Chinese at /zh (zh/index.html), the same page in two languages.
//
// The settings window is rendered into each page at build time, so it shows without JavaScript
// and the page doesn't move when the script takes over. Its version is the newest changelog entry,
// and its language is the page's <html lang>.
export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL("./index.html", import.meta.url)),
        zh: fileURLToPath(new URL("./zh/index.html", import.meta.url)),
      },
    },
  },
  plugins: [
    {
      name: "calmmouse-settings-window",
      transformIndexHtml(html) {
        const version = html.match(/class="changelog__version">([^<]+)</)?.[1]?.trim();
        if (!version) throw new Error("index.html has no .changelog__version to read the version from");
        if (!html.includes("<!-- settings-window -->")) throw new Error("index.html has no <!-- settings-window --> placeholder");
        const lang = html.match(/<html lang="([^"]+)"/)?.[1] ?? "en";
        return html.replace("<!-- settings-window -->", () => renderStaticWindow(version, lang));
      },
    },
    {
      name: "calmmouse-zh-path",
      // The Chinese page is /zh (vercel.json rewrites it in production); serve it there in dev too.
      configureServer(server) {
        server.middlewares.use((req, _res, next) => {
          if (req.url === "/zh" || req.url.startsWith("/zh?")) req.url = req.url.replace("/zh", "/zh/");
          next();
        });
      },
    },
  ],
});
