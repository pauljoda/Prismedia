# Book Reader Route Design

## Goal

Move comic reading from a detail-page overlay into a dedicated full-page route so paged and webtoon modes never reveal the page behind the reader during transitions, scrolling, or reloads.

## Route Shape

The canonical route is `/books/[bookId]/reader`.

Query parameters:

- `kind=book|volume|chapter` identifies the reading context.
- `id=<guid>` identifies the book, volume, or chapter being read.
- `returnKind=book|volume|chapter` identifies where the back control should return.
- `returnId=<guid>` identifies the return target.
- `command=resume|start-over` optionally chooses progress-based or first-page startup.
- `mode=paged|webtoon` optionally overrides the saved reader mode.
- `page=<zero-based index>` optionally opens a specific page in the resolved reader page list.

If return parameters are missing or invalid, the reader falls back to the natural context route: book detail for `book`, volume detail for `volume`, and chapter detail for `chapter`.

## Behavior

All existing reader entry points navigate to the route instead of mounting `ComicReader` as an overlay. This includes book resume/start-over actions, book detail reading, volume reading, chapter reading, and chapter page-grid activation.

The route loads the book and the requested entity context. For chapter contexts it reads one chapter. For volume contexts it stitches all volume chapters into one page list. For book contexts it reads the selected progress chapter when resuming, or the first direct chapter when starting over. Progress is saved through the existing `updateEntityProgress` API using the current chapter and local page index.

The reader close control becomes a back arrow. It saves the current position, then navigates to the return URL derived from `returnKind` and `returnId`. Escape should perform the same close/back action. Next-chapter behavior stays in the reader route: chapter and book contexts switch to the next chapter, while volume contexts advance inside the stitched volume list until the volume is complete.

## Components

`ComicReader.svelte` remains the reusable reader UI. It gains a page presentation mode that does not portal into `document.body`, uses a solid black route surface, and renders a back arrow instead of an X when requested.

Route/query construction lives in `apps/web-svelte/src/lib/entities/book-reader-route.ts` so tests can cover URL parsing, return URL construction, and reader href construction without rendering the route.

## Testing

Unit tests cover supported query parsing, fallback return URLs, reader href construction, and page index clamping. Existing `ComicReader` tests continue to cover interaction behavior. The implementation is verified with focused Vitest runs, `svelte-check`, and `release:check`.
