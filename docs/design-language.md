# Design Language

## Direction

Prismedia uses a **Dark Room** visual system.

The aesthetic is a precision monitoring environment: a darkened screening room where content commands full attention and controls recede until needed. Think color-grading suites, broadcast control rooms, and professional video hardware — zero ambient light, sharp instrument panels, and color used only when it carries meaning.

## Core Principles

- **Sharp geometry.** All containers use `border-radius: 0`. No rounded corners anywhere in the UI.
- **Material base, glass overlay.** Base surfaces are solid dark material layers. Floating and interactive elements use glass (backdrop-blur + semi-transparent fill) layered above them.
- **Color as signal.** Accent colors appear only on active, selected, or critical states. When they appear, they glow.
- **Gradient fills for depth.** Subtle linear gradients distinguish surface planes. No flat solid fills on containers.
- **Mobile first, desktop first-class.** Layout, touch targets, and navigation begin from a mobile interaction model, then expand deliberately for desktop. Neither platform is a degraded version of the other.
- **Glow and animation for state.** Selection, focus, and activity are expressed through luminous glow (`box-shadow` blur spread) and purposeful animation — not static color change alone.
- **Density over emptiness.** Information-dense layouts are the target. Empty space is a deliberate choice, not a default.

---

## Palette

### Surface Hierarchy — Material Layers

Base surfaces are solid, near-black. Subtle linear gradients move from top-left (lighter) to bottom-right (darker) to establish surface weight. These are the material ground layer beneath any glass content.

| Token       | Hex        | Usage                                   |
|-------------|------------|-----------------------------------------|
| `bg`        | `#07080b`  | Page root, outermost background         |
| `surface-1` | `#0c0f15`  | Sidebar, inset wells, recessed areas    |
| `surface-2` | `#101420`  | Cards, panels, primary containers       |
| `surface-3` | `#151a28`  | Elevated panels, drawers                |
| `surface-4` | `#1c2235`  | Tooltips, dropdowns, contextual overlays|

### Glass Layers — Floating Surfaces

Glass layers sit above material surfaces. They require `backdrop-filter: blur()` and a semi-transparent fill. Use glass for anything that floats above the base content layer: interactive cards, modals, command palettes, sheets.

| Token     | Fill                          | Blur  | Usage                                  |
|-----------|-------------------------------|-------|----------------------------------------|
| `glass-1` | `rgba(12, 15, 21, 0.72)`      | `12px`| Media cards, interactive list items    |
| `glass-2` | `rgba(16, 20, 32, 0.82)`      | `16px`| Command palette, floating panels       |
| `glass-3` | `rgba(21, 26, 40, 0.92)`      | `24px`| Sheets, drawers, full-screen overlays  |

### Accent — Brass Scale

Brass is the primary accent register. It is warm, operational, and rare. Use `accent-500` as the base; use `accent-400` / `accent-300` as the luminous glow-layer color. Never use accent for decoration.

| Token        | Hex        |
|--------------|------------|
| `accent-950` | `#131008`  |
| `accent-900` | `#261f0f`  |
| `accent-800` | `#3d3016`  |
| `accent-700` | `#5a4620`  |
| `accent-600` | `#7a5e2c`  |
| `accent-500` | `#c49a5a`  |
| `accent-400` | `#d4af74`  |
| `accent-300` | `#e0c48e`  |
| `accent-200` | `#ebdaaf`  |
| `accent-100` | `#f5efd5`  |
| `accent-50`  | `#faf6ea`  |

**Accent gradients** — use for fills and decorative rules, never as solid points:

| Name               | Value                                                         |
|--------------------|---------------------------------------------------------------|
| Selection gradient | `linear-gradient(135deg, #c49a5a 0%, #e0c48e 100%)`          |
| Active gradient    | `linear-gradient(135deg, #7a5e2c 0%, #c49a5a 100%)`          |
| Subtle gradient    | `linear-gradient(180deg, rgba(196,154,90,0.12) 0%, rgba(196,154,90,0) 100%)` |

**Glow values** — `box-shadow` for selected and focused states:

| Name         | Value                                                                                             |
|--------------|---------------------------------------------------------------------------------------------------|
| Subtle glow  | `0 0 0 1px rgba(196,154,90,0.35), 0 0 8px rgba(196,154,90,0.15)`                                |
| Full glow    | `0 0 0 1px rgba(196,154,90,0.60), 0 0 16px rgba(196,154,90,0.30), 0 0 32px rgba(196,154,90,0.10)` |

### Text

| Token           | Hex        | Usage                              |
|-----------------|------------|------------------------------------|
| `text-primary`  | `#f2eed8`  | Headings, primary labels           |
| `text-secondary`| `#c4c9d4`  | Body text, descriptions            |
| `text-muted`    | `#8a93a6`  | Metadata, secondary labels         |
| `text-disabled` | `#4a5260`  | Disabled controls, placeholder     |
| `text-accent`   | `#c49a5a`  | Active labels, accent text         |

### Status Colors — Instrument LED Style

Muted, realistic. Like LED indicators in a darkened control room, not bright SaaS badges. Each status has a glow value for animated indicator use.

| State   | Default    | Muted (bg) | Text       | Glow                          |
|---------|------------|------------|------------|-------------------------------|
| Success | `#4e8a62`  | `#2a4a38`  | `#80b898`  | `rgba(78, 138, 98, 0.30)`    |
| Warning | `#b09040`  | `#5c4c20`  | `#ccb060`  | `rgba(176, 144, 64, 0.30)`   |
| Error   | `#a84850`  | `#5a2c30`  | `#cc7880`  | `rgba(168, 72, 80, 0.30)`    |
| Info    | `#4478a8`  | `#283850`  | `#70a4cc`  | `rgba(68, 120, 168, 0.30)`   |

