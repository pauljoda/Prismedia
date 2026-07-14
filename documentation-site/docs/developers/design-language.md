---
sidebar_position: 6
title: Design Language
description: The prism visual system used across Prismedia.
---

# Design Language

Prismedia uses a literal prism as its visual model: one place for a whole media collection, expressed as white light entering a prism and separating into the spectrum. The app remains dark and content-led; neutral silver represents Prismedia itself, while color identifies each kind of media.

## Principles

- **Black canvas, restrained atmosphere.** True black stays dominant behind a soft, blurred spectrum.
- **Entity color has meaning.** Video is red/orange, movies orange/yellow, series yellow/green, galleries green/cyan, books cyan/blue, images blue/violet, audio violet/magenta, and collections magenta/red.
- **Artwork leads on details.** Real artwork can supply a dark background plus primary and secondary accents. Poster art may shape atmosphere without being promoted into a hero; only an explicit backdrop is a hero.
- **Glass is chrome.** Navigation, sticky toolbars, menus, sheets, dialogs, and floating controls may be frosted. Cards, grids, rows, and content panels stay opaque.
- **Motion explains state.** Glow and motion reinforce selection and loading, with visible non-color cues and reduced-motion alternatives.
- **Shared components first.** Tokens and shared building blocks own the design language.

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

## Loading

Blocking states use the responsive prism animation: white light enters from outside the component, hits the neutral mark, and fans into seven bands that expand across the available width and height. The cycle lasts 2.8 seconds. Reduced-motion mode displays a static colored prism and spectrum. Small retained-content operations continue to use compact progress indicators.

## Implementation rules

1. Use entity-family colors on browse and navigation surfaces.
2. Sample an already-loaded image for detail palettes; do not fetch artwork a second time.
3. Keep body text neutral and readable over artwork.
4. Only apply backdrop blur to chrome or floating functional layers.
5. Use the shared radii and controls; avoid generic component-library defaults and decorative pills.
6. Verify mobile, desktop, keyboard focus, contrast, and reduced motion before shipping.
