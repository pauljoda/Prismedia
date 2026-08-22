<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { AlertTriangle, Headphones, Pause, Play } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { onMount } from "svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { getBookMetadataCapability, getCapability } from "$lib/api/capabilities";
  import { recordEntityConsumptionEvent, updateEntityProgress } from "$lib/api/consumption";
  import {
    BOOK_FORMAT,
    CAPABILITY_KIND,
    CONSUMPTION_ACTIVITY_KIND,
    CONSUMPTION_EVENT_KIND,
    PROGRESS_UNIT,
    READER_MODE,
  } from "$lib/api/generated/codes";
  import BookFileReader from "$lib/components/BookFileReader.svelte";
  import PdfReader from "$lib/components/PdfReader.svelte";
  import { ConsumptionActivityClock } from "$lib/entities/consumption-activity-clock";
  import {
    exactWebEpubResumeLocation,
    webEpubLaunchLocation,
  } from "$lib/entities/epub-contents";
  import {
    bookReaderContextFromUrl,
    bookReaderReturnHref,
    type BookReaderRouteContext,
  } from "$lib/entities/book-reader-route";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";

  type LoadState = "loading" | "ready" | "error";
  type ReaderFlow = "paginated" | "scrolled";
  type ReaderSurface = "epub" | "pdf";

  const nsfw = useNsfw();
  const playback = useAudioPlayback()!;

  let loadState: LoadState = $state("loading");
  let surface: ReaderSurface | null = $state(null);
  let book = $state.raw<EntityCardFull | null>(null);
  let context = $state.raw<BookReaderRouteContext | null>(null);
  let readerTitle = $state("Reader");
  let returnHref = $state("/books");
  let errorMessage = $state<string | null>(null);
  let sourceUrl = $state("");
  let epubLocation = $state.raw<string | null>(null);
  let epubInitialFraction = $state.raw<number | null>(null);
  let epubFlow = $state.raw<ReaderFlow>("paginated");
  let epubSaveLocation: string | null = null;
  let epubSaveFraction = 0;
  let epubFlowMode: ReaderFlow = "paginated";
  let pdfInitialPage = $state(0);
  let pdfLastPage = 0;
  let pdfLastCount = 0;
  let progressSaveQueue: Promise<void> = Promise.resolve();
  const readerActivityClock = new ConsumptionActivityClock();
  const readerConsumptionSessionId = createReaderSessionId();

  const bookId = $derived(page.params.id ?? "");
  const combinedAudiobookActive = $derived(
    Boolean(
      context?.combined &&
      book &&
      playback.context?.playbackOwnerEntityId === book.id &&
      playback.currentTrack,
    ),
  );

  onMount(() => {
    const startReader = async () => {
      await loadReader(page.url);
      if (loadState === "ready" && book) {
        void recordEntityConsumptionEvent(book.id, {
          kind: CONSUMPTION_EVENT_KIND.accessed,
          sessionId: readerConsumptionSessionId,
        }).catch(() => undefined);
      }
      if (loadState === "ready" && document.visibilityState === "visible") {
        readerActivityClock.start();
      }
    };
    const heartbeat = window.setInterval(() => queueReaderActivityHeartbeat(false), 15_000);
    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible") readerActivityClock.start();
      else queueReaderActivityHeartbeat(true);
    };
    document.addEventListener("visibilitychange", handleVisibilityChange);
    void startReader();

    return () => {
      window.clearInterval(heartbeat);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      queueReaderActivityHeartbeat(true);
    };
  });

  function createReaderSessionId(): string {
    return globalThis.crypto?.randomUUID?.() ?? `reader-${Date.now()}-${Math.random()}`;
  }

  function queueReaderActivityHeartbeat(stop: boolean) {
    const activitySeconds = stop ? readerActivityClock.stop() : readerActivityClock.take();
    if (!activitySeconds || loadState !== "ready") return;
    if (surface === "epub") {
      void queueEpubSave(false, activitySeconds).catch(() => undefined);
    } else if (surface === "pdf") {
      void queuePdfSave(pdfLastPage, pdfLastCount, false, activitySeconds).catch(() => undefined);
    }
  }

  async function loadReader(url: URL) {
    loadState = "loading";
    errorMessage = null;
    surface = null;
    const nextContext = bookReaderContextFromUrl(url) ?? {
      kind: "book" as const,
      id: bookId,
      command: "resume" as const,
    };

    try {
      const nextBook = await fetchEntity(bookId);
      const format = getBookMetadataCapability(nextBook.capabilities)?.format;
      if (format !== BOOK_FORMAT.epub && format !== BOOK_FORMAT.pdf) {
        throw new Error("This book has no readable EPUB or PDF rendition.");
      }

      book = nextBook;
      context = nextContext;
      readerTitle = nextBook.title;
      returnHref = await resolveReaderReturnHref(nextBook.id, nextContext);
      sourceUrl = `/entities/${nextBook.id}/files/source`;
      if (format === BOOK_FORMAT.epub) loadEpubState(nextBook, nextContext);
      else loadPdfState(nextBook, nextContext);
      loadState = "ready";
    } catch (error) {
      if (redirectHiddenEntityNotFound(error, nsfw.mode)) return;
      errorMessage = error instanceof Error ? error.message : String(error);
      loadState = "error";
    }
  }

  function loadEpubState(nextBook: EntityCardFull, nextContext: BookReaderRouteContext) {
    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
    const resume = nextContext.command !== "start-over" && !progress?.completedAt;
    const launchLocation = webEpubLaunchLocation(nextContext.location);
    const launchFraction = launchLocation ? null : nextContext.fraction ?? null;
    const persistedLocation = resume ? exactWebEpubResumeLocation(progress?.location) : null;
    surface = "epub";
    epubLocation = launchLocation ?? (launchFraction === null ? persistedLocation : null);
    epubInitialFraction = launchFraction
      ?? (epubLocation
        ? null
        : resume && Number(progress?.total ?? 0) > 0
          ? Number(progress?.index ?? 0) / Number(progress?.total ?? 0)
          : null);
    epubFlow = progress?.mode === READER_MODE.scrolled ? "scrolled" : "paginated";
    epubFlowMode = epubFlow;
    epubSaveLocation = epubLocation;
    epubSaveFraction = launchFraction
      ?? (launchLocation ? 0 : resume ? Number(progress?.index ?? 0) / 10_000 : 0);
  }

  function loadPdfState(nextBook: EntityCardFull, nextContext: BookReaderRouteContext) {
    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
    const resume = nextContext.command !== "start-over" && !progress?.completedAt;
    surface = "pdf";
    pdfInitialPage = resume ? Math.max(0, Number(progress?.index ?? 0)) : 0;
    pdfLastPage = pdfInitialPage;
    pdfLastCount = Math.max(0, Number(progress?.total ?? 0));
  }

  function handleEpubLocation(location: { cfi: string | null; fraction: number; label: string | null }) {
    epubSaveLocation = location.cfi;
    epubSaveFraction = location.fraction;
    void queueEpubSave().catch(() => undefined);
  }

  function handleEpubFlow(flow: ReaderFlow) {
    epubFlowMode = flow;
    void queueEpubSave().catch(() => undefined);
  }

  async function saveEpubProgress(
    completed = false,
    activitySeconds = readerActivityClock.take(),
  ) {
    if (!book) return;
    const index = Math.max(0, Math.min(10_000, Math.round(epubSaveFraction * 10_000)));
    await updateEntityProgress(book.id, {
      currentEntityId: book.id,
      unit: PROGRESS_UNIT.cfi,
      index,
      total: 10_000,
      mode: epubFlowMode === "scrolled" ? READER_MODE.scrolled : READER_MODE.paged,
      location: epubSaveLocation,
      completed: completed ? true : null,
      activitySeconds,
      activityKind: activitySeconds ? CONSUMPTION_ACTIVITY_KIND.reading : undefined,
    });
  }

  function queueEpubSave(completed = false, activitySeconds?: number | null) {
    const save = progressSaveQueue
      .catch(() => undefined)
      .then(() => saveEpubProgress(completed, activitySeconds));
    progressSaveQueue = save;
    return save;
  }

  async function savePdfProgress(
    pageIndex: number,
    pageCount: number,
    completed: boolean,
    activitySeconds = readerActivityClock.take(),
  ) {
    if (!book || pageCount <= 0) return;
    await updateEntityProgress(book.id, {
      currentEntityId: book.id,
      unit: PROGRESS_UNIT.page,
      index: clampPageIndex(pageIndex, pageCount),
      total: pageCount,
      mode: READER_MODE.scrolled,
      completed: completed ? true : null,
      activitySeconds,
      activityKind: activitySeconds ? CONSUMPTION_ACTIVITY_KIND.reading : undefined,
    });
  }

  function queuePdfSave(
    pageIndex: number,
    pageCount: number,
    completed: boolean,
    activitySeconds?: number | null,
  ) {
    const save = progressSaveQueue
      .catch(() => undefined)
      .then(() => savePdfProgress(pageIndex, pageCount, completed, activitySeconds));
    progressSaveQueue = save;
    return save;
  }

  function handlePdfPageChange(pageIndex: number, pageCount: number) {
    pdfLastPage = pageIndex;
    pdfLastCount = pageCount;
    void queuePdfSave(pageIndex, pageCount, pageCount > 0 && pageIndex >= pageCount - 1)
      .catch(() => undefined);
  }

  async function closeReader() {
    if (surface === "epub") {
      await queueEpubSave(epubSaveFraction >= 0.995).catch(() => undefined);
    } else if (surface === "pdf") {
      await queuePdfSave(
        pdfLastPage,
        pdfLastCount,
        pdfLastCount > 0 && pdfLastPage >= pdfLastCount - 1,
      ).catch(() => undefined);
    }
    await goto(returnHref);
  }

  function clampPageIndex(index: number, count: number): number {
    return Math.max(0, Math.min(index, Math.max(0, count - 1)));
  }

  async function resolveReaderReturnHref(bookEntityId: string, nextContext: BookReaderRouteContext) {
    if (nextContext.returnId) {
      const href = await resolveEntityHrefById(nextContext.returnId).catch(() => null);
      if (href) return href;
    }
    return bookReaderReturnHref(bookEntityId, nextContext);
  }
