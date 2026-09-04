# Shared component bases

This directory contains source copied with shadcn-svelte CLI 1.6.0 from the official registry, then adapted to Prismedia. The installed set is Select, Switch, Separator, DropdownMenu, and Popover. Bits UI provides their interaction behavior. The lockfile records the tested runtime version.

Application code imports `Select`, `Toggle`, `DropdownMenu`, `Popover`, and `Separator` from `@prismedia/ui-svelte`. Select and Toggle retain their existing adapter APIs; DropdownMenu and Popover expose namespaced composition parts. Domain fetching, settings persistence, entity relationships, and validation belong outside this directory.

## Composing overlays

- Use DropdownMenu for a list of commands or navigation destinations. Put items in a Group, use `onSelect` for commands, and use the Item's `child` snippet to spread its props onto a real anchor for links. Do not nest a button or link inside the default menu item.
- Use Popover for contextual forms and mixed controls, such as filter presets. Give Content an accessible name with `aria-labelledby` and a Title. Use shared Button and TextInput controls within it. TextInput exposes a bindable native `ref` for deliberate form focus.
- Let the base own positioning, outside dismissal, Escape, and keyboard behavior. Consumers should not add scrims, window listeners, coordinate state, or `keepFlyoutOnScreen` to these components.
- Content portals to the body by default. When composing inside a native dialog or fullscreen host, explicitly pass `portalProps={{ to: hostElement }}`. Validate the actual top-layer host before migrating those consumers; the current menu consumers are outside those hosts.
- Keep form state intact while an overlay closes so its focused element remains available to the focus manager. Reset an unfinished form when reopening it.
- Only the used DropdownMenu parts are retained. Add submenu or checkbox/radio parts selectively from the registry when a concrete consumer needs them.

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
- DropdownMenu and Popover use bounded, opaque neutral surfaces and existing radius, typography, border, and shadow tokens. Menu items have visible focus/hover states, comfortable targets, disabled handling, and an optional destructive variant.
- Bits UI 2.19.0 drops the rendered content ID through its floating layer. DropdownMenu and Popover Content explicitly forward a stable ID through the documented child snippet, preserving `aria-controls` and menu typeahead. Keep the regression test when comparing a future upstream update; do not replace this with custom keyboard handlers.
- Popover Content explicitly carries `role="dialog"`, matching its trigger's popup semantics. Consumers supply its title and description.

## Checks

Run both package typechecks, the web unit suite, and the static build. Keep adapter tests for keyboard navigation, annotations, disabled state, typeahead, empty values, external updates, controlled state, and dialog portal placement. Menu consumers cover account permissions, real links, callback isolation, and breadcrumb portals. Preset tests cover naming, cancellation, focus return, applying, deleting, and explicit overwrite confirmation. Existing consumer tests exercise import mapping, collection rules, download protocol preference, and request monitoring.

Also inspect `/design-language#component-bases` and a real Settings section through the .NET app. Test pointer selection, keyboard selection, scrolling, focus return, layered Escape, and a narrow viewport. DOM tests do not establish native top-layer behavior, touch behavior, or visual quality.

Sources: [installation](https://www.shadcn-svelte.com/docs/installation), [Select](https://www.shadcn-svelte.com/docs/components/select), [Switch](https://www.shadcn-svelte.com/docs/components/switch), [DropdownMenu](https://www.shadcn-svelte.com/docs/components/dropdown-menu), [Popover](https://www.shadcn-svelte.com/docs/components/popover), [theming](https://www.shadcn-svelte.com/docs/theming). Upstream attribution is retained in `LICENSE.md`.
