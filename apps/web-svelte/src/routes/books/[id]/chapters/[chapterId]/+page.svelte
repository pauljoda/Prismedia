<script lang="ts">
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { BookOpen, Check, Images, Info, Play, RotateCcw, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityProgress } from "$lib/api/playback";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref, type BookReaderHrefOptions } from "$lib/entities/book-reader-route";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";

  let book = $state<EntityCardFull | null>(null);
  let pageCards = $state<EntityThumbnailCard[]>([]);
  let chapterSummaries = $state.raw<BookReaderChapter[]>([]);

  const bookId = $derived(page.params.id ?? "");
  const chapterId = $derived(page.params.chapterId ?? "");
  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${bookId}:${chapterId}`,
    load: async ({ signal }) => {
      const [nextBook, nextChapter] = await Promise.all([
        fetchEntity(bookId, { signal }),
        fetchEntity(chapterId, { signal }),
      ]);
      const nextChapterSummaries = await loadChapterSummaries(nextBook, nextChapter, signal);
      signal.throwIfAborted();
      book = nextBook;
      chapterSummaries = nextChapterSummaries;
      pageCards = thumbnailsToCards(orderedBookChildren(nextChapter, ENTITY_KIND.bookPage));
      return nextChapter;
    },
    breadcrumbs: (currentChapter) => [
      { label: "Books", href: "/books" },
      { label: book?.title ?? "Book", href: `/books/${bookId}` },
      { label: currentChapter.title },
    ],
  });
  const chapter = $derived(detail.entity);
  const bookTitle = $derived(book?.title ?? "Book");
  const card = $derived(chapter ? entityCardToDetailCard(chapter) as EntityDetailCardFull : null);
  const chapterPages = $derived(chapter ? orderedBookChildren(chapter, ENTITY_KIND.bookPage) : []);
  const readerPageCount = $derived(chapterPages.length);
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const chapterProgress = $derived(progressDisplay?.chapterId === chapterId ? progressDisplay : null);
  const primaryReadLabel = $derived(
    chapterProgress ? (chapterProgress.isComplete ? "Re-read chapter" : "Resume chapter") : "Read chapter",
  );
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    if (readerPageCount === 0) return [];
    return [
      {
        id: "read-chapter",
        label: primaryReadLabel,
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: openPrimaryReader,
      },
      {
        id: "mark-chapter-read",
        label: "Mark read",
        icon: Check,
        hidden: Boolean(chapterProgress?.isComplete),
        onClick: markChapterRead,
      },
      {
        id: "restart-chapter",
        label: "Start over",
        icon: RotateCcw,
        hidden: !chapterProgress || chapterProgress.isComplete,
        onClick: () => openReaderAt(0),
      },
    ];
  });
  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    const tabs: EntityDetailTab[] = [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "stats", "positions", "source"],
      },
      {
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        sections: ["dates", "links"],
        layout: "grid",
      },
    ];

    return tabs;
  });

  async function loadChapterSummaries(
    nextBook: EntityCardFull,
    currentChapter: EntityCardFull,
    signal: AbortSignal,
  ): Promise<BookReaderChapter[]> {
    const currentPageCount = orderedBookChildren(currentChapter, ENTITY_KIND.bookPage).length;
    const volumeThumbnails = orderedBookChildren(nextBook, ENTITY_KIND.bookVolume);
    let parentVolumeIndex = volumeThumbnails.findIndex((volume) => volume.id === currentChapter.parentEntityId);
    let currentVolume: EntityCardFull | null = null;

    if (parentVolumeIndex >= 0) {
      currentVolume = await fetchEntity(volumeThumbnails[parentVolumeIndex].id, { signal });
    } else {
      for (const [index, volumeThumbnail] of volumeThumbnails.entries()) {
        const volume = await fetchEntity(volumeThumbnail.id, { signal });
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
          const nextVolumeDetail = await fetchEntity(nextVolume.id, { signal });
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

  function openReaderAt(index: number) {
    if (!book || !chapter) return;
    void goto(resolve(bookReaderRoute({
      bookId: book.id,
      kind: "chapter",
      id: chapter.id,
      returnId: chapter.id,
      pageIndex: Math.max(0, Math.min(index, Math.max(0, readerPageCount - 1))),
    }), { id: book.id }));
  }

  function openPrimaryReader() {
    if (!book || !chapter) return;
    void goto(resolve(bookReaderRoute({
      bookId: book.id,
      kind: "chapter",
      id: chapter.id,
      returnId: chapter.id,
      command: chapterProgress && !chapterProgress.isComplete ? "resume" : undefined,
    }), { id: book.id }));
  }

  function bookReaderSearch(options: BookReaderHrefOptions): `?${string}` {
    const href = bookReaderHref(options);
    return href.slice(href.indexOf("?")) as `?${string}`;
  }

  function bookReaderRoute(options: BookReaderHrefOptions): `/books/[id]/reader?${string}` {
    return `/books/[id]/reader${bookReaderSearch(options)}` as `/books/[id]/reader?${string}`;
  }

  async function markChapterRead() {
    if (!book || !chapter || readerPageCount === 0) return;
    await updateEntityProgress(book.id, {
      currentEntityId: chapter.id,
      unit: PROGRESS_UNIT.page,
      index: readerPageCount - 1,
      total: readerPageCount,
      mode: chapterProgress?.readerMode ?? "paged",
      completed: true,
    });
    await detail.reload({ showLoading: false });
  }
</script>

<svelte:head>
  <title>{chapter?.title ?? "Chapter"} · Prismedia</title>
</svelte:head>

<div class="chapter-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load chapter."
    onRetry={detail.retry}
    tabCount={2}
  >
    {#if card && chapter && book}
      <EntityDetail
        {card}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        tabs={detailTabs}
        actionButtons={heroActions}
      >
        {#snippet heroMeta()}
          <span class="meta-item">{bookTitle}</span>
          <span class="meta-sep"></span>
          <span class="meta-item">
            {readerPageCount} page{readerPageCount === 1 ? "" : "s"}
          </span>
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
          initialMediaWall
          emptyTitle="No pages"
          emptyMessage="No pages found in this chapter."
          onCardActivate={(card, visibleCards) => {
            const index = visibleCards.findIndex((item) => item.entity.id === card.entity.id);
            openReaderAt(Math.max(0, index));
          }}
        />
      </section>
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .chapter-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
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

</style>
