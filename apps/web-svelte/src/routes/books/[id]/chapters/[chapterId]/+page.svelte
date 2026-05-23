<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { ArrowLeft, BookOpen, Check, Images, Info, Play, RotateCcw, SlidersHorizontal } from "@lucide/svelte";
  import {
    fetchBook,
    fetchEntity,
    updateEntityMetadata,
    updateEntityProgress,
    type BookDetail,
    type EntityCardFull,
  } from "$lib/api/prismedia";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    entityPageToReaderImage,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import ComicReader from "$lib/components/ComicReader.svelte";
  import EntityDetail, {
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  type ReaderMode = "paged" | "webtoon";

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  let chapter = $state<EntityCardFull | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let pageCards = $state<EntityThumbnailCard[]>([]);
  let chapterSummaries = $state.raw<BookReaderChapter[]>([]);
  let readerOpen = $state(false);
  let readerIndex = $state(0);
  let readerMode: ReaderMode = $state("paged");

  const bookId = $derived(page.params.id ?? "");
  const chapterId = $derived(page.params.chapterId ?? "");
  const bookTitle = $derived(book?.title ?? "Book");
  const card = $derived(chapter ? entityCardToDetailCard(chapter) as EntityDetailCardFull : null);
  const chapterPages = $derived(chapter ? orderedBookChildren(chapter, ENTITY_KIND.bookPage) : []);
  const readerPages = $derived(chapterPages.map(entityPageToReaderImage));
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const chapterProgress = $derived(progressDisplay?.chapterId === chapterId ? progressDisplay : null);
  const chapterIndex = $derived(chapterSummaries.findIndex((item) => item.id === chapterId));
  const nextChapter = $derived(
    chapterIndex >= 0 ? chapterSummaries[chapterIndex + 1] ?? null : null,
  );
  const primaryReadLabel = $derived(
    chapterProgress ? (chapterProgress.isComplete ? "Re-read chapter" : "Resume chapter") : "Read chapter",
  );
  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    const tabs: EntityDetailTab[] = [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "tags", "stats", "positions", "source"],
      },
    ];

    if (card.links.length > 0) {
      tabs.push({
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        count: card.links.length,
        sections: ["links"],
      });
    }

    return tabs;
  });

  onMount(() => {
    void loadChapter();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadChapter();
  });

  async function loadChapter() {
    loadState = "loading";
    errorMessage = null;
    try {
      const [nextBook, nextChapter] = await Promise.all([
        fetchBook(bookId),
        fetchEntity(chapterId),
      ]);
      book = nextBook;
      chapter = nextChapter;
      chapterSummaries = await loadChapterSummaries(nextBook, nextChapter);
      pageCards = thumbnailsToCards(orderedBookChildren(nextChapter, ENTITY_KIND.bookPage));

      const nextProgress = bookEntityProgressDisplay(nextBook, chapterSummaries);
      const readerCommand = page.url.searchParams.get("reader");
      if (readerCommand === "start-over") {
        readerIndex = 0;
        readerMode = nextProgress?.readerMode ?? "paged";
        readerOpen = true;
      } else if (readerCommand === "resume") {
        readerIndex = nextProgress?.chapterId === nextChapter.id && !nextProgress.isComplete
          ? nextProgress.currentPage - 1
          : 0;
        readerMode = nextProgress?.readerMode ?? "paged";
        readerOpen = true;
      } else if (nextProgress?.chapterId === nextChapter.id && !nextProgress.isComplete) {
        readerIndex = nextProgress.currentPage - 1;
        readerMode = nextProgress.readerMode;
      } else {
        readerIndex = 0;
        readerMode = nextProgress?.readerMode ?? "paged";
      }
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
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

    const directChapters = orderedBookChildren(nextBook, ENTITY_KIND.bookChapter);
    return directChapters.map((thumbnail, index) => ({
      id: thumbnail.id,
      title: thumbnail.title,
      sortOrder: index,
      pageCount: thumbnail.id === currentChapter.id ? currentPageCount : 0,
    }));
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!chapter) return;
    await updateEntityMetadata(chapter.id, request, { kind: chapter.kind });
    await loadChapter();
  }

  function openReaderAt(index: number) {
    readerIndex = Math.max(0, Math.min(index, Math.max(0, readerPages.length - 1)));
    readerOpen = true;
  }

  function openPrimaryReader() {
    openReaderAt(chapterProgress && !chapterProgress.isComplete ? chapterProgress.currentPage - 1 : 0);
  }

  async function saveProgress(index = readerIndex, completed = false) {
    if (!book || !chapter || readerPages.length === 0) return;
    await updateEntityProgress(book.id, {
      currentEntityId: chapter.id,
      unit: "page",
      index: Math.max(0, Math.min(index, readerPages.length - 1)),
      total: readerPages.length,
      mode: readerMode,
      completed,
    });
  }

  function handleIndexChange(index: number) {
    readerIndex = index;
    const reachedEnd = readerPages.length > 0 && index >= readerPages.length - 1;
    void saveProgress(index, reachedEnd);
  }

  function handleModeChange(mode: ReaderMode) {
    readerMode = mode;
    void saveProgress(readerIndex, false);
  }

  async function handleNextChapter() {
    if (!book || !nextChapter) return;
    await saveProgress(Math.max(0, readerPages.length - 1), true);
    location.href = `/books/${book.id}/chapters/${nextChapter.id}?reader=resume`;
  }

  async function markChapterRead() {
    if (readerPages.length === 0) return;
    readerIndex = Math.max(0, readerPages.length - 1);
    await saveProgress(readerIndex, true);
    await loadChapter();
  }

  async function closeReader() {
    readerOpen = false;
    const reachedEnd = readerPages.length > 0 && readerIndex >= readerPages.length - 1;
    await saveProgress(readerIndex, reachedEnd);
    await loadChapter();
  }
