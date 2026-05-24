# Book Reader Route Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace comic reader overlays with a dedicated `/books/[bookId]/reader` route that supports book, volume, and chapter contexts.

**Architecture:** Keep `ComicReader.svelte` as the reusable reading surface, but add a route presentation mode with a back arrow. Put URL parsing/building in `book-reader-route.ts`; put route orchestration in one Svelte page that loads the correct context, maps global page indexes to chapter-local progress, and returns to the requested entity.

**Tech Stack:** Svelte 5, SvelteKit route pages, TypeScript, Vitest, existing Prismedia API helpers.

---

### Task 1: Reader URL Helpers

**Files:**
- Modify: `apps/web-svelte/src/lib/entities/book-reader-route.ts`
- Modify: `apps/web-svelte/src/lib/entities/book-reader-route.test.ts`

- [ ] Add tests for `bookReaderContextFromUrl`, `bookReaderReturnHref`, and `bookReaderHref`.
- [ ] Implement typed context parsing for `book|volume|chapter`, supported commands, supported modes, and numeric page indexes.
- [ ] Implement return URL fallback rules:
  - book: `/books/{bookId}`
  - volume: `/books/{bookId}/volumes/{returnId}`
  - chapter: `/books/{bookId}/chapters/{returnId}`
- [ ] Implement href building for launch points with `kind`, `id`, `returnKind`, `returnId`, and optional `command`, `mode`, `page`.
- [ ] Run `pnpm --filter @prismedia/web-svelte exec vitest run --config vitest.config.ts src/lib/entities/book-reader-route.test.ts`.
- [ ] Commit helper changes.

### Task 2: Route Presentation for ComicReader

**Files:**
- Modify: `apps/web-svelte/src/lib/components/ComicReader.svelte`
- Modify: `apps/web-svelte/src/lib/components/ComicReader.test.ts`

- [ ] Add a failing test that renders `ComicReader` with `presentation="page"` and `closeIcon="back"` and asserts the dialog is not portaled and the close button is labelled `Back`.
- [ ] Add optional props:
  - `presentation?: "overlay" | "page"`
  - `closeIcon?: "close" | "back"`
- [ ] Render the root without `use:portal` in page presentation, keep overlay behavior as default, and use `ArrowLeft` for the back icon.
- [ ] Run `npx @sveltejs/mcp svelte-autofixer apps/web-svelte/src/lib/components/ComicReader.svelte --svelte-version 5`.
- [ ] Run `pnpm --filter @prismedia/web-svelte exec vitest run --config vitest.config.ts src/lib/components/ComicReader.test.ts`.
- [ ] Commit component changes.

### Task 3: Full-Page Reader Route

**Files:**
- Create: `apps/web-svelte/src/routes/books/[id]/reader/+page.svelte`
- Modify as needed: `apps/web-svelte/src/lib/entities/book-entity-reader.ts`

- [ ] Implement route state for loading, error, book, reader pages, current index, mode, title, chapters, and return href.
- [ ] Load `fetchBook(bookId)` plus the entity identified by `kind/id`.
- [ ] Resolve contexts:
  - `chapter`: one chapter's ordered pages.
  - `volume`: all ordered child chapters and pages.
  - `book`: progress chapter for resume, first direct chapter for start-over.
- [ ] Save progress with `updateEntityProgress`, mapping the reader page index back to the owning chapter and local page index.
- [ ] Navigate back via `goto(returnHref)` after save.
- [ ] Keep next-chapter navigation in the reader route by swapping to the next chapter where applicable.
- [ ] Render `ComicReader` with `presentation="page"` and `closeIcon="back"`.
- [ ] Run `npx @sveltejs/mcp svelte-autofixer 'apps/web-svelte/src/routes/books/[id]/reader/+page.svelte' --svelte-version 5`.

### Task 4: Redirect Launch Points

**Files:**
- Modify: `apps/web-svelte/src/routes/books/[id]/+page.svelte`
- Modify: `apps/web-svelte/src/routes/books/[id]/chapters/[chapterId]/+page.svelte`
- Modify: `apps/web-svelte/src/routes/books/[id]/volumes/[volumeId]/+page.svelte`
- Modify: `CHANGELOG.md`

- [ ] Replace local `readerOpen` overlays with `goto(bookReaderHref(...))`.
- [ ] Remove obsolete close/save overlay code from detail pages once the route owns progress saving.
- [ ] Preserve launch behavior for read, resume, start-over, volume reading, and chapter page-grid activation.
- [ ] Add a user-facing changelog entry under `Fixed`.
- [ ] Run focused Vitest tests for helpers and ComicReader.
- [ ] Run `pnpm --filter @prismedia/web-svelte typecheck`.
- [ ] Run `pnpm release:check`.
- [ ] Browser smoke test the route if a dev server is available or can be started without rebooting the full stack.
- [ ] Commit the implementation.
