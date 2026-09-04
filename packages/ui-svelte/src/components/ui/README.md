# Shared component bases

This directory contains source copied with shadcn-svelte CLI 1.6.0 from the official registry, then themed for Prismedia. The foundation includes Button, Input, InputGroup, Textarea, Badge, Card, Field, Label, Tabs, Collapsible, Select, Switch, Separator, DropdownMenu, Popover, ToggleGroup, Slider, and Command, with Toggle supporting the group styles. Bits UI provides behavior for interactive composites; simple controls retain native HTML semantics. The lockfile records the tested runtime version.

Application code imports these bases from `@prismedia/ui-svelte`. Select and Toggle retain their existing adapter APIs; DropdownMenu, Popover, ToggleGroup, and Command expose namespaced composition parts. `SearchableSelect` composes Command and Popover for local single-choice catalogs. Domain fetching, settings persistence, entity relationships, and validation belong outside this directory.

## Building on the foundation

- Button, Badge, TextInput, SearchInput, and Panel keep their established import paths as thin adapters. Button maps legacy `primary`/`danger` and `md` names to the base variants. Badge retains quiet default and semantic status variants. Panel uses Card without imposing new internal spacing on existing consumers.
- Compose new panels from Card. Existing Panel consumers retain their page composition. Settings keeps its list of destinations, colored section icons, and established preference layouts; media artwork grids retain their purpose-built layout.
- Compose fields with Field and Label, and use Input/Textarea controls. App TextField and TextAreaField retain validation and change callbacks while forwarding required, invalid, and description semantics. SearchInput composes InputGroup and keeps clear/focus behavior in one place.
- Use Tabs for panels, not route navigation. EntityDetail uses manual keyboard activation and function binding so the existing dirty-edit guard can reject a tab change. Sidebar uses Collapsible only for section disclosure; the app still owns permissions, ordering, and persisted preferences.
- A component migration is not permission to redesign a page. Preserve deliberate composition and identity choices; obtain page-by-page agreement before changing them.
- Global search composes Command Input, List, Group, and Item with the existing native Dialog. The shared thumbnail still owns artwork; the app owns request debouncing, stale-response invalidation, history, and navigation. Disable Command filtering for server-ranked results and use its public selection API after asynchronous rows mount. Recent and full-result actions belong in the same keyboard list, with removal buttons outside the option itself.
- Prefer component variants over descendant CSS that restyles controls. Theme aliases map Card, secondary, muted, border, and ring colors to the shared neutral palette. Tight radii, existing font voices, and sparse entity identity remain Prismedia's theme; avoid layering old inset shadows and tiny utility typography over every base.

## Composing overlays

- Use DropdownMenu for a list of commands or navigation destinations. Put items in a Group, use `onSelect` for commands, and use the Item's `child` snippet to spread its props onto a real anchor for links. Do not nest a button or link inside the default menu item.
- Use Popover for contextual forms and mixed controls, such as filter presets. Give Content an accessible name with `aria-labelledby` and a Title. Use shared Button and TextInput controls within it. TextInput exposes a bindable native `ref` for deliberate form focus.
- Let the base own positioning, outside dismissal, Escape, and keyboard behavior. Consumers should not add scrims, window listeners, coordinate state, or `keepFlyoutOnScreen` to these components.
- Content portals to the body by default. When composing inside a native dialog or fullscreen host, explicitly pass `portalProps={{ to: hostElement }}`. Validate the actual top-layer host before migrating those consumers; the current menu consumers are outside those hosts.
- Keep form state intact while an overlay closes so its focused element remains available to the focus manager. Reset an unfinished form when reopening it.
- Only the used DropdownMenu parts are retained. Add submenu or checkbox/radio parts selectively from the registry when a concrete consumer needs them.

## Choosing library controls

