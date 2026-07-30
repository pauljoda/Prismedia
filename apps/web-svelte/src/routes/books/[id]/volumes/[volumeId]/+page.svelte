<script lang="ts">
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { BookOpen, Check, CloudDownload, Info, Play, RotateCcw, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityProgress } from "$lib/api/playback";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import {
    bookEntityProgressDisplay,
    entityPageToReaderImage,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref, type BookReaderHrefOptions } from "$lib/entities/book-reader-route";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";

  let book = $state<EntityCardFull | null>(null);
  let chapterDetails = $state.raw<EntityCardFull[]>([]);
  let chapterCards = $state<EntityThumbnailCard[]>([]);

  const bookId = $derived(page.params.id ?? "");
  const volumeId = $derived(page.params.volumeId ?? "");
  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${bookId}:${volumeId}`,
    load: async ({ signal }) => {
      const [nextBook, nextVolume] = await Promise.all([
        fetchEntity(bookId, { signal }),
        fetchEntity(volumeId, { signal }),
      ]);
      const chapterThumbnails = orderedBookChildren(nextVolume, ENTITY_KIND.bookChapter);
      const details = await Promise.all(chapterThumbnails.map((chapter) => fetchEntity(chapter.id, { signal })));
      signal.throwIfAborted();
      book = nextBook;
      chapterDetails = details;
      chapterCards = thumbnailsToCards(chapterThumbnails, {
        hrefFor: (chapter) => `/books/${nextBook.id}/chapters/${chapter.id}`,
      });
      return nextVolume;
    },
    breadcrumbs: (currentVolume) => [
      { label: "Books", href: "/books" },
      { label: book?.title ?? "Book", href: `/books/${bookId}` },
      { label: currentVolume.title },
    ],
  });
  const volume = $derived(detail.entity);
  const bookTitle = $derived(book?.title ?? "Book");
  const card = $derived(volume ? entityCardToDetailCard(volume) as EntityDetailCardFull : null);
  const chapterSummaries = $derived(chapterDetails.map((chapter, index): BookReaderChapter => ({
    id: chapter.id,
    title: chapter.title,
    sortOrder: Number(chapter.sortOrder ?? index),
    pageCount: orderedBookChildren(chapter, ENTITY_KIND.bookPage).length,
  })));
  const volumePages = $derived(
    chapterDetails.flatMap((chapter) => orderedBookChildren(chapter, ENTITY_KIND.bookPage)),
  );
  const readerPages = $derived(volumePages.map(entityPageToReaderImage));
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const progressChapterIndex = $derived(
    progressDisplay ? chapterDetails.findIndex((chapter) => chapter.id === progressDisplay.chapterId) : -1,
  );
  const volumeProgress = $derived(progressChapterIndex >= 0 ? progressDisplay : null);
  const primaryReadLabel = $derived(
    volumeProgress ? (volumeProgress.isComplete ? "Re-read volume" : "Resume volume") : "Read volume",
  );
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    if (readerPages.length === 0) return [];
    return [
      {
        id: "read-volume",
        label: primaryReadLabel,
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: () => openReaderAt(),
      },
      {
        id: "mark-volume-read",
        label: "Mark read",
        icon: Check,
        hidden: Boolean(volumeProgress?.isComplete),
        onClick: markVolumeRead,
      },
      {
        id: "restart-volume",
        label: "Start over",
        icon: RotateCcw,
        hidden: !volumeProgress || volumeProgress.isComplete,
        onClick: () => openReaderAt(0),
      },
    ];
  });
  const acq = useEntityAcquisition({
    entityId: () => volume?.id,
    capabilities: () => volume?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto(resolve(`/books/${bookId}`)),
  });
  const fileManagement = {
    onDeleted: () => goto(resolve(`/books/${bookId}`)),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "acquisition" },
  ]);
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
      ...(acq.visible
        ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
        : []),
    ];

    return tabs;
  });

  function positionForReaderIndex(index: number) {
    let offset = 0;
    for (const chapter of chapterDetails) {
      const pages = orderedBookChildren(chapter, ENTITY_KIND.bookPage);
      const nextOffset = offset + pages.length;
      if (index < nextOffset) {
        return { chapter, pageIndex: index - offset, pageCount: pages.length };
      }
      offset = nextOffset;
    }
    const chapter = chapterDetails.at(-1) ?? null;
    return {
      chapter,
      pageIndex: Math.max(0, (chapter ? orderedBookChildren(chapter, ENTITY_KIND.bookPage).length : 1) - 1),
      pageCount: chapter ? orderedBookChildren(chapter, ENTITY_KIND.bookPage).length : 0,
    };
  }

  function openReaderAt(index?: number) {
    if (!book || !volume) return;
    void goto(resolve(bookReaderRoute({
      bookId: book.id,
      kind: "volume",
      id: volume.id,
      returnId: volume.id,
      command: index == null && volumeProgress && !volumeProgress.isComplete ? "resume" : undefined,
      pageIndex: index == null ? undefined : Math.max(0, Math.min(index, Math.max(0, readerPages.length - 1))),
    }), { id: book.id }));
  }

  function bookReaderSearch(options: BookReaderHrefOptions): `?${string}` {
    const href = bookReaderHref(options);
    return href.slice(href.indexOf("?")) as `?${string}`;
  }

  function bookReaderRoute(options: BookReaderHrefOptions): `/books/[id]/reader?${string}` {
    return `/books/[id]/reader${bookReaderSearch(options)}` as `/books/[id]/reader?${string}`;
  }

  async function saveProgress(index: number, completed = false) {
    if (!book || readerPages.length === 0) return;
    const position = positionForReaderIndex(index);
    if (!position.chapter) return;
    await updateEntityProgress(book.id, {
      currentEntityId: position.chapter.id,
      unit: PROGRESS_UNIT.page,
      index: position.pageIndex,
      total: position.pageCount,
      mode: volumeProgress?.readerMode ?? "paged",
      // Mid-reading sends null; only the explicit end-of-volume save reports completion.
      completed: completed ? true : null,
    });
  }

  async function markVolumeRead() {
    if (readerPages.length === 0) return;
    await saveProgress(Math.max(0, readerPages.length - 1), true);
    await detail.reload({ showLoading: false });
  }
</script>

<svelte:head>
  <title>{volume?.title ?? "Volume"} · Prismedia</title>
</svelte:head>

<div class="volume-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load volume."
    onRetry={detail.retry}
    tabCount={3}
  >
    {#if card && volume && book}
      <EntityDetail
        {card}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        tabs={detailTabs}
        sections={detailSections}
        actionButtons={heroActions}
      >
        {#snippet heroMeta()}
          <span class="meta-item">{bookTitle}</span>
          <span class="meta-sep"></span>
          <span class="meta-item">{chapterDetails.length} chapters</span>
          <span class="meta-sep"></span>
          <span class="meta-item">{readerPages.length} pages</span>
        {/snippet}

        {#snippet sectionContent(section)}
          {#if section.id === "acquisition"}
            <EntityAcquisitionCard {acq} entity={volume} {fileManagement} />
          {/if}
        {/snippet}
      </EntityDetail>

      <section class="content-section">
        <h2 class="content-heading">
          <BookOpen class="h-4 w-4" />
          Chapters
          <span class="content-count">{chapterCards.length}</span>
        </h2>
        <EntityGrid
          cards={chapterCards}
          prefsKey={`book-${book.id}-volume-${volume.id}-chapters`}
          initialSortBy="position"
          emptyTitle="No chapters"
          emptyMessage="No chapters found in this volume."
        />
      </section>
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .volume-page {
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
