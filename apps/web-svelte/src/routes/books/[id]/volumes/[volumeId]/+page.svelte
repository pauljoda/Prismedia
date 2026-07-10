<script lang="ts">
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, Check, CloudDownload, Info, Play, RotateCcw, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchBook, type BookDetail } from "$lib/api/media";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityMetadata } from "$lib/api/entity-mutations";
  import { updateEntityProgress } from "$lib/api/playback";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import {
    bookEntityProgressDisplay,
    entityPageToReaderImage,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref } from "$lib/entities/book-reader-route";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  let volume = $state<EntityCardFull | null>(null);
  let chapterDetails = $state.raw<EntityCardFull[]>([]);
  let chapterCards = $state<EntityThumbnailCard[]>([]);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);

  const bookId = $derived(page.params.id ?? "");
  const volumeId = $derived(page.params.volumeId ?? "");
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
    onChanged: loadVolume,
    onPruned: () => goto(`/books/${bookId}`),
  });
  const fileManagement = {
    onDeleted: () => goto(`/books/${bookId}`),
    onReverted: () => refreshAfterManagedFileRevert(acq, loadVolume),
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

  onMount(() => {
    void loadVolume();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadVolume();
  });

  $effect(() => {
    if (!book || !volume) return;
    return appChrome.setBreadcrumbs([
      { label: "Books", href: "/books" },
      { label: book.title, href: `/books/${book.id}` },
      { label: volume.title },
    ]);
  });

  async function loadVolume() {
    loadState = "loading";
    errorMessage = null;
    try {
      const [nextBook, nextVolume] = await Promise.all([
        fetchBook(bookId),
        fetchEntity(volumeId),
      ]);
      const chapterThumbnails = orderedBookChildren(nextVolume, ENTITY_KIND.bookChapter);
      const details = await Promise.all(chapterThumbnails.map((chapter) => fetchEntity(chapter.id)));
      book = nextBook;
      volume = nextVolume;
      chapterDetails = details;
      chapterCards = thumbnailsToCards(chapterThumbnails, {
        hrefFor: (chapter) => `/books/${nextBook.id}/chapters/${chapter.id}`,
      });
      const nextProgress = bookEntityProgressDisplay(nextBook, details.map((detail, index) => ({
        id: detail.id,
        title: detail.title,
        sortOrder: Number(detail.sortOrder ?? index),
        pageCount: orderedBookChildren(detail, ENTITY_KIND.bookPage).length,
      })));
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!volume) return;
    await updateEntityMetadata(volume.id, request, { kind: volume.kind });
    await loadVolume();
  }

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
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "volume",
      id: volume.id,
      returnId: volume.id,
      command: index == null && volumeProgress && !volumeProgress.isComplete ? "resume" : undefined,
      pageIndex: index == null ? undefined : Math.max(0, Math.min(index, Math.max(0, readerPages.length - 1))),
    }));
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
    await loadVolume();
  }
</script>

<svelte:head>
  <title>{volume?.title ?? "Volume"} · Prismedia</title>
</svelte:head>

<div class="volume-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load volume."}</p>
      <button type="button" onclick={() => void loadVolume()}>Retry</button>
    </div>
  {:else if card && volume && book}
    <EntityDetail
      {card}
      onMetadataSave={handleMetadataSave}
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
</div>

<style>
  .volume-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
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

  .error-notice button {
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

</style>