- Prefer the base component's standard styling through Prismedia's shared theme tokens when it already fits. Preserving a page's layout does not require recreating every legacy control detail; pickers should use the shared Select/SearchableSelect styles instead of route-specific overrides.
- Use Select for short finite lists, such as sorting and page size. Use SearchableSelect when choices need explicit text search, such as Identify providers. The searchable adapter matches labels and values case-insensitively, preserves catalog order, and limits rendered results to 50 by default. Parent code owns the chosen value and any fallback selection.
- Use ToggleGroup for a small set of mutually exclusive layout choices. The library consumer uses function binding to reject an empty value when the active choice is pressed again.
- Use Slider for single numeric values. Supply `thumbLabel` so the focusable control is named, and keep persistence in the consumer. Multi-thumb ranges are intentionally not exposed yet.
- Preserve the library toolbar's established layout when replacing control internals: full-width search and sort, inline view/thumbnail controls with filters and presets opposite, then the selection strip below. Use a named Popover for thumbnail size only on narrow screens; do not move desktop controls into an extra menu.
- Use shared Button variants for row actions, including Clear. Do not rely on styles scoped to a sibling row for icon alignment, spacing, or focus treatment.
- Keep search/filter reset separate from browsing preferences. Sort, artwork layout, and selection must not reveal the filter-reset row; clearing filters preserves those choices.
- BulkSelectionBar uses shared Buttons and DropdownMenu while retaining selection eligibility and callbacks in the app. Pagination uses the same Button and Select adapters; its controller still owns page boundaries, loading, and retry behavior.

## Updating a base

From the repository root, the selective installation command is:

```sh
pnpm dlx shadcn-svelte@1.6.0 add select switch --cwd packages/ui-svelte --no-deps-install
```

Do not accept an overwrite without comparing the incoming source with the adaptations below. Do not run `init` against the application stylesheet or install all components. `components.json` points to the app's existing Tailwind stylesheet; new semantic token aliases map to Prismedia's palette and radius scale instead of replacing them.

Review generated dependency declarations before installing. Runtime dependencies belong in this package. Use relative imports within the shared package; generated app-style aliases and `.js` suffixes do not necessarily match workspace exports.

## Intentional adaptations

- Registry variant recipes use the existing class-variance-authority dependency. No additional variant runtime or forms framework is needed. Simple Button and Badge bases expose native button/span contracts; links remain real anchors, styled with the exported recipes when needed.
- Tabs target Bits UI's `data-state="active"` and explicit orientation attributes. Card uses a real border and allows content overflow so existing overlays are not clipped.
- Global player shortcuts yield to focused controls and already-consumed keyboard events. Test tab navigation beside a real video player; isolated tab tests cannot establish shortcut ownership across the page.
- Settings numeric controls preserve clamping and parent-owned persistence. Disabled integer fields disable their step buttons too; decimal Slider saves through its commit event once, not a second blur handler.
- Select retains the existing option/value/callback API, three sizes, error variant, disabled options, and status annotations. Choosing the current item does not clear the value or call `onchange` again. Empty values remain valid options.
- Select forwards item labels to Bits UI for typeahead and exposes disabled options through `aria-disabled`. The trigger is a native button with a listbox popup, not an editable combobox.
- Portal placement uses the nearest native dialog when present. Body portals cannot escape a native modal's inert boundary. Keep the existing Dialog implementation until its dismissal and busy-state contracts are explicitly migrated.
- Global search uses a separate native modal top-layer entry, so opening search from another dialog and closing it restores that dialog and its focused control. Standard Command styles do not require replacing native modal ownership. Dialog's optional `initialFocus` callback chooses the task field once per opening without stealing subsequent focus or adding caller-owned focus timers.
- Menu surfaces are opaque, collision-aware, bounded in height and viewport width, and allow long labels to wrap. Sizes, borders, focus rings, and selected states use existing neutral tokens.
- Toggle is a controlled adapter over Switch. Function binding preserves the caller as the state owner, so a rejected or pending save cannot leave an optimistic switch value behind.
- Switch styles target Bits UI's actual `data-state="checked"` / `unchecked` attributes. Reduced-motion preferences suppress the thumb transition. The small visual control has a larger hit area; consumers still own row layout and non-overlapping targets.
- Settings rows use one labeled control rather than nesting a switch button inside a row button.
- DropdownMenu and Popover use bounded, opaque neutral surfaces and existing radius, typography, border, and shadow tokens. Menu items have visible focus/hover states, comfortable targets, disabled handling, and an optional destructive variant.
- Bits UI 2.19.0 drops the rendered content ID through its floating layer. DropdownMenu and Popover Content explicitly forward a stable ID through the documented child snippet, preserving `aria-controls` and menu typeahead. Keep the regression test when comparing a future upstream update; do not replace this with custom keyboard handlers.
- Popover Content explicitly carries `role="dialog"`, matching its trigger's popup semantics. Consumers supply its title and description.
- Command Input reuses the existing TextInput style recipe instead of adding a second input system. Its List wraps a Bits Viewport, required by Bits UI 2.19 for active-descendant linkage and list management. Keep the keyboard/ARIA regression tests when updating registry source.
- Command Item's optional `showIndicator` is false for actions and artwork results; selection pickers keep the check indicator. Native action buttons inside the Command root isolate Enter so they do not also activate the selected result.
- SearchableSelect focuses its search field on opening; Popover owns dismissal and focus return. The adapter resets search on reopening, commits only on item selection, and uses the nearest native dialog as its portal host. Its first application consumer is IdentifyProviderSelect; asynchronous entity picking remains a separate domain concern.
- ToggleGroup uses the existing class-variance-authority dependency and neutral selected states. No additional styling runtime is needed.
- Slider and ToggleGroup orientation styles target Bits UI's `data-orientation` attribute explicitly. Registry-only shorthand variants are not assumed to exist in the app stylesheet. Muted control fills reuse the mapped neutral accent token.

