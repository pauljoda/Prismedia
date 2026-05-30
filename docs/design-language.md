# Design Language

## Direction

Prismedia uses the **Prism Noir Luxe** visual system.

The aesthetic is a refined industrial control room — Blackmagic DaVinci Resolve meets high-end audio rack gear. Every surface feels like a machined panel in a private console room. Content commands full attention; controls recede until needed. Color appears only when it carries meaning.

## Core Principles

- **Controlled radii.** Tight, consistent radii from a unified scale (`XS 4px` through `2XL 24px`). Sharp default corners and bubbly pills are both off-language.
- **Material base, shell glass.** Base surfaces, cards, chips, badges, list items, and moving components are solid dark material layers. Glass (`backdrop-filter` + translucent fill) is reserved for shell-level overlays, high-level chrome, and static asset treatments.
- **Color as signal.** Accent colors appear only on active, selected, or critical states. When they appear, they glow.
- **Gradient fills for depth.** Subtle linear gradients distinguish surface planes. No flat solid fills on containers.
- **Mobile first, desktop first-class.** Layout, touch targets, and navigation begin from a mobile interaction model, then expand deliberately for desktop. Neither platform is a degraded version of the other.
- **Glow and animation for state.** Selection, focus, and activity are expressed through luminous glow (`box-shadow` blur spread) and purposeful animation — not static color change alone.
- **Density over emptiness.** Information-dense layouts are the target. Empty space is a deliberate choice, not a default.

---

## Palette

### Surface Hierarchy — Material Layers

Base surfaces are solid, near-black. Subtle linear gradients move from top-left (lighter) to bottom-right (darker) to establish surface weight. These are the material ground layer beneath any glass content.

| Token       | Hex        | Name      | Usage                                   |
|-------------|------------|-----------|-----------------------------------------|
| `bg`        | `#07080b`  | Noir      | Page root, outermost background         |
| `surface-1` | `#0b0e12`  | Obsidian  | Sidebar, inset wells, recessed areas    |
| `surface-2` | `#11161d`  | Graphite  | Panels and primary containers           |
| `surface-3` | `#202734`  | Slate Glass | Elevated panels, drawers              |
| `surface-4` | `#2a3038`  | Carbon    | Tooltips, dropdowns, contextual overlays|

### Glass Layers — Shell Surfaces

Glass layers sit above material surfaces. They require `backdrop-filter: blur()` and a semi-transparent fill, so they are expensive when repeated or animated. Use glass only when content can move behind shell-level or high-level chrome: app shell rails, sticky toolbars, command palettes, menus, modals, sheets, player chrome, and lightbox controls. Static asset treatments may blur the asset itself, such as hero reflections or poster backdrops.

Do not use blur or glass recipes on cards, chips, badges, grid thumbnails, list rows, progress meters, or other repeated/moving components. Those surfaces should use material gradients, borders, glow, and shadow.

| Token     | Fill                          | Blur  | Usage                                  |
|-----------|-------------------------------|-------|----------------------------------------|
| `glass-1` | `rgba(17, 22, 29, 0.72)`     | `12px`| Toolbars, player chrome, lightbox bars |
| `glass-2` | `rgba(17, 22, 29, 0.80)`     | `16px`| Command palette, menus, floating shell panels |
| `glass-3` | `rgba(17, 22, 29, 0.92)`     | `24px`| Sheets, drawers, full-screen overlays  |

### Accent — Brass Scale

Brass is the primary accent register. It is warm, operational, and rare. The primary accent (`accent-500`) is the golden brass used for active/selected labels and glow layers. The deeper amber (`accent-600`) anchors gradients and solid fills. Never use accent for decoration.

| Token        | Hex        |
|--------------|------------|
| `accent-950` | `#1a1408`  |
| `accent-900` | `#2d2210`  |
| `accent-800` | `#4a3818`  |
| `accent-700` | `#7a5e20`  |
| `accent-600` | `#d59a2a`  |
| `accent-500` | `#f2c26a`  |
| `accent-400` | `#f5d48a`  |
| `accent-300` | `#f7dfa0`  |
| `accent-200` | `#faecc0`  |
| `accent-100` | `#fdf5e0`  |
| `accent-50`  | `#fefaf0`  |

