---
sidebar_position: 6
title: Design Language
description: The prism visual system used across Prismedia.
---

# Design Language

Prismedia uses a literal prism as its visual model: one place for a whole media collection, expressed as white light entering a prism and separating into the spectrum. The app remains dark and content-led; neutral silver represents Prismedia itself, while small, deliberate color cues identify each kind of media.

## Principles

- **Black canvas, restrained atmosphere.** True black and neutral surfaces dominate. Spectrum atmosphere appears on artwork-reactive detail pages and literal prism moments, not across the global shell.
- **Entity color has meaning.** Video is red/orange, movies orange/yellow, series yellow/green, galleries green/cyan, books cyan/blue, images blue/violet, audio violet/magenta, and collections magenta/red.
- **Artwork leads on details.** Real artwork can supply a dark background plus primary and secondary accents. Poster art may shape atmosphere without being promoted into a hero; only an explicit backdrop is a hero.
- **Glass is chrome.** Navigation, sticky toolbars, menus, sheets, dialogs, and floating controls may be frosted. Cards, grids, rows, and content panels stay opaque.
- **Motion explains state.** Motion reinforces selection and loading, with visible non-color cues and reduced-motion alternatives. Persistent controls do not glow.
- **Shared components first.** Tokens and shared building blocks own the design language.

## Brand marks

The colored prism is the default app, favicon, and documentation mark. The neutral prism belongs to the responsive loading sequence, where white light enters the mark and separates into the spectrum. The red-tinted prism is reserved for the NSFW visibility control and its active state; it is not a general brand or error mark.

## Spectrum

| Token | Value |
| --- | --- |
| Neutral | `#c7c9cc` |
| Red | `#ff141f` |
| Orange | `#ff570a` |
| Yellow | `#ffc71f` |
| Green | `#1fc247` |
| Cyan | `#0ab3e6` |
| Blue | `#0d47ff` |
| Violet | `#7a14f5` |
| Magenta | `#d60de0` |

Use these exact tokens for literal prism light. Persistent entity chrome uses the corresponding muted `materialSpectrum` tokens so color reads like flat paint. Derive transparent borders and material fills with opacity or `color-mix()` rather than inventing similar colors. Reserve emitted-light effects for the prism loading animation, not persistent entity chrome.

| Material token | Value |
| --- | --- |
| Red | `#b3484d` |
| Orange | `#b76337` |
| Yellow | `#9e873b` |
| Green | `#4d925d` |
| Cyan | `#3b869c` |
| Blue | `#536fb0` |
| Violet | `#775ca5` |
| Magenta | `#9a4f9d` |

The everyday app should read as roughly 90–95% neutral. Spend entity color on short section markers, thin progress and active rails, selected controls with a second non-color cue, and artwork-derived detail atmosphere. Do not repeat the same color across a label, icon, card border, button, background gradient, and shadow.

Every page gets one muted accent moment. Library pages use the top edge of the grid toolbar, Settings uses section icons, and detail pages use artwork atmosphere plus the active-tab rail. Prefer one clear location over several competing accents.

## Loading

Blocking states use the responsive prism animation: white light enters from outside the component, hits the neutral mark, and fans into seven bands that expand across the available width and height. The cycle lasts 2.8 seconds. Reduced-motion mode displays a static colored prism and spectrum. Small retained-content operations continue to use compact progress indicators.

## Implementation rules

1. Use entity-family colors as sparse markers and states on browse and navigation surfaces; keep surrounding text, icons, cards, and controls neutral.
2. Sample an already-loaded image for detail palettes; do not fetch artwork a second time.
3. Keep body text neutral and readable over artwork.
4. Only apply backdrop blur to chrome or floating functional layers.
5. Use the shared radii and controls; avoid generic component-library defaults and decorative pills.
6. Verify mobile, desktop, keyboard focus, contrast, and reduced motion before shipping.