</script>

<svelte:head>
  <title>{chapter?.title ?? "Chapter"} · Prismedia</title>
</svelte:head>

<div class="chapter-page">
  <a href={`/books/${bookId}`} class="back-link">
    <ArrowLeft class="h-4 w-4" />
    {book?.title ?? "Book"}
  </a>

  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true"></div>
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load chapter."}</p>
      <button type="button" onclick={() => void loadChapter()}>Retry</button>
    </div>
  {:else if card && chapter && book}
    <EntityDetail
      {card}
      onMetadataSave={handleMetadataSave}
      posterSize="large"
      tabs={detailTabs}
    >
      {#snippet heroMeta()}
        <span class="meta-item">{bookTitle}</span>
        <span class="meta-sep"></span>
        <span class="meta-item">
          {readerPages.length} page{readerPages.length === 1 ? "" : "s"}
        </span>
      {/snippet}

      {#snippet extraActions()}
        {#if readerPages.length > 0}
          <button type="button" class="reader-action" onclick={openPrimaryReader}>
            <Play class="h-3.5 w-3.5" />
            {primaryReadLabel}
          </button>
          {#if !chapterProgress?.isComplete}
            <button type="button" class="reader-action" onclick={() => void markChapterRead()}>
              <Check class="h-3.5 w-3.5" />
              Mark read
            </button>
          {/if}
          {#if chapterProgress && !chapterProgress.isComplete}
            <button type="button" class="reader-action" onclick={() => openReaderAt(0)}>
              <RotateCcw class="h-3.5 w-3.5" />
              Start over
            </button>
          {/if}
        {/if}
      {/snippet}
    </EntityDetail>

    <section class="content-section">
      <h2 class="content-heading">
        <Images class="h-4 w-4" />
        Pages
        <span class="content-count">{pageCards.length}</span>
      </h2>
      <EntityGrid
        cards={pageCards}
        prefsKey={`book-${book.id}-chapter-${chapter.id}-pages`}
        initialSortBy="position"
        emptyTitle="No pages"
        emptyMessage="No pages found in this chapter."
        onCardActivate={(card, visibleCards) => {
          const index = visibleCards.findIndex((item) => item.entity.id === card.entity.id);
          openReaderAt(Math.max(0, index));
        }}
      />
    </section>
  {/if}
</div>

{#if readerOpen}
  <ComicReader
    images={readerPages}
    initialIndex={readerIndex}
    initialMode={readerMode}
    nextChapterLabel={nextChapter?.title ?? null}
    title={`${book?.title ?? "Book"}${chapter ? ` · ${chapter.title}` : ""}`}
    onIndexChange={handleIndexChange}
    onModeChange={handleModeChange}
    onNextChapter={nextChapter ? handleNextChapter : undefined}
    onClose={() => void closeReader()}
  />
{/if}

<style>
  .chapter-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }

  .back-link {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.78rem;
    text-decoration: none;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    transition: color 0.15s;
  }

  .back-link:hover {
    color: var(--color-text-primary, #f2eed8);
  }

  .loading-shell {
    min-height: 28rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    animation: pulse 1.2s ease-in-out infinite;
  }

  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .error-notice button,
  .reader-action {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.4rem 0.8rem;
    font-size: 0.78rem;
    cursor: pointer;
  }

  .reader-action:hover,
  .error-notice button:hover {
    color: var(--color-text-accent, #c49a5a);
    border-color: rgba(196, 154, 90, 0.45);
    box-shadow: 0 0 16px rgb(196 154 90 / 0.16);
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

  .content-section {
    display: grid;
    gap: 0.75rem;
  }

  .content-heading {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
    font-weight: 600;
    color: var(--color-text-primary, #f2eed8);
  }

  .content-count {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    color: var(--color-text-muted, #8a93a6);
    padding: 0.1rem 0.4rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