## Checks

Run both package typechecks, the web unit suite, and the static build. Keep adapter tests for keyboard navigation, annotations, disabled state, typeahead, empty values, external updates, controlled state, and dialog portal placement. Menu consumers cover account permissions, real links, callback isolation, and breadcrumb portals. Preset tests cover naming, cancellation, focus return, applying, deleting, and explicit overwrite confirmation. Existing consumer tests exercise import mapping, collection rules, download protocol preference, and request monitoring.

Library control tests cover separate toolbar rows, direct view and thumbnail controls, selection entry/exit, persisted collapsed rows, sort direction, and compact thumbnail popover dismissal. Searchable selection tests cover bounded results, matches beyond the initial limit, disabled items, provider fallback, external value changes, empty results, keyboard commit, and Escape/focus return. Also check these controls in a populated library and Identify through the running app.

Bulk-action tests cover eligibility, selected-ID callbacks, keyboard opening, and Escape/focus return. Pagination tests cover page-size selection and cancellation, navigation callbacks, loading/error guards, and retry. Grid tests verify that expansion does not change requests or card order and that Clear preserves browsing preferences.

Global search tests cover recent-search keyboard activation, preserved server ranking, per-kind limits, full-result actions, navigation/history, stale responses during debounce, empty/error/retry states, and action-key isolation. Validate Escape/focus return, pointer interaction after dismissal, and native-dialog nesting in the running app; JSDOM cannot prove top-layer rendering.

Also inspect `/design-language#component-bases` and a real Settings section through the .NET app. Test pointer selection, keyboard selection, scrolling, focus return, layered Escape, and a narrow viewport. DOM tests do not establish native top-layer behavior, touch behavior, or visual quality.

Sources: [installation](https://www.shadcn-svelte.com/docs/installation), [Select](https://www.shadcn-svelte.com/docs/components/select), [Switch](https://www.shadcn-svelte.com/docs/components/switch), [DropdownMenu](https://www.shadcn-svelte.com/docs/components/dropdown-menu), [Popover](https://www.shadcn-svelte.com/docs/components/popover), [Toggle Group](https://www.shadcn-svelte.com/docs/components/toggle-group), [Slider](https://www.shadcn-svelte.com/docs/components/slider), [Command](https://www.shadcn-svelte.com/docs/components/command), [Bits Command structure](https://www.bits-ui.com/docs/components/command#structure), [theming](https://www.shadcn-svelte.com/docs/theming). Upstream attribution is retained in `LICENSE.md`.
