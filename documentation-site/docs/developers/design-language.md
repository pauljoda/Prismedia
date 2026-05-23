---
sidebar_position: 2
title: Design Language
description: Dark Room visual direction for Prismedia.
---

# Design Language

Prismedia follows the Dark Room direction: a sharp, cinematic interface for managing a private media library.

## Rules

- **Sharp corners everywhere.** Use `border-radius: 0`.
- **Material base plus glass overlay.** Solid dark surfaces carry the layout; translucent glass is reserved for floating or interactive elements.
- **Brass means active.** Use `#c49a5a` for selected, active, focused, or current state. Express it with glow, not flat color alone.
- **Mobile first.** Design the phone layout first; desktop expands it.
- **Typography has three voices.** Geist for headings, Inter for body, JetBrains Mono for metadata.
- **Core actions cannot depend on hover.** Hover can enrich the UI, but touch users must still have the action.

## Surface hierarchy

Use five material levels:

| Level | Purpose |
| --- | --- |
| `bg` | Page background. |
| `surface-1` | Primary panels and page sections. |
| `surface-2` | Secondary panels and grouped controls. |
| `surface-3` | Raised controls and selected rows. |
| `surface-4` | Highest non-modal material surface. |

Glass layers are named `glass-1`, `glass-2`, and `glass-3`.

## UI change checklist

Before shipping UI changes:

1. Check mobile first.
2. Check that all corners are sharp.
3. Check that selected/focused state has glow or motion, not color alone.
4. Check that the page does not look like generic SaaS defaults.
5. Check that controls are reachable without hover.