**Accent gradients** — use for fills and decorative rules, never as solid points:

| Name               | Value                                                         |
|--------------------|---------------------------------------------------------------|
| Selection gradient | `linear-gradient(135deg, #d59a2a 0%, #f2c26a 100%)`          |
| Active gradient    | `linear-gradient(135deg, #7a5e20 0%, #d59a2a 100%)`          |
| Subtle gradient    | `linear-gradient(180deg, rgba(242,194,106,0.12) 0%, rgba(242,194,106,0) 100%)` |

**Glow values** — `box-shadow` for selected and focused states:

| Name              | Value                                                                                    |
|-------------------|------------------------------------------------------------------------------------------|
| Subtle glow       | `0 0 25px rgba(242,194,106,0.10), 0 0 8px rgba(242,194,106,0.16)`                      |
| Strong glow       | `0 0 30px rgba(242,194,106,0.18), 0 0 10px rgba(242,194,106,0.25)`                     |
| Phosphor glow     | `0 0 20px rgba(255,255,255,0.10), 0 0 6px rgba(255,255,255,0.15)`                      |

### Phosphor — Digital Accent

A silver/platinum register for digital signals, secondary indicators, and cool-tone highlights.

| Token           | Hex        |
|-----------------|------------|
| `phosphor-400`  | `#ffffff`  |
| `phosphor-500`  | `#e2e8f0`  |
| `phosphor-600`  | `#94a3b8`  |

### Text

| Token              | Hex        | Usage                              |
|--------------------|------------|------------------------------------|
| `text-primary`     | `#f0ede3`  | Headings, primary labels           |
| `text-secondary`   | `#c8ccd4`  | Body text, descriptions            |
| `text-muted`       | `#a4acb9`  | Metadata, secondary labels         |
| `text-disabled`    | `#5a6070`  | Disabled controls, placeholder     |
| `text-accent`      | `#f2c26a`  | Active labels, accent text         |
| `text-accent-bright` | `#f5d48a` | Bright accent hover/emphasis     |

### Status Colors — Signal Indicators

Vivid, confident signal colors. Like instrument readouts — clear and immediate, not pastel or muted.

| State   | Default    | Muted (bg) | Text       |
|---------|------------|------------|------------|
| Success | `#63c889`  | `#1a3d28`  | `#8ee0aa`  |
| Warning | `#f2c26a`  | `#3d3010`  | `#f5d48a`  |
| Error   | `#ff806f`  | `#4a1c18`  | `#ff9f92`  |
| Info    | `#6fa8dc`  | `#1a2e44`  | `#92c0e8`  |

### Borders

| Token                  | Value                              |
|------------------------|------------------------------------|
| `border-subtle`        | `rgba(164, 172, 185, 0.07)`        |
| `border-default`       | `rgba(164, 172, 185, 0.12)`        |
| `border-accent`        | `rgba(242, 194, 106, 0.24)`        |
| `border-accent-strong` | `rgba(242, 194, 106, 0.52)`        |

---

## Typography

Four font voices loaded via `@fontsource` packages:

| Voice   | Font           | Usage                                          |
|---------|----------------|------------------------------------------------|
| Display | Cinzel         | Brand display text, cinematic headings         |
| Heading | Geist Sans     | Product headings, section headers, item titles |
| Body    | Inter Variable | Body copy, descriptions, form labels           |
| Utility | JetBrains Mono | Metadata, timestamps, file paths, durations, counters |

**Base size:** 15px. Labels use uppercase + wide tracking for kicker treatment.

**Type scale:**