</script>

<svelte:head>
  <title>{readerTitle} · Prismedia</title>
</svelte:head>

{#snippet combinedAudioControls()}
  <div class="combined-audio-controls" aria-label="Companion audiobook controls">
    <Headphones class="h-3.5 w-3.5 shrink-0" />
    <span class="combined-track-title">{playback.currentTrack?.title ?? "Audiobook"}</span>
    <Button
      variant="ghost"
      size="icon"
      class="combined-toggle"
      aria-label={playback.playing ? "Pause companion audiobook" : "Play companion audiobook"}
      title={playback.playing ? "Pause audiobook" : "Play audiobook"}
      onclick={() => playback.toggle()}
    >
      {#if playback.playing}
        <Pause class="h-3.5 w-3.5" fill="currentColor" />
      {:else}
        <Play class="h-3.5 w-3.5" fill="currentColor" />
      {/if}
    </Button>
  </div>
{/snippet}

{#if loadState === "ready" && surface === "pdf"}
  <PdfReader
    sourceUrl={sourceUrl}
    title={readerTitle}
    presentation="page"
    closeIcon="back"
    initialPage={pdfInitialPage}
    onPageChange={handlePdfPageChange}
    onClose={() => void closeReader()}
  />
{:else if loadState === "ready" && surface === "epub"}
  <BookFileReader
    sourceUrl={sourceUrl}
    contentType="application/epub+zip"
    title={readerTitle}
    presentation="page"
    closeIcon="back"
    initialLocation={epubLocation}
    initialFraction={epubInitialFraction}
    initialFlow={epubFlow}
    onLocationChange={handleEpubLocation}
    onFlowChange={handleEpubFlow}
    companionControls={combinedAudiobookActive ? combinedAudioControls : undefined}
    onClose={() => void closeReader()}
  />
{:else}
  <main class="reader-route-shell">
    {#if loadState === "error"}
      <section class="reader-route-error">
        <AlertTriangle class="h-5 w-5" />
        <p>{errorMessage ?? "Unable to open reader."}</p>
        <Button variant="secondary" onclick={() => void goto(returnHref)}>Back</Button>
      </section>
    {/if}
  </main>
{/if}

<style>
  .reader-route-shell {
    position: fixed;
    inset: 0;
    z-index: 90;
    display: grid;
    place-items: center;
    background: #000;
    color: var(--color-text-primary);
  }

  .reader-route-error {
    display: grid;
    justify-items: center;
    gap: 0.75rem;
    max-width: 28rem;
    padding: 1.25rem;
    text-align: center;
    color: var(--color-text-secondary);
  }

  .combined-audio-controls {
    display: flex;
    min-width: 0;
    max-width: min(15rem, 34vw);
    align-items: center;
    gap: 0.35rem;
    border: 1px solid color-mix(in srgb, var(--color-border-accent-strong) 72%, transparent);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    padding-left: 0.48rem;
    color: var(--color-text-accent-bright);
    backdrop-filter: blur(var(--glass-blur-sm));
  }

  .combined-track-title {
    overflow: hidden;
    color: var(--color-text-secondary);
    font-size: 0.65rem;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  :global(.combined-toggle) {
    width: 1.9rem;
    height: 1.9rem;
    flex: 0 0 auto;
    color: var(--color-text-accent-bright);
  }

  @media (max-width: 540px) {
    .combined-track-title {
      display: none;
    }
  }
</style>
