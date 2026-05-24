<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { ArrowLeft, BookOpen, Check, Info, Play, RotateCcw, SlidersHorizontal, Users } from "@lucide/svelte";
  import { getCapability } from "$lib/api/capabilities";
  import {
    fetchBook,
    fetchEntity,
    updateEntityFlags,
    updateEntityMetadata,
    updateEntityRating,
    type BookDetail,
    type EntityCardFull,
  } from "$lib/api/prismedia";
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
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import IdentifyButton from "$lib/components/IdentifyButton.svelte";
  import {
    isHiddenEntityNotFoundError,
    redirectHiddenEntityNotFound,
  } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();

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
  let chapterDetails = $state.raw<ChapterDetail[]>([]);
  let progressChapterSummary = $state.raw<BookReaderChapter | null>(null);
  let volumeCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let selectedChapterId: string | null = $state(null);

  const bookId = $derived(page.params.id ?? "");
  const bookType = $derived(book?.bookType ?? null);
  const bookTitle = $derived(book?.title ?? "Book");
  const chapterSummaries = $derived(combineChapterSummaries(chapterDetails, progressChapterSummary));
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const selectedChapter = $derived(
    chapterDetails.find((chapter) => chapter.detail.id === selectedChapterId) ?? chapterDetails[0] ?? null,
  );
  const selectedProgress = $derived(
    progressDisplay?.chapterId === selectedChapter?.detail.id ? progressDisplay : null,
  );
  const readerPageCount = $derived(selectedChapter?.pages.length ?? 0);
  const primaryReadLabel = $derived(
    selectedProgress ? (selectedProgress.isComplete ? "Re-read" : "Resume") : "Read",
  );
  const hasCastAndCrew = $derived(studioCards.length > 0 || creditCards.length > 0);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!book) return null;
    return {
      ...entityCardToDetailCard(book),
      tags: relationshipTags,
    };
  });

  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "cast-and-crew",
      label: "Cast and Crew",
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
    if (!book || !selectedChapter) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: selectedChapter.detail.id,
      returnId: book.id,
      command: selectedProgress && !selectedProgress.isComplete ? "resume" : undefined,
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

  function startProgressOver() {
    if (!book || !progressDisplay) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
      command: "start-over",
    }));
  }

</script>

<svelte:head>
  <title>{book?.title ?? "Book"} · Prismedia</title>
</svelte:head>

<div class="book-page">
  <a href="/books" class="back-link">
    <ArrowLeft class="h-4 w-4" />
    Books
  </a>

  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true"></div>
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
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
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
          <span class="progress-badge">{progressDisplay.percent}%</span>
        {/if}
      {/snippet}

      {#snippet extraActions()}
        {#if book}
        <IdentifyButton entityId={bookId} />
        {#if readerPageCount > 0}
          <button type="button" class="reader-action" onclick={openSelectedReader}>
            <Play class="h-3.5 w-3.5" />
            {primaryReadLabel}
          </button>
        {/if}
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "cast-and-crew"}
          <EntityCastAndCrewSection {studioCards} {creditCards} />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if progressDisplay}
      <section class="progress-section">
        <button type="button" class="progress-summary" onclick={resumeProgress}>
          <span class="section-kicker">Current chapter</span>
          <strong>{progressDisplay.chapterLabel}</strong>
          <span class="progress-detail-lines">
            {#if progressDisplay.chapterPageLabel}
              <span>{progressDisplay.chapterPageLabel}</span>
            {/if}
            {#if progressDisplay.workPageLabel}
              <span>{progressDisplay.workPageLabel}</span>
            {/if}
          </span>
        </button>
        <span class="progress-percent">{progressDisplay.percent}%</span>
        {#if progressDisplay.showMeter}
          <span class="progress-track" aria-hidden="true">
            <span style:width={`${progressDisplay.percent}%`}></span>
          </span>
        {/if}
        <div class="progress-actions">
          <button type="button" class="reader-action" onclick={resumeProgress}>
            <Play class="h-3.5 w-3.5" />
            Resume
          </button>
          <button type="button" class="reader-action" onclick={startProgressOver}>
            <RotateCcw class="h-3.5 w-3.5" />
            Start over
          </button>
        </div>
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
        <div class="chapter-grid">
          {#each chapterDetails as chapter (chapter.detail.id)}
            <a class="chapter-card" href={`/books/${book.id}/chapters/${chapter.detail.id}`}>
              <div class="chapter-card-body">
                <span class="section-kicker">Ch. {chapter.summary.sortOrder + 1}</span>
                <strong>{chapter.detail.title}</strong>
                <span>
                  {chapter.pages.length} page{chapter.pages.length === 1 ? "" : "s"}
                </span>
              </div>
            </a>
          {/each}
        </div>
      </section>
    {:else}
      <div class="empty-children">
        <p>No chapters linked to this book yet.</p>
      </div>
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

  .progress-badge {
    display: inline-flex;
    align-items: center;
    padding: 0.15rem 0.5rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: var(--color-text-accent, #c49a5a);
    border: 1px solid rgba(196, 154, 90, 0.35);
    background: rgba(196, 154, 90, 0.08);
  }

  .progress-section {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 0.85rem;
    padding: 1rem;
    text-align: left;
    border: 1px solid var(--color-border-subtle, #1c2235);
    background: var(--color-glass-1, rgba(12, 15, 21, 0.72));
    color: var(--color-text-muted, #8a93a6);
    backdrop-filter: blur(14px);
  }

  .progress-summary {
    min-width: 0;
    border: 0;
    padding: 0;
    background: transparent;
    color: inherit;
    text-align: left;
    cursor: pointer;
  }

  .progress-summary:hover strong {
    color: var(--color-text-accent, #c49a5a);
  }

  .progress-detail-lines {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem 0.8rem;
    margin-top: 0.4rem;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.86rem;
  }

  .progress-detail-lines span {
    white-space: nowrap;
  }

  .progress-section strong,
  .chapter-card strong {
    display: block;
    color: var(--color-text-primary, #f2eed8);
    font-size: 0.92rem;
    line-height: 1.35;
  }

  .section-kicker {
    display: block;
    margin-bottom: 0.25rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--color-text-disabled, #5f687a);
  }

  .progress-percent {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    color: var(--color-text-accent, #c49a5a);
  }

  .progress-track {
    grid-column: 1 / -1;
    display: block;
    height: 4px;
    border: 1px solid rgb(255 255 255 / 0.1);
    background: rgb(0 0 0 / 0.4);
  }

  .progress-track span {
    display: block;
    height: 100%;
    background: linear-gradient(to right, #7a5228, #c49a5a, #f3d69c);
    box-shadow: 0 0 14px rgb(196 154 90 / 0.55);
  }

  .progress-actions {
    grid-column: 1 / -1;
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
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

  .chapter-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr));
    gap: 0.75rem;
  }

  .chapter-card {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: start;
    gap: 0.75rem;
    min-height: 6.5rem;
    padding: 0.9rem;
    border: 1px solid var(--color-border-subtle, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    text-decoration: none;
    transition: border-color 0.15s, box-shadow 0.15s;
  }

  .chapter-card:hover {
    border-color: rgba(196, 154, 90, 0.42);
    box-shadow: 0 0 18px rgb(196 154 90 / 0.12);
  }

  .chapter-card-body {
    display: grid;
    gap: 0.35rem;
    min-width: 0;
  }

  .empty-children {
    padding: 2rem;
    border: 1px solid var(--color-border-subtle, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    text-align: center;
    font-size: 0.85rem;
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