| Token      | Size     | Weight | Line-height | Letter-spacing |
|------------|----------|--------|-------------|----------------|
| `display`  | 2.5rem   | 700    | 0.92        | −0.04em        |
| `h1`       | 1.75rem  | 600    | 0.95        | −0.03em        |
| `h2`       | 1.25rem  | 600    | 1.05        | −0.02em        |
| `h3`       | 1.05rem  | 600    | 1.15        | −0.015em       |
| `h4`       | 0.875rem | 600    | 1.2         | −0.01em        |
| `body-lg`  | 1rem     | 400    | 1.6         | 0              |
| `body`     | 0.875rem | 400    | 1.55        | 0              |
| `body-sm`  | 0.8rem   | 400    | 1.5         | 0              |
| `label`    | 0.75rem  | 500    | 1.3         | +0.04em        |
| `kicker`   | 0.68rem  | 600    | 1.3         | +0.15em        |
| `mono`     | 0.8rem   | 400    | 1.45        | 0              |
| `mono-sm`  | 0.72rem  | 400    | 1.4         | 0              |

**Text glow effects** — luminous text for active states:
- `text-glow-accent`: brass text with glow text-shadow
- `text-glow-phosphor`: silver/white text with phosphor glow

---

## Surface Recipes

### Material Panel (primary container)

```css
background: linear-gradient(160deg, var(--color-surface-2) 0%, var(--color-surface-1) 100%);
border: 1px solid var(--border-subtle);
border-radius: var(--radius-md);
box-shadow: var(--shadow-panel);
```

The machined bevel (inset top/left highlights) reads as a lit panel edge — a material signal without fake 3D.

### Material Card (cards, chips, rows)

```css
background: linear-gradient(180deg, rgba(255,255,255,0.035), rgba(255,255,255,0)),
            var(--color-surface-2);
border: 1px solid var(--border-default);
border-radius: var(--radius-sm);
box-shadow: var(--shadow-panel);
transition: border-color 180ms var(--ease-mechanical),
            box-shadow   180ms var(--ease-mechanical);

/* Hover */
border-color: var(--border-accent);
box-shadow: var(--shadow-panel-hover);

/* Selected / Active */
border-color: var(--border-accent-strong);
box-shadow: var(--shadow-glow-accent-strong);
```

### Glass Panel (shell surface — command palette, menu, drawer)

```css
background: var(--color-overlay-glass);
backdrop-filter: blur(16px);
-webkit-backdrop-filter: blur(16px);
border: 1px solid var(--border-default);
border-radius: var(--radius-md);
box-shadow: var(--shadow-elevated);
```

### Well (inset container — inputs, metadata blocks, code)

```css
background: var(--color-surface-1);
border: 1px solid var(--border-subtle);
border-radius: var(--radius-sm);
box-shadow: var(--shadow-well);
```

---

## Geometry

### Border Radius

Controlled radii from a unified scale. Subtle softening that maintains the industrial precision feel without harsh zero-radius edges.

| Token        | Value    | Usage                                    |
|--------------|----------|------------------------------------------|
| `radius-xs`  | `4px`    | Small chips, badges, inline elements     |
| `radius-sm`  | `6px`    | Items, inputs, buttons                   |
| `radius-md`  | `10px`   | Panels, modals, primary containers       |
| `radius-lg`  | `14px`   | Large panels and drawers                 |
| `radius-xl`  | `18px`   | Hero sections, feature panels            |
| `radius-2xl` | `24px`   | Full-bleed containers                    |

### Spacing Grid

4px base unit. Prefer multiples of 4. Density targets:

| Context                       | Padding       |
|-------------------------------|---------------|
| Dense row (list item, table)  | 8px 12px      |
| Card inner padding            | 12px 16px     |
| Section padding (mobile)      | 16px          |
| Section padding (desktop)     | 24px 32px     |
| Modal / sheet padding         | 24px          |
| Touch target minimum          | 44×44px       |

---

## Responsive Layout

**Mobile first** — all components are designed and built starting from mobile layout and interaction. Desktop is an expansion, not a reduction.

| Breakpoint | Token | Range        |
|-----------|-------|--------------|
| Mobile    | `sm`  | < 640px      |
| Tablet    | `md`  | 640–1024px   |
| Desktop   | `lg`  | 1024–1440px  |
| Wide      | `xl`  | > 1440px     |

**Mobile requirements:**

- Touch targets minimum 44×44px.
- No hover-only primary actions. Every action reachable by tap.
- Navigation accessible from bottom (thumb zone) or persistent top bar.
- Grid views: 2 columns at most on mobile, 1 column below 400px.
- Sheets and drawers animate in from the bottom edge.

