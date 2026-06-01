<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { AlertTriangle } from "@lucide/svelte";
  import { getCapability } from "$lib/api/capabilities";
  import { fetchBook, type BookDetail } from "$lib/api/media";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityProgress } from "$lib/api/playback";
  import {
    bookEntityProgressDisplay,
    entityPageToReaderImage,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import {
    bookReaderContextFromUrl,
    bookReaderHref,
    bookReaderReturnHref,
    type BookReaderRouteContext,
  } from "$lib/entities/book-reader-route";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnail } from "$lib/api/generated/model";
  import ComicReader from "$lib/components/ComicReader.svelte";
  import BookFileReader from "$lib/components/BookFileReader.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  // Book formats whose reader streams the raw source file (no chapter/page entities).
  const SINGLE_FILE_FORMATS = new Set(["epub", "pdf"]);
  type ReaderFlow = "paginated" | "scrolled";

  type LoadState = "loading" | "ready" | "error";
  type ReaderMode = "paged" | "webtoon";

  interface ReaderChapter {
    detail: EntityCardFull;
    pages: EntityThumbnail[];
    summary: BookReaderChapter;
  }

  const nsfw = useNsfw();

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  let context = $state.raw<BookReaderRouteContext | null>(null);
  let readerChapters = $state.raw<ReaderChapter[]>([]);
  let nextChapter = $state.raw<BookReaderChapter | null>(null);
  let readerIndex = $state(0);
  let readerMode: ReaderMode = $state("paged");
  let readerTitle = $state("Reader");
  let returnHref = $state("/books");
  let errorMessage: string | null = $state(null);
  let progressSaveQueue: Promise<void> = Promise.resolve();

  // Single-file book reader state (EPUB/PDF).
  let singleFileBook = $state(false);
  let singleFileSource = $state("");
  let singleFileContentType = $state("application/epub+zip");
  let singleFileLocation = $state.raw<string | null>(null);
  let singleFileFlow = $state.raw<ReaderFlow>("paginated");
  let singleFileSaveLocation: string | null = null;
  let singleFileSaveFraction = 0;
  let singleFileFlowMode: ReaderFlow = "paginated";

  const bookId = $derived(page.params.id ?? "");
  const readerPages = $derived(
    readerChapters.flatMap((chapter) => chapter.pages.map(entityPageToReaderImage)),
  );

  onMount(() => {
    void loadReader(page.url);
  });

  async function loadReader(url: URL) {
    loadState = "loading";
    errorMessage = null;

    const nextContext = bookReaderContextFromUrl(url);
    if (!nextContext) {
      context = null;
      errorMessage = "Reader link is missing a valid context.";
      loadState = "error";
      return;
    }

    try {
      const nextBook = await fetchBook(bookId);

      if (SINGLE_FILE_FORMATS.has(nextBook.format)) {
        await loadSingleFileReader(nextBook, nextContext);
        return;
      }

      const resolved = await resolveReader(nextBook, nextContext);
      book = nextBook;
      context = nextContext;
      readerChapters = resolved.chapters;
      nextChapter = resolved.nextChapter;
      readerMode = nextContext.mode ?? resolved.readerMode;
      readerIndex = clampIndex(nextContext.pageIndex ?? resolved.initialIndex, resolved.pageCount);
      readerTitle = resolved.title;
      returnHref = await resolveReaderReturnHref(nextBook.id, nextContext);
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function loadSingleFileReader(nextBook: BookDetail, nextContext: BookReaderRouteContext) {
    const progress = getCapability(nextBook.capabilities, "progress");
    const resume = nextContext.command !== "start-over" && !progress?.completedAt;
    singleFileBook = true;
    singleFileSource = `/entities/${nextBook.id}/files/source`;
    singleFileContentType = nextBook.format === "pdf" ? "application/pdf" : "application/epub+zip";
    singleFileLocation = resume ? progress?.location ?? null : null;
    singleFileFlow = progress?.mode === "scrolled" ? "scrolled" : "paginated";
    singleFileFlowMode = singleFileFlow;
    singleFileSaveLocation = singleFileLocation;
    singleFileSaveFraction = resume ? Number(progress?.index ?? 0) / 10000 : 0;
    book = nextBook;
    context = nextContext;
    readerTitle = nextBook.title;
    returnHref = await resolveReaderReturnHref(nextBook.id, nextContext);
    loadState = "ready";
  }

  function handleSingleFileLocation(location: { cfi: string | null; fraction: number; label: string | null }) {
    singleFileSaveLocation = location.cfi;
    singleFileSaveFraction = location.fraction;
    void queueSingleFileSave().catch(() => undefined);
  }

  function handleSingleFileFlow(flow: ReaderFlow) {
    singleFileFlowMode = flow;
    void queueSingleFileSave().catch(() => undefined);
  }

  async function saveSingleFileProgress(completed = false) {
    if (!book) return;
    const percent = Math.max(0, Math.min(10000, Math.round(singleFileSaveFraction * 10000)));
    await updateEntityProgress(book.id, {
      currentEntityId: book.id,
      unit: "cfi",
      index: percent,
      total: 10000,
      mode: singleFileFlowMode,
      location: singleFileSaveLocation,
      completed: completed ? true : null,
    });
  }

  function queueSingleFileSave(completed = false) {
    const nextSave = progressSaveQueue
      .catch(() => undefined)
      .then(() => saveSingleFileProgress(completed));
    progressSaveQueue = nextSave;
    return nextSave;
  }

  async function closeSingleFileReader() {
    const completed = singleFileSaveFraction >= 0.995;
    await queueSingleFileSave(completed).catch(() => undefined);
    await goto(returnHref);
  }

  async function resolveReader(nextBook: BookDetail, nextContext: BookReaderRouteContext) {
    if (nextContext.kind === "volume") {
      return resolveVolumeReader(nextBook, nextContext.id);
    }
    if (nextContext.kind === "book") {
      return resolveBookReader(nextBook, nextContext);
    }
    return resolveChapterReader(nextBook, nextContext);
  }

  async function resolveChapterReader(nextBook: BookDetail, nextContext: BookReaderRouteContext) {
    const chapter = await fetchEntity(nextContext.id);
    const summaries = await loadChapterSummaries(nextBook, chapter);
    const progress = bookEntityProgressDisplay(nextBook, summaries);
    const chapterIndex = summaries.findIndex((item) => item.id === chapter.id);
    const pages = orderedBookChildren(chapter, ENTITY_KIND.bookPage);
    const initialIndex = initialChapterIndex(nextContext, progress, chapter.id);

    return {
      title: `${nextBook.title} · ${chapter.title}`,
      chapters: [readerChapter(chapter, pages, chapterIndex >= 0 ? chapterIndex : 0)],
      nextChapter: chapterIndex >= 0 ? summaries[chapterIndex + 1] ?? null : null,
      initialIndex,
      readerMode: progress?.readerMode ?? "paged",
      pageCount: pages.length,
    };
  }

  async function resolveVolumeReader(nextBook: BookDetail, volumeId: string) {
    const volume = await fetchEntity(volumeId);
    const chapterDetails = await Promise.all(
      orderedBookChildren(volume, ENTITY_KIND.bookChapter).map((chapter) => fetchEntity(chapter.id)),
    );
    const chapters = chapterDetails.map((chapter, index) =>
      readerChapter(chapter, orderedBookChildren(chapter, ENTITY_KIND.bookPage), index),
    );
    const summaries = chapters.map((chapter) => chapter.summary);
    const progress = bookEntityProgressDisplay(nextBook, summaries);
    const initialIndex = progress && !progress.isComplete
      ? pageOffsetForChapter(chapters, progress.chapterId) + progress.currentPage - 1
      : 0;

    return {
      title: `${nextBook.title} · ${volume.title}`,
      chapters,
      nextChapter: null,
      initialIndex,
      readerMode: progress?.readerMode ?? "paged",
      pageCount: chapters.reduce((total, chapter) => total + chapter.pages.length, 0),
    };
  }

  async function resolveBookReader(nextBook: BookDetail, nextContext: BookReaderRouteContext) {
    const progressChapterId = getCapability(nextBook.capabilities, "progress")?.currentEntityId ?? null;
    if (nextContext.command === "resume" && progressChapterId) {
      const progressChapter = await fetchEntity(progressChapterId);
      if (progressChapter.kind === ENTITY_KIND.bookChapter) {
        return resolveBookChapterReader(nextBook, nextContext, progressChapter);
      }
    }

    const firstChapter = await loadFirstBookChapterDetail(nextBook);
    return resolveBookChapterReader(nextBook, nextContext, firstChapter);
  }

  async function resolveBookChapterReader(
    nextBook: BookDetail,
    nextContext: BookReaderRouteContext,
    selectedChapter: EntityCardFull | null,
  ) {
    const summaries = selectedChapter ? await loadChapterSummaries(nextBook, selectedChapter) : [];
    const progress = bookEntityProgressDisplay(nextBook, summaries);
    const selectedIndex = selectedChapter
      ? Math.max(0, summaries.findIndex((chapter) => chapter.id === selectedChapter.id))
      : -1;
    const pages = selectedChapter ? orderedBookChildren(selectedChapter, ENTITY_KIND.bookPage) : [];
    const initialIndex = selectedChapter && progress?.chapterId === selectedChapter.id && !progress.isComplete
      ? progress.currentPage - 1
      : 0;

    return {
      title: `${nextBook.title}${selectedChapter ? ` · ${selectedChapter.title}` : ""}`,
      chapters: selectedChapter ? [readerChapter(selectedChapter, pages, selectedIndex >= 0 ? selectedIndex : 0)] : [],
      nextChapter: selectedIndex >= 0 ? summaries[selectedIndex + 1] ?? null : null,
      initialIndex,
      readerMode: progress?.readerMode ?? "paged",
      pageCount: pages.length,
    };
  }

  async function loadFirstBookChapterDetail(nextBook: BookDetail): Promise<EntityCardFull | null> {
    const directChapters = orderedBookChildren(nextBook, ENTITY_KIND.bookChapter);
    if (directChapters.length > 0) {
      return fetchEntity(directChapters[0].id);
    }

    const firstVolume = orderedBookChildren(nextBook, ENTITY_KIND.bookVolume)[0];
    if (!firstVolume) return null;

    const volume = await fetchEntity(firstVolume.id);
    const firstVolumeChapter = orderedBookChildren(volume, ENTITY_KIND.bookChapter)[0];
    return firstVolumeChapter ? fetchEntity(firstVolumeChapter.id) : null;
  }

  async function loadChapterSummaries(
    nextBook: BookDetail,
    currentChapter: EntityCardFull,
  ): Promise<BookReaderChapter[]> {
    const currentPageCount = orderedBookChildren(currentChapter, ENTITY_KIND.bookPage).length;
    const volumeThumbnails = orderedBookChildren(nextBook, ENTITY_KIND.bookVolume);
    let parentVolumeIndex = volumeThumbnails.findIndex((volume) => volume.id === currentChapter.parentEntityId);
    let currentVolume: EntityCardFull | null = null;

    if (parentVolumeIndex >= 0) {
      currentVolume = await fetchEntity(volumeThumbnails[parentVolumeIndex].id);
    } else {
      for (const [index, volumeThumbnail] of volumeThumbnails.entries()) {
        const volume = await fetchEntity(volumeThumbnail.id);
        if (orderedBookChildren(volume, ENTITY_KIND.bookChapter).some((child) => child.id === currentChapter.id)) {
          parentVolumeIndex = index;
          currentVolume = volume;
          break;
        }
      }
    }

    if (parentVolumeIndex >= 0 && currentVolume) {
      let chapterThumbnails = orderedBookChildren(currentVolume, ENTITY_KIND.bookChapter);
      const currentIndex = chapterThumbnails.findIndex((chapter) => chapter.id === currentChapter.id);

      if (currentIndex === chapterThumbnails.length - 1) {
        const nextVolume = volumeThumbnails[parentVolumeIndex + 1];
        if (nextVolume) {
          const nextVolumeDetail = await fetchEntity(nextVolume.id);
          chapterThumbnails = [
            ...chapterThumbnails,
            ...orderedBookChildren(nextVolumeDetail, ENTITY_KIND.bookChapter),
          ];
        }
      }

      return chapterThumbnails.map((thumbnail, index) => ({
        id: thumbnail.id,
        title: thumbnail.title,
        sortOrder: index,
        pageCount: thumbnail.id === currentChapter.id ? currentPageCount : 0,
      }));
    }

    return orderedBookChildren(nextBook, ENTITY_KIND.bookChapter).map((thumbnail, index) => ({
      id: thumbnail.id,
      title: thumbnail.title,
      sortOrder: index,
      pageCount: thumbnail.id === currentChapter.id ? currentPageCount : 0,
    }));
  }

  function readerChapter(detail: EntityCardFull, pages: EntityThumbnail[], index: number): ReaderChapter {
    return {
      detail,
      pages,
      summary: {
        id: detail.id,
        title: detail.title,
        sortOrder: index,
        pageCount: pages.length,
      },
    };
  }

  function initialChapterIndex(
    nextContext: BookReaderRouteContext,
    progress: ReturnType<typeof bookEntityProgressDisplay>,
    chapterId: string,
  ) {
    if (nextContext.command === "start-over") return 0;
    if (progress?.chapterId === chapterId && !progress.isComplete) return progress.currentPage - 1;
    return 0;
  }

  function pageOffsetForChapter(chapters: ReaderChapter[], chapterId: string) {
    let offset = 0;
    for (const chapter of chapters) {
      if (chapter.detail.id === chapterId) return offset;
      offset += chapter.pages.length;
    }
    return 0;
  }

  function positionForReaderIndex(index: number) {
    let offset = 0;
    for (const chapter of readerChapters) {
      const nextOffset = offset + chapter.pages.length;
      if (index < nextOffset) {
        return { chapter, pageIndex: index - offset, pageCount: chapter.pages.length };
      }
      offset = nextOffset;
    }

    const chapter = readerChapters.at(-1) ?? null;
    return {
      chapter,
      pageIndex: Math.max(0, (chapter?.pages.length ?? 1) - 1),
      pageCount: chapter?.pages.length ?? 0,
    };
  }

  async function saveProgress(index = readerIndex, completed = false) {
    if (!book || readerPages.length === 0) return;
    const position = positionForReaderIndex(index);
    if (!position.chapter) return;
    await updateEntityProgress(book.id, {
      currentEntityId: position.chapter.detail.id,
      unit: "page",
      index: Math.max(0, Math.min(position.pageIndex, Math.max(0, position.pageCount - 1))),
      total: position.pageCount,
      mode: readerMode,
      // Only an explicit end-of-book save reports completion; mid-reading sends null so it does not
      // get treated as an explicit "mark unread".
      completed: completed ? true : null,
    });
  }

  function queueProgressSave(index = readerIndex, completed = false) {
    const nextSave = progressSaveQueue
      .catch(() => undefined)
      .then(() => saveProgress(index, completed));
    progressSaveQueue = nextSave;
    return nextSave;
  }

  function handleIndexChange(index: number) {
    readerIndex = index;
    const reachedEnd = readerPages.length > 0 && index >= readerPages.length - 1;
    void queueProgressSave(index, reachedEnd).catch(() => undefined);
  }

  function handleModeChange(mode: ReaderMode) {
    readerMode = mode;
    void queueProgressSave(readerIndex, false).catch(() => undefined);
  }

  async function handleNextChapter() {
    if (!book || !context || !nextChapter) return;
    await queueProgressSave(readerIndex, true).catch(() => undefined);
    const nextHref = bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: nextChapter.id,
      returnId: context.returnId ?? context.id,
      command: "resume",
      mode: readerMode,
    });
    await goto(nextHref);
    await loadReader(new URL(nextHref, page.url.origin));
  }

  async function closeReader() {
    const reachedEnd = readerPages.length > 0 && readerIndex >= readerPages.length - 1;
    await queueProgressSave(readerIndex, reachedEnd).catch(() => undefined);
    await goto(returnHref);
  }

  function clampIndex(index: number, pageCount: number) {
    return Math.max(0, Math.min(index, Math.max(0, pageCount - 1)));
  }

  async function resolveReaderReturnHref(bookId: string, nextContext: BookReaderRouteContext) {
    if (nextContext.returnId) {
      const href = await resolveEntityHrefById(nextContext.returnId).catch(() => null);
      if (href) return href;
    }

    return bookReaderReturnHref(bookId, nextContext);
  }
