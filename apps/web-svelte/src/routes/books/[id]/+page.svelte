<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, Info, Play, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import { getCapability } from "$lib/api/capabilities";
  import { updateEntityProgress } from "$lib/api/playback";
  import { fetchBook, type BookDetail } from "$lib/api/media";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import {
    updateEntityFlags,
    updateEntityMetadata,
    updateEntityRating,
  } from "$lib/api/entity-mutations";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailTag } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    orderedBookChildren,
    type BookEntityProgressDisplay,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref } from "$lib/entities/book-reader-route";
  import {
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import {
    isHiddenEntityNotFoundError,
    redirectHiddenEntityNotFound,
  } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  interface ChapterDetail {
    detail: EntityCardFull;
    card: EntityThumbnailCard;
    pages: ReturnType<typeof orderedBookChildren>;
    summary: BookReaderChapter;
  }

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let progressBusy = $state(false);
  let chapterDetails = $state.raw<ChapterDetail[]>([]);
  let progressChapterSummary = $state.raw<BookReaderChapter | null>(null);
  let childBookCards = $state<EntityThumbnailCard[]>([]);
  let volumeCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let selectedChapterId: string | null = $state(null);

  const bookId = $derived(page.params.id ?? "");
  const bookType = $derived(book?.bookType ?? null);
  // Single-file books (EPUB/PDF) are read straight from the source file with no chapter entities.
  const isSingleFileBook = $derived(!!book && book.format !== "image-archive");
  const singleFileProgress = $derived(book && isSingleFileBook ? getCapability(book.capabilities, "progress") : null);
  // Started once a position has been saved (EPUB and PDF both set currentEntityId to the book id).
  const singleFileInProgress = $derived(!!singleFileProgress?.currentEntityId && !singleFileProgress?.completedAt);
  const peopleLabel = $derived(bookType === "comic" || bookType === "manga" ? "Artists" : "People");
  const bookTitle = $derived(book?.title ?? "Book");
  const chapterSummaries = $derived(combineChapterSummaries(chapterDetails, progressChapterSummary));
  const chapterCards = $derived(chapterDetails.map((chapter) => chapter.card));
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const selectedChapter = $derived(
    chapterDetails.find((chapter) => chapter.detail.id === selectedChapterId) ?? chapterDetails[0] ?? null,
  );
  const selectedProgress = $derived(
    progressDisplay?.chapterId === selectedChapter?.detail.id ? progressDisplay : null,
  );
  const readerPageCount = $derived(selectedChapter?.pages.length ?? 0);
  // Comics may be organized as volumes with no direct chapters; they are still readable
  // (the reader resolves the first chapter), so the Read button must appear for them too.
  const hasReadableContent = $derived(
    !isSingleFileBook && (readerPageCount > 0 || chapterDetails.length > 0 || volumeCards.length > 0),
  );
  // Started/completed come straight from the progress capability (same source the grid card uses),
  // so the label is correct even for volume-only comics whose in-progress chapter isn't a direct child.
  const comicProgress = $derived(book && !isSingleFileBook ? getCapability(book.capabilities, "progress") : undefined);
  const comicStarted = $derived(!!comicProgress?.currentEntityId);
  const comicCompleted = $derived(!!comicProgress?.completedAt);
  const primaryReadLabel = $derived(
    comicCompleted ? "Re-read" : comicStarted ? "Resume" : "Read",
  );
  const hasCastAndCrew = $derived(studioCards.length > 0 || creditCards.length > 0);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!book) return null;
    return {
      ...entityCardToDetailCard(book),
      tags: relationshipTags,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => card?.entity.id, () => card?.entity.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (identifyAction.action) actions.push(identifyAction.action);
    if (isSingleFileBook) {
      actions.push({
        id: "read-book",
        label: singleFileInProgress ? "Resume" : singleFileProgress?.completedAt ? "Re-read" : "Read",
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: openSingleFileReader,
      });
    } else if (hasReadableContent) {
      actions.push({
        id: "read-book",
        label: primaryReadLabel,
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: openSelectedReader,
      });
    }
    return actions;
  });

  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "cast-and-crew",
      label: peopleLabel,
      icon: Users,
      hidden: !hasCastAndCrew,
    },
  ]);

  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    const tabs: EntityDetailTab[] = [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "tags", "cast-and-crew"],
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
    void loadBook();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadBook();
  });

  $effect(() => {
    if (!book) return;
    return appChrome.setBreadcrumbs([
      { label: "Books", href: "/books" },
      { label: book.title },
    ]);
  });

  async function loadBook() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextBook = await fetchBook(bookId);
      const [relationships, chapters] = await Promise.all([
        hydrateStandardRelationshipCards(nextBook),
        hydrateChapters(nextBook),
      ]);
      const progressSummary = await hydrateProgressChapterSummary(nextBook, chapters);

      book = nextBook;
      chapterDetails = chapters;
      progressChapterSummary = progressSummary;
      childBookCards = thumbnailsToCards(orderedBookChildren(nextBook, ENTITY_KIND.book), {
        hrefFor: (childBook) => `/books/${childBook.id}`,
      });
      volumeCards = thumbnailsToCards(orderedBookChildren(nextBook, ENTITY_KIND.bookVolume), {
        hrefFor: (volume) => `/books/${nextBook.id}/volumes/${volume.id}`,
      });
      studioCards = relationships.studioCards;
      creditCards = relationships.creditCards;
      relationshipTags = relationships.relationshipTags;

      const nextProgress = bookEntityProgressDisplay(nextBook, combineChapterSummaries(chapters, progressSummary));
      selectedChapterId = nextProgress?.chapterId ?? chapters[0]?.detail.id ?? null;
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function hydrateChapters(nextBook: BookDetail): Promise<ChapterDetail[]> {
    const directChapters = orderedBookChildren(nextBook, ENTITY_KIND.bookChapter).map((thumbnail, index) => ({
      thumbnail,
      sortOrder: Number(thumbnail.sortOrder ?? index),
    }));
    const chapterItems = directChapters.sort((a, b) =>
      a.sortOrder - b.sortOrder || a.thumbnail.title.localeCompare(b.thumbnail.title),
    );
    const details = await Promise.all(chapterItems.map((item) => fetchEntity(item.thumbnail.id)));
    return details.map((detail, index) => {
      const item = chapterItems[index];
      const thumbnail = item.thumbnail;
      const pages = orderedBookChildren(detail, ENTITY_KIND.bookPage);
      return {
        detail,
        card: thumbnailsToCards([thumbnail], {
          hrefFor: (item) => `/books/${nextBook.id}/chapters/${item.id}`,
        })[0],
        pages,
        summary: {
          id: detail.id,
          title: detail.title,
          sortOrder: index,
          pageCount: pages.length,
        },
      };
    });
  }

  function combineChapterSummaries(
    chapters: ChapterDetail[],
    progressSummary: BookReaderChapter | null,
  ): BookReaderChapter[] {
    const summaries = chapters.map((chapter) => chapter.summary);
    if (!progressSummary || summaries.some((summary) => summary.id === progressSummary.id)) {
      return summaries;
    }

    return [...summaries, progressSummary];
  }

  async function hydrateProgressChapterSummary(
    nextBook: BookDetail,
    chapters: ChapterDetail[],
  ): Promise<BookReaderChapter | null> {
    const progress = getCapability(nextBook.capabilities, "progress");
    if (!progress?.currentEntityId || chapters.some((chapter) => chapter.detail.id === progress.currentEntityId)) {
      return null;
    }

    let detail: EntityCardFull;
    try {
      detail = await fetchEntity(progress.currentEntityId);
    } catch (err) {
      if (isHiddenEntityNotFoundError(err)) return null;
      throw err;
    }

    if (detail.kind !== ENTITY_KIND.bookChapter) return null;

    const pages = orderedBookChildren(detail, ENTITY_KIND.bookPage);
    const sortOrder = Number(detail.sortOrder ?? chapters.length);
    return {
      id: detail.id,
      title: detail.title,
      sortOrder: Number.isFinite(sortOrder) ? sortOrder : chapters.length,
      pageCount: pages.length,
    };
  }

  async function handleRatingChange(value: number | null) {
    if (!book || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(book, value, (next) => (book = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!book) return;
    await toggleOptimisticEntityFlag(book, "isFavorite", (next) => (book = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!book) return;
    await toggleOptimisticEntityFlag(book, "isOrganized", (next) => (book = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!book) return;
    await updateEntityMetadata(book.id, request, { kind: book.kind });
    await loadBook();
  }

  function openSelectedReader() {
    if (!book) return;
    // In progress: resume where they left off (the reader resolves the saved chapter), regardless
    // of which chapter is selected.
    if (comicStarted && !comicCompleted) {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "book",
        id: book.id,
        returnId: book.id,
        command: "resume",
      }));
      return;
    }
    // Starting fresh (or re-reading): open the selected direct chapter, else the book's first.
    if (selectedChapter) {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "chapter",
        id: selectedChapter.detail.id,
        returnId: book.id,
      }));
      return;
    }
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
    }));
  }

  function openSingleFileReader() {
    if (!book) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
      command: singleFileInProgress ? "resume" : singleFileProgress?.completedAt ? "start-over" : undefined,
    }));
  }

  function resumeProgress() {
    if (!book || !progressDisplay) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
      command: "resume",
    }));
  }

  /** Marks the book read or unread without moving the reading position. Independent of the cursor. */
  async function handleToggleRead(read: boolean) {
    if (!book || !progressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: progressDisplay.chapterId,
        unit: "page",
        index: Math.max(0, progressDisplay.currentPage - 1),
        total: progressDisplay.pageCount,
        mode: progressDisplay.readerMode,
        completed: read,
      });
      await loadBook();
    } catch {
      // best-effort; the panel reflects the last known state on failure
    } finally {
      progressBusy = false;
    }
  }

  /** Resets reading progress to the first page and clears completion (bypasses the forward-only guard). */
  async function startProgressOver() {
    const firstChapter = chapterSummaries[0];
    if (!book || !firstChapter || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: firstChapter.id,
        unit: "page",
        index: 0,
        total: firstChapter.pageCount,
        mode: progressDisplay?.readerMode ?? "paged",
        reset: true,
      });
      await loadBook();
    } catch {
      // best-effort
    } finally {
      progressBusy = false;
    }
  }

