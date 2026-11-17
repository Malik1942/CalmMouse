import { defineConfig } from "vite";
import { renderStaticWindow } from "./src/settings/render.js";

// The settings window is rendered into index.html at build time, so it shows without JavaScript
// and the page doesn't move when the script takes over. Its version is the newest changelog entry.
export default defineConfig({
  plugins: [
    {
      name: "calmmouse-settings-window",
      transformIndexHtml(html) {
        const version = html.match(/class="changelog__version">([^<]+)</)?.[1]?.trim();
        if (!version) throw new Error("index.html has no .changelog__version to read the version from");
        if (!html.includes("<!-- settings-window -->")) throw new Error("index.html has no <!-- settings-window --> placeholder");
        return html.replace("<!-- settings-window -->", renderStaticWindow(version));
      },
    },
  ],
});