</script>

<svelte:head>
  <title>{readerTitle} · Prismedia</title>
</svelte:head>

{#if loadState === "ready" && singleFileBook}
  <BookFileReader
    sourceUrl={singleFileSource}
    contentType={singleFileContentType}
    title={readerTitle}
    presentation="page"
    closeIcon="back"
    initialLocation={singleFileLocation}
    initialFlow={singleFileFlow}
    onLocationChange={handleSingleFileLocation}
    onFlowChange={handleSingleFileFlow}
    onClose={() => void closeSingleFileReader()}
  />
{:else if loadState === "ready"}
  <ComicReader
    images={readerPages}
    initialIndex={readerIndex}
    initialMode={readerMode}
    nextChapterLabel={nextChapter?.title ?? null}
    title={readerTitle}
    presentation="page"
    closeIcon="back"
    onIndexChange={handleIndexChange}
    onModeChange={handleModeChange}
    onNextChapter={nextChapter ? handleNextChapter : undefined}
    onClose={() => void closeReader()}
  />
{:else}
  <main class="reader-route-shell">
    {#if loadState === "error"}
      <section class="reader-route-error">
        <AlertTriangle class="h-5 w-5" />
        <p>{errorMessage ?? "Unable to open reader."}</p>
        <button type="button" onclick={() => void goto(returnHref)}>Back</button>
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

  .reader-route-error button {
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    padding: 0.55rem 0.85rem;
    color: var(--color-text-primary);
  }
</style>
