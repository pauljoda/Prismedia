# Shared component bases

This directory contains source copied with shadcn-svelte CLI 1.6.0 from the official registry, then adapted to Prismedia. The initial set is Select, Switch, and Select's Separator dependency. Bits UI provides their interaction behavior. The lockfile records the tested runtime version.

Application code imports the existing `Select` and `Toggle` adapters from `@prismedia/ui-svelte`. Keep registry components internal until a new composition requires a deliberate public API. Domain fetching, settings persistence, entity relationships, and validation belong outside this directory.

## Updating a base

From the repository root, the selective installation command is:

```sh
pnpm dlx shadcn-svelte@1.6.0 add select switch --cwd packages/ui-svelte --no-deps-install
```

Do not accept an overwrite without comparing the incoming source with the adaptations below. Do not run `init` against the application stylesheet or install all components. `components.json` points to the app's existing Tailwind stylesheet; new semantic token aliases map to Prismedia's palette and radius scale instead of replacing them.

Review generated dependency declarations before installing. Runtime dependencies belong in this package. Use relative imports within the shared package; generated app-style aliases and `.js` suffixes do not necessarily match workspace exports.

## Intentional adaptations

- Select retains the existing option/value/callback API, three sizes, error variant, disabled options, and status annotations. Choosing the current item does not clear the value or call `onchange` again. Empty values remain valid options.
- Select forwards item labels to Bits UI for typeahead and exposes disabled options through `aria-disabled`. The trigger is a native button with a listbox popup, not an editable combobox.
- Portal placement uses the nearest native dialog when present. Body portals cannot escape a native modal's inert boundary. Keep the existing Dialog implementation until its dismissal and busy-state contracts are explicitly migrated.
- Menu surfaces are opaque, collision-aware, bounded in height and viewport width, and allow long labels to wrap. Sizes, borders, focus rings, and selected states use existing neutral tokens.
- Toggle is a controlled adapter over Switch. Function binding preserves the caller as the state owner, so a rejected or pending save cannot leave an optimistic switch value behind.
- Switch styles target Bits UI's actual `data-state="checked"` / `unchecked` attributes. Reduced-motion preferences suppress the thumb transition. The small visual control has a larger hit area; consumers still own row layout and non-overlapping targets.
- Settings rows use one labeled control rather than nesting a switch button inside a row button.

## Checks

Run both package typechecks, the web unit suite, and the static build. Keep adapter tests for keyboard navigation, annotations, disabled state, typeahead, empty values, external updates, controlled state, and dialog portal placement. Existing consumer tests exercise import mapping, collection rules, download protocol preference, and request monitoring.

Also inspect `/design-language#component-bases` and a real Settings section through the .NET app. Test pointer selection, keyboard selection, scrolling, focus return, layered Escape, and a narrow viewport. DOM tests do not establish native top-layer behavior, touch behavior, or visual quality.

Sources: [installation](https://www.shadcn-svelte.com/docs/installation), [Select](https://www.shadcn-svelte.com/docs/components/select), [Switch](https://www.shadcn-svelte.com/docs/components/switch), [theming](https://www.shadcn-svelte.com/docs/theming). Upstream attribution is retained in `LICENSE.md`.
