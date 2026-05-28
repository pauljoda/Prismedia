<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, Check, Images, Info, Play, RotateCcw, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchBook, type BookDetail } from "$lib/api/media";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityMetadata } from "$lib/api/entity-mutations";
  import { updateEntityProgress } from "$lib/api/playback";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    orderedBookChildren,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref } from "$lib/entities/book-reader-route";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  let chapter = $state<EntityCardFull | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let pageCards = $state<EntityThumbnailCard[]>([]);
  let chapterSummaries = $state.raw<BookReaderChapter[]>([]);

  const bookId = $derived(page.params.id ?? "");
  const chapterId = $derived(page.params.chapterId ?? "");
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

  $effect(() => {
    if (!book || !chapter) return;
    return appChrome.setBreadcrumbs([
      { label: "Books", href: "/books" },
      { label: book.title, href: `/books/${book.id}` },
      { label: chapter.title },
    ]);
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
    if (!book || !chapter) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: chapter.id,
      returnId: chapter.id,
      pageIndex: Math.max(0, Math.min(index, Math.max(0, readerPageCount - 1))),
    }));
  }

  function openPrimaryReader() {
    if (!book || !chapter) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: chapter.id,
      returnId: chapter.id,
      command: chapterProgress && !chapterProgress.isComplete ? "resume" : undefined,
    }));
  }

  async function markChapterRead() {
    if (!book || !chapter || readerPageCount === 0) return;
    await updateEntityProgress(book.id, {
      currentEntityId: chapter.id,
      unit: "page",
      index: readerPageCount - 1,
      total: readerPageCount,
      mode: chapterProgress?.readerMode ?? "paged",
      completed: true,
    });
    await loadChapter();
  }
</script>

<svelte:head>
  <title>{chapter?.title ?? "Chapter"} · Prismedia</title>
</svelte:head>

<div class="chapter-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
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
</div>

<style>
  .chapter-page {
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