### Borders

| Token                | Value                              |
|----------------------|------------------------------------|
| `border-subtle`      | `rgba(148, 158, 178, 0.07)`        |
| `border-default`     | `rgba(148, 158, 178, 0.13)`        |
| `border-accent`      | `rgba(196, 154, 90, 0.25)`         |
| `border-accent-strong` | `rgba(196, 154, 90, 0.50)`       |
| `border-glow`        | `rgba(196, 154, 90, 0.80)`         |

---

## Typography

Three font voices loaded via local `@fontsource` packages:

| Voice   | Font           | Usage                                          |
|---------|----------------|------------------------------------------------|
| Heading | Geist          | Page titles, section headers, card titles      |
| Body    | Inter          | Body copy, descriptions, form labels           |
| Utility | JetBrains Mono | Metadata, timestamps, file paths, durations, counters |

**Base size:** 14px (dense UI). Labels use uppercase + wide tracking for kicker treatment.

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
| `kicker`   | 0.68rem  | 600    | 1.3         | +0.10em        |
| `mono`     | 0.8rem   | 400    | 1.45        | 0              |
| `mono-sm`  | 0.72rem  | 400    | 1.4         | 0              |

---

## Surface Recipes

### Material Panel (primary container)

```css
background: linear-gradient(160deg, var(--surface-2) 0%, var(--surface-1) 100%);
border: 1px solid var(--border-subtle);
border-radius: 0;
box-shadow: inset 0 1px 0 rgba(255,255,255,0.04), 0 4px 24px rgba(0,0,0,0.40);
```

The inset top shadow reads as a lit panel edge — a material signal without fake 3D.

### Glass Card (media card, interactive list item)

```css
background: rgba(12, 15, 21, 0.72);
backdrop-filter: blur(12px);
-webkit-backdrop-filter: blur(12px);
border: 1px solid var(--border-default);
border-radius: 0;
box-shadow: 0 2px 12px rgba(0,0,0,0.35);
transition: border-color 160ms cubic-bezier(0.25,0,0.25,1),
            box-shadow   160ms cubic-bezier(0.25,0,0.25,1);

/* Hover */
border-color: var(--border-accent);
box-shadow: 0 2px 12px rgba(0,0,0,0.35), 0 0 0 1px rgba(196,154,90,0.20);

/* Selected / Active */
border-color: var(--border-accent-strong);
box-shadow: 0 0 0 1px rgba(196,154,90,0.60),
            0 0 16px rgba(196,154,90,0.30),
            0 0 32px rgba(196,154,90,0.10);
```

### Glass Panel (floating surface — command palette, popover, drawer)

```css
background: rgba(16, 20, 32, 0.82);
backdrop-filter: blur(16px);
-webkit-backdrop-filter: blur(16px);
border: 1px solid var(--border-default);
border-radius: 0;
box-shadow: 0 8px 40px rgba(0,0,0,0.60),
            inset 0 1px 0 rgba(255,255,255,0.05);
```

### Well (inset container — inputs, metadata blocks, code)

```css
background: var(--surface-1);
border: 1px solid var(--border-subtle);
border-radius: 0;
box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
```

---

## Geometry

### Border Radius

`border-radius: 0` on all elements: panels, cards, buttons, inputs, badges, chips, modals, tooltips, dropdowns, popovers, separators, progress bars, avatars, and thumbnails.

Sharp edges are a core identity element. Softening them — even slightly — undermines the visual system.

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

| Token              | Value  |
|--------------------|--------|
| `duration-fast`    | `80ms` |
| `duration-normal`  | `160ms`|
| `duration-moderate`| `240ms`|
| `duration-slow`    | `380ms`|

### Keyframe Animations

**Glow pulse** — selected items, active indicators, items in processing state:

```css
@keyframes glow-pulse {
  0%, 100% {
    box-shadow: 0 0 0 1px rgba(196,154,90,0.50),
                0 0 10px rgba(196,154,90,0.20);
  }
  50% {
    box-shadow: 0 0 0 1px rgba(196,154,90,0.80),
                0 0 20px rgba(196,154,90,0.40),
                0 0 40px rgba(196,154,90,0.15);
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
/* Duration: 240ms ease-enter */
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

8×8px squares (`border-radius: 0`). Glow via `box-shadow` using the status glow values. Colors: success green (active), warning amber (queued/warning), error red (failed), gray (idle), brass (highlighted). `led-pulse` animation for processing/loading state.

### Selection State

Selected items express state through three layered signals:

1. `border-color` → `border-accent-strong`
2. Full glow `box-shadow`
3. Optional `glow-pulse` animation for persistent or "currently playing" states

### Accent Meter / Progress Bar

Height: 3px. No `border-radius`. Fill: accent selection gradient (`accent-500` → `accent-300`). Used for job progress, disk usage, buffer position, and queue health.

### Separator

`1px solid var(--border-subtle)`. No gradient fade. Clean, hard horizontal rule — consistent with sharp geometry. Vertical separators use the same token.

---

## Anti-Patterns

- **No rounded corners.** `border-radius` is forbidden. Sharp geometry is a core identity element.
- **No default shadcn appearance** shipped without token and composition overrides.
- **No purple-gradient startup aesthetic.**
- **No flat solid fills** on major containers — always gradient surface or glass recipe.
- **No bright saturated status colors** — muted LED palette only.
- **No hover-only primary actions.** Everything reachable by tap on mobile.
- **No decorative glow** — glow appears only on selected, active, or processing states. Never for style alone.
- **No bounce or spring easing.** All motion uses mechanical or standard bezier curves.
- **No oversized empty cards on mobile** — density is maintained across breakpoints.
