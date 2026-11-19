# UI design rules

Standing preferences for how the site looks and feels. Follow these when adding or changing UI.

## Transitions and animation

- Always smooth and clean. Nothing bulky, rigid, or jarring.
- Use short, eased transitions (roughly 200-300ms, `ease-out`) on interactive state changes: hover, focus, open/close, route changes.
- Prefer animating `opacity` and `transform` (and grid-template-rows for expanding panels) over properties that cause layout jumps.
- No abrupt show/hide, bouncing, or long or heavy motion. Subtle beats showy.
- Respect `prefers-reduced-motion`: motion should degrade to simple fades or none.

## Icons

- Always smooth and clean: simple line icons with rounded caps and joins, consistent stroke width (the nav uses 1.5) and a shared size.
- Inline SVG using `currentColor` so icons follow the theme tokens. No raw colors.
- No bulky, filled, heavy, or mixed-style icon sets.

## Mobile first

- Mobile use is extremely important, potentially prioritized over desktop. Most guests will open the site on a phone.
- Design and verify at phone widths first, then scale up. Never ship a change that works on desktop but is cramped or broken on mobile.
- Tap targets at least 44x44px, comfortable spacing, readable text sizes, no horizontal scroll.
- Don't rely on hover for anything essential; hover is an enhancement only.

## Cursor

- On desktop (devices with a mouse), every clickable control (buttons, links, summaries, selects, checkboxes, radios) shows `cursor: pointer`.
- This is handled globally in `src/index.css` inside `@media (hover: hover) and (pointer: fine)`. New custom clickable elements that aren't a native button or link need `role="button"` or an explicit `cursor-pointer` inside that same media query.
