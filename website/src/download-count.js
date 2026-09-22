// The live download count, on its own small-print line under the hero.
//
// index.html already reserves the line (a non-breaking space holds its
// height), so filling it in never moves the page. It stays blank until the
// total reaches MIN_DISPLAY_COUNT, and on any failure: no number is the
// designed fallback, so nothing is logged either.

export const MIN_DISPLAY_COUNT = 500;

// formatDownloadCount(500)    → "500 downloads"
// formatDownloadCount(1284)   → "1,284 downloads"
// formatDownloadCount(15800)  → "15,800 downloads"
// formatDownloadCount(100000) → "100,000 downloads"
export function formatDownloadCount(n) {
  return `${n.toLocaleString("en-US")} downloads`;
}

export async function showDownloadCount() {
  const line = document.querySelector("[data-download-count]");
  if (!line) return;
  try {
    const response = await fetch("/api/downloads");
    if (!response.ok) return;
    const { total } = await response.json();
    if (!Number.isFinite(total) || total < MIN_DISPLAY_COUNT) return;
    line.textContent = formatDownloadCount(total);
    line.removeAttribute("aria-hidden");
  } catch {
    // Offline, a non-JSON reply (plain `npm run dev` has no api/), or anything
    // else: stay blank.
  }
}
