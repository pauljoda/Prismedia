# `components/forms/`

Domain-specific form controls used across the Svelte app. **These are
not generic primitives** — they wrap `@prismedia/ui-svelte` (or HTML
elements directly) with Prismedia conventions baked in: NSFW awareness,
the Dark Room visual rules (sharp corners, brass accents, glow on
focus), and the entity-edit data model used by the rest of the app.

## What lives here

| Component | Purpose |
|-----------|---------|
| [`FormField.svelte`](./FormField.svelte) | Label + helper-text wrapper used by every other field component |
| [`TextField.svelte`](./TextField.svelte) | Single-line text input |
| [`TextAreaField.svelte`](./TextAreaField.svelte) | Multi-line text input |
| [`DateField.svelte`](./DateField.svelte) | Date input with the Dark Room calendar styling |
| [`SearchSelect.svelte`](./SearchSelect.svelte) | Async-search dropdown for picking a single related entity (studio, performer, etc.) |
| [`TagSelect.svelte`](./TagSelect.svelte) | Multi-select for tags, with NSFW-aware filtering |
| [`ToggleChip.svelte`](./ToggleChip.svelte) | On/off chip used in field-mask UIs (e.g. "include this field when accepting a scrape") |
| [`FormActions.svelte`](./FormActions.svelte) | Save / Cancel button row with consistent spacing |
| [`EditFormShell.svelte`](./EditFormShell.svelte) | The wrapper around an entity edit form (header, save bar, dirty-state) |

## Where to put a new form-shaped component

- **Generic, reusable across any product** (e.g. a brand-neutral
  Button, Badge, Checkbox) → [`packages/ui-svelte/`](../../../../../../packages/ui-svelte/).
- **Domain-specific or app-specific behavior** (validation, fetching
  related entities, NSFW filtering, edit-form data model) → here.
- **One-off component used by a single page** → keep it next to the
  page or in the page's directory.

## Importing

Every component is exported from the [barrel `index.ts`](./index.ts):

```ts
import { TextField, TagSelect, EditFormShell } from "$lib/components/forms";
```
