// Look-alikes of the symbols the app's Settings window uses, drawn for the website: Apple's
// SF Symbols licence covers app interfaces, not websites. 24 × 24, stroked in currentColor.

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

const svg = (body, attrs = "") =>
  `<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"${attrs}>${body}</svg>`;

export const ICONS = {
  gear: svg(gear(12, 12, 9.5, 7.2, 8, 3)),
  mouse: svg('<rect x="7" y="2.75" width="10" height="18.5" rx="5"/><path d="M12 2.75v4.5"/>'),
  gears: svg(gear(9.5, 9, 7, 5.3, 8, 2.1) + gear(17.5, 17, 4.8, 3.6, 6, 1.4), ' stroke-width="1.3"'),
  cursorClick: svg('<path d="M10.5 9.5l8.5 3.6-3.7 1.1 2.4 4-1.5.9-2.4-4-2.6 2.8z"/><path d="M8.6 6.2L7.1 4.7M10.8 5.4V3.5M6.6 8.4H4.7"/>'),
  scope: svg('<circle cx="12" cy="12" r="7"/><path d="M12 2.5v5M12 16.5v5M2.5 12h5M16.5 12h5"/>'),
  handTap: svg('<path d="M10.2 13.2V7.3a1.5 1.5 0 0 1 3 0v4.8l3.6.7a2 2 0 0 1 1.6 2.3l-.6 3.3a2.6 2.6 0 0 1-2.6 2.1h-3a2.6 2.6 0 0 1-2.1-1.1l-3-4.2a1.4 1.4 0 0 1 2.2-1.7z"/><path d="M8.6 7.3a3.1 3.1 0 0 1 6.2 0M6.8 7.3a4.9 4.9 0 0 1 9.8 0"/>'),
  star: svg('<path d="M12 3.6l2.5 5.3 5.8.7-4.3 4 1.1 5.7L12 16.5l-5.1 2.8 1.1-5.7-4.3-4 5.8-.7z"/>'),
  checkCircle: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="10" fill="var(--sw-green)"/><path d="M7.6 12.4l3 3 5.8-6.3" fill="none" stroke="var(--sw-on-green)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  chevronRight: svg('<path d="M9.5 6.5l5.5 5.5-5.5 5.5"/>', ' class="sw__chevron" stroke-width="2.4"'),
  chevronsUpDown: svg('<path d="M8.5 9.5L12 6l3.5 3.5M8.5 14.5L12 18l3.5-3.5"/>', ' stroke-width="2"'),
  appDashed: svg('<rect x="3.5" y="3.5" width="17" height="17" rx="4.5" stroke-dasharray="3 2.4"/>'),
  pauseCircle: svg('<circle cx="12" cy="12" r="8.5"/><path d="M10 9v6M14 9v6"/>'),
};
