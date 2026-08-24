# CalmMouse design system

Five layers, loaded in this order by `../style.css`. Later layers may use earlier
ones; never the reverse.

| Layer | File | Owns |
| --- | --- | --- |
| Foundations | `tokens.css` | Every raw value in the site |
| Base | `base.css` | Reset, element defaults, focus rings |
| Components | `components.css` | Reusable pieces that style their own insides |
| Patterns | `patterns.css` | Page compositions and the space between pieces |
| Utilities | `utilities.css` | Single-purpose helpers, global motion rules |

## The one rule

**Literals live in `tokens.css` and nowhere else.** If a component needs a value
that isn't a token, add the token first. That's what keeps light and dark mode in
sync: the dark theme remaps semantic roles in one place, and nothing downstream
has to know which theme is active.

## Foundations

- **Colour** is two-tier. `--palette-*` is the raw ramp; `--color-*` maps it to
  roles (`--color-bg`, `--color-text-secondary`, `--color-accent`). Components
  reference roles only. The mouse illustration has its own `--color-shell-*`
  scale because glass isn't a UI surface.
- **Type** has static steps (`--text-xs` … `--text-2xl`) for UI text and fluid
  steps (`--text-lede`, `--text-display-lg`) for editorial text that scales with
  the viewport.
- **Space** is a 4px scale (`--space-1` = 4px … `--space-16` = 64px), plus fluid
  rhythm tokens (`--space-section`, `--space-block`) for vertical cadence.
- Also: `--radius-*`, `--shadow-*`, motion (`--ease-out-expo`, `--duration-*`),
  and layout (`--container-width`, `--measure-*`).

## Naming

Loose BEM: `.block`, `.block__element`, `.block--modifier`. State classes are
`.is-*` (`.is-visible`).

## Components

`.btn` (`--primary`, `--sm`, `--lg`), `.link-arrow`, `.link-quiet`, `.eyebrow`,
`.lede`, `.fineprint`, `.card` (`__icon`, `__title`, `__body`, `--wide`),
`.tile`, `.step`, `.switch` (`--on`), `.window`.

Components carry no outer margin and make no assumption about where they sit —
that's what makes them reusable. Placement belongs to the pattern.

## Patterns

`.container`, `.actions`, `.site-nav`, `.hero`, `.section` (`--alt`, `--center`),
`.compare`, `.bento`, `.figure-center`, `.tile-grid`, `.steps`, `.site-footer`.

## Adding something

1. Can existing components express it? Compose them.
2. Needs a new value? Add a token, then use it.
3. Reusable in more than one place? Component. Only ever one place? Pattern.