</script>

<svelte:head>
  <title>{book?.title ?? "Book"} · Prismedia</title>
</svelte:head>

<div class="book-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load book."}</p>
      <button type="button" onclick={() => void loadBook()}>Retry</button>
    </div>
  {:else if card && book}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      {peopleLabel}
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        {#if bookType}
          <span class="meta-item">{bookType}</span>
        {/if}
        {#if chapterDetails.length > 0}
          <span class="meta-sep"></span>
          <span class="meta-item">
            {chapterDetails.length} chapter{chapterDetails.length === 1 ? "" : "s"}
          </span>
          <span class="meta-sep"></span>
          <span class="meta-item">
            {chapterDetails.reduce((total, chapter) => total + chapter.pages.length, 0)} pages
          </span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if progressDisplay}
          <span class="hero-badge">{progressDisplay.percent}%</span>
        {/if}
      {/snippet}


      {#snippet sectionContent(section)}
        {#if section.id === "cast-and-crew"}
          <EntityCastAndCrewSection {studioCards} {creditCards} castLabel={peopleLabel} />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if progressDisplay}
      <section class="progress-section">
        <MediaProgressPanel
          kind="read"
          completed={progressDisplay.isComplete}
          percent={progressDisplay.percent}
          positionLabel={progressDisplay.workPageLabel ?? progressDisplay.chapterPageLabel ?? progressDisplay.pageLabel}
          countLabel={progressDisplay.chapterLabel}
          canResume={!progressDisplay.isComplete}
          canStartOver
          busy={progressBusy}
          onToggleCompleted={handleToggleRead}
          onResume={resumeProgress}
          onStartOver={startProgressOver}
        />
      </section>
    {/if}

    {#if childBookCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <BookOpen class="h-4 w-4" />
          Books
          <span class="content-count">{childBookCards.length}</span>
        </h2>
        <EntityGrid
          cards={childBookCards}
          prefsKey={`book-${book.id}-books`}
          initialSortBy="position"
          emptyTitle="No books"
          emptyMessage="No books found for this series."
        />
      </section>
    {/if}

    {#if volumeCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <BookOpen class="h-4 w-4" />
          Volumes
          <span class="content-count">{volumeCards.length}</span>
        </h2>
        <EntityGrid
          cards={volumeCards}
          prefsKey={`book-${book.id}-volumes`}
          initialSortBy="position"
          emptyTitle="No volumes"
          emptyMessage="No volumes found for this book."
        />
      </section>
    {/if}

    {#if chapterDetails.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <BookOpen class="h-4 w-4" />
          Chapters
          <span class="content-count">{chapterDetails.length}</span>
        </h2>
        <EntityGrid
          cards={chapterCards}
          prefsKey={`book-${book.id}-chapters`}
          initialSortBy="position"
          emptyTitle="No chapters"
          emptyMessage="No chapters found for this book."
        />
      </section>
    {/if}
  {/if}
</div>

<style>
  .book-page {
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
    border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
    border-radius: var(--radius-xs, 4px);
    background:
      linear-gradient(160deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0)),
      var(--color-overlay-glass-light, rgba(17, 22, 29, 0.55));
    color: var(--color-text-muted, #8a93a6);
    padding: 0.4rem 0.8rem;
    font-size: 0.78rem;
    cursor: pointer;
    box-shadow: var(--shadow-card, 0 2px 6px rgba(0, 0, 0, 0.3));
    transition:
      border-color var(--duration-normal, 180ms) var(--ease-mechanical, ease),
      box-shadow var(--duration-normal, 180ms) var(--ease-mechanical, ease),
      color var(--duration-fast, 100ms) var(--ease-default, ease),
      background var(--duration-normal, 180ms) var(--ease-mechanical, ease);
  }

  .error-notice button:hover {
    color: var(--color-text-accent, #c49a5a);
    border-color: var(--color-border-accent-strong, rgba(242, 194, 106, 0.52));
    box-shadow: var(--shadow-card-hover, 0 8px 24px rgba(0, 0, 0, 0.4));
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

  /* The shared MediaProgressPanel provides its own card surface; this wrapper only
     participates in the page's section gap. */
  .progress-section {
    display: block;
    min-width: 0;
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
