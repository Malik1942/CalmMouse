// Look-alikes of the symbols the app's Settings window uses, drawn for the website: Apple's
// SF Symbols licence covers app interfaces, not websites. Stroked in currentColor, on a 24 × 24
// box unless they say otherwise; the toolbar and preset icons are drawn at 24 pt, so one unit
// is a point there.

/** A gear outline: `teeth` flat teeth between radii `inner` and `outer`, with a hole. */
function gear(cx, cy, outer, inner, teeth, hole) {
  const step = (2 * Math.PI) / teeth;
  const points = [];
  for (let i = 0; i < teeth; i++) {
    const a = i * step - Math.PI / 2;
    points.push([a - step * 0.27, inner], [a - step * 0.13, outer], [a + step * 0.13, outer], [a + step * 0.27, inner]);
  }
  const d = points
    .map(([a, r], i) => `${i ? "L" : "M"}${(cx + r * Math.cos(a)).toFixed(2)} ${(cy + r * Math.sin(a)).toFixed(2)}`)
    .join("");
  return `<path d="${d}Z"/><circle cx="${cx}" cy="${cy}" r="${hole}"/>`;
}

const svg = (body, { width = 1.5, box = "0 0 24 24", attrs = "" } = {}) =>
  `<svg viewBox="${box}" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="${width}" stroke-linecap="round" stroke-linejoin="round"${attrs}>${body}</svg>`;

/** A five-pointed star outline: points at radius `outer`, notches at `inner`. */
function star(cx, cy, outer, inner) {
  const d = Array.from({ length: 10 }, (_, i) => {
    const a = ((-90 + 36 * i) * Math.PI) / 180;
    const r = i % 2 ? inner : outer;
    return `${i ? "L" : "M"}${(cx + r * Math.cos(a)).toFixed(2)} ${(cy + r * Math.sin(a)).toFixed(2)}`;
  }).join("");
  return `<path d="${d}Z"/>`;
}

export const ICONS = {
  gear: svg(gear(12, 12, 9.5, 7.2, 8, 3)),
  mouse: svg('<rect x="5.75" y="2.25" width="12.5" height="20" rx="6.25"/><path d="M12 2.25v5.5M5.8 7.75h12.4"/>'),
  // Two gears side by side, wider than high: drawn on a 28 × 24 box.
  gears: svg(gear(8.25, 9.25, 7.1, 5.38, 8, 2.2) + gear(21, 16.5, 5.85, 4.43, 8, 1.6), { width: 1.3, box: "0 0 28 24" }),
  // An arrow pointer inside two rings that open at the lower left.
  cursorClick: svg(
    '<path d="M8.21 16.91A6.6 6.6 0 1 1 18.6 11.5M9.94 14.45A3.6 3.6 0 1 1 15.6 11.5"/>' +
      '<path d="M12 11.3v6.5l1.4-1.2 1.2 2.7 1-.5-1.2-2.6 1.9-.1z" fill="currentColor" stroke-width="1"/>',
    { width: 1.25 },
  ),
  scope: svg('<circle cx="12" cy="11.5" r="6"/><path d="M3.6 11.5h4.6M15.8 11.5h4.6M12 3.1v4.6M12 15.3v4.6"/>', { width: 1.3 }),
  // A pointing hand, with rings beside its fingertip.
  handTap: svg(
    '<path d="M8.4 13.2V6.6a.9.9 0 0 1 1.8 0v3.8a1.15 1.15 0 0 1 2.3.3 1.15 1.15 0 0 1 2.2.6l1.4.3a2.2 2.2 0 0 1 2.2 2.7l-.4 1.9a2.6 2.6 0 0 1-2.5 2.1h-2a2.6 2.6 0 0 1-2-.9l-3.6-3.9a.95.95 0 0 1 1.4-1.3z"/>' +
      '<path d="M7.22 8.4A2.4 2.4 0 0 1 11.14 5.66M5.86 9.61A4.2 4.2 0 0 1 12.74 4.79"/>',
    { width: 1.25 },
  ),
  star: svg(star(12, 13.75, 10.68, 5.02)),
  checkCircle: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="10" fill="var(--sw-green)"/><path d="M7.6 12.4l3 3 5.8-6.3" fill="none" stroke="var(--sw-on-green)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  // Drawn in points on its own 8 × 10 box: the app's chevron is about 8 pt wide wherever it sits.
  chevronRight: svg('<path d="M1.9.9L6.1 5 1.9 9.1"/>', { width: 1.3, box: "0 0 8 10", attrs: ' class="sw__chevron"' }),
  chevronsUpDown: svg('<path d="M5.75 8.75L12 2.75l6.25 6M5.75 15.25L12 21.25l6.25-6"/>', { width: 2.5 }),
  appDashed: svg('<rect x="3.5" y="3.5" width="17" height="17" rx="4.5" stroke-dasharray="3 2.4"/>'),
  pauseCircle: svg('<circle cx="12" cy="12" r="8.5"/><path d="M10 9v6M14 9v6"/>'),
};