**Desktop expansions:**

- Sidebar navigation, wider grids (4–6 columns), panel-split layouts.
- Hover states augment but never replace primary touch affordances.
- Additional density via tighter gutters and visible metadata rows.

---

## Motion & Animation

Motion is precise and deliberate — like instrumentation in a darkened control room. No bounce, no spring physics, no decorative easing.

### Easing Curves

| Token              | Value                          | Usage                               |
|--------------------|--------------------------------|-------------------------------------|
| `ease-default`     | `cubic-bezier(0.4, 0, 0.2, 1)`| Standard property transitions       |
| `ease-enter`       | `cubic-bezier(0, 0, 0.2, 1)`  | Elements entering the viewport      |
| `ease-exit`        | `cubic-bezier(0.4, 0, 1, 1)`  | Elements leaving the viewport       |
| `ease-mechanical`  | `cubic-bezier(0.25, 0, 0.25, 1)`| Panel slides, sidebar collapse    |

### Durations

| Token              | Value   |
|--------------------|---------|
| `duration-fast`    | `100ms` |
| `duration-normal`  | `180ms` |
| `duration-moderate`| `250ms` |
| `duration-slow`    | `400ms` |

### Keyframe Animations

**Glow pulse** — selected items, active indicators, items in processing state:

```css
@keyframes glow-pulse {
  0%, 100% {
    box-shadow: 0 0 0 1px rgba(242,194,106,0.50),
                0 0 10px rgba(242,194,106,0.20);
  }
  50% {
    box-shadow: 0 0 0 1px rgba(242,194,106,0.80),
                0 0 20px rgba(242,194,106,0.40),
                0 0 40px rgba(242,194,106,0.15);
  }
}
/* Duration: 2.4s ease-in-out infinite */
```

**Fade in** — panel and overlay entrance:

```css
@keyframes fade-in {
  from { opacity: 0; transform: scale(0.97); }
  to   { opacity: 1; transform: scale(1); }
}
/* Duration: 250ms ease-enter */
```

**Slide up** — mobile sheet and bottom-drawer entrance:

```css
@keyframes slide-up {
  from { transform: translateY(100%); }
  to   { transform: translateY(0); }
}
/* Duration: 280ms ease-mechanical */
```

**LED pulse** — status indicator in processing/loading state:

```css
@keyframes led-pulse {
  0%, 100% { opacity: 1; }
  50%       { opacity: 0.35; }
}
/* Duration: 1.6s ease-in-out infinite */
```

---

## Recurring Motifs

### LED Status Indicators

Soft signal indicators with controlled radius (`radius-xs`). Glow via `box-shadow` using status colors. Colors: success green (active), warning amber (queued/warning), error red (failed), info blue (paused), brass (highlighted), phosphor (digital signal). `led-pulse` animation for processing/loading state.

### Selection State

Selected items express state through three layered signals:

1. `border-color` → `border-accent-strong`
2. Full glow `box-shadow`
3. Optional `glow-pulse` animation for persistent or "currently playing" states

### Accent Meter / Progress Bar

Height: 3px. Controlled radius. Fill: accent selection gradient (`accent-700` → `accent-500` → `accent-400`). Used for job progress, disk usage, buffer position, and queue health. Phosphor variant available for digital/system meters.

### Separator

`1px solid var(--border-subtle)`. Clean horizontal rule with subtle glow only when it communicates state. Vertical separators use the same token.

---

## Anti-Patterns

- **No default shadcn appearance** shipped without token and composition overrides.
- **No purple-gradient startup aesthetic.**
- **No flat solid fills** on major containers — use material gradients by default, and glass only for shell-level overlays or static asset treatments.
- **No bright neon status colors** — vivid but controlled signal palette only.
- **No hover-only primary actions.** Everything reachable by tap on mobile.
- **No decorative glow** — glow appears only on selected, active, or processing states. Never for style alone.
- **No bounce or spring easing.** All motion uses mechanical or standard bezier curves.
- **No oversized empty panels on mobile** — density is maintained across breakpoints.
- **No bubbly or pill-shaped containers** — radii stay tight and controlled from the scale.
