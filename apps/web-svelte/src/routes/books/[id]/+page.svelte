<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, Check, Info, Play, RotateCcw, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { getCapability } from "$lib/api/capabilities";
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
  let chapterDetails = $state.raw<ChapterDetail[]>([]);
  let progressChapterSummary = $state.raw<BookReaderChapter | null>(null);
  let volumeCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let selectedChapterId: string | null = $state(null);

  const bookId = $derived(page.params.id ?? "");
  const bookType = $derived(book?.bookType ?? null);
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

  const identifyAction = useIdentifyDetailAction(() => card?.entity.id, () => card?.entity.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (identifyAction.action) actions.push(identifyAction.action);
    if (readerPageCount > 0) {
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
          <button type="button" class="reader-action primary" onclick={resumeProgress}>
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

  .error-notice button,
  .reader-action {
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

  .reader-action:hover,
  .error-notice button:hover {
    color: var(--color-text-accent, #c49a5a);
    border-color: var(--color-border-accent-strong, rgba(242, 194, 106, 0.52));
    box-shadow: var(--shadow-card-hover, 0 8px 24px rgba(0, 0, 0, 0.4));
  }

  .reader-action.primary {
    color: var(--color-text-accent-bright, #f5d48a);
    border-color: var(--color-border-accent-strong, rgba(242, 194, 106, 0.52));
    background:
      linear-gradient(135deg, rgba(122, 94, 32, 0.28), rgba(242, 194, 106, 0.08)),
      var(--color-overlay-glass-accent, rgba(36, 30, 18, 0.6));
    box-shadow: var(--shadow-glow-accent, 0 0 25px rgba(242, 194, 106, 0.1));
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

  .progress-section {
    position: relative;
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 0.75rem 1rem;
    overflow: hidden;
    padding: 1rem 1.25rem;
    text-align: left;
    border: 1px solid var(--color-border-default, rgba(164, 172, 185, 0.12));
    border-radius: var(--radius-md, 10px);
    background:
      linear-gradient(145deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0) 32%),
      var(--color-surface-2);
    color: var(--color-text-muted, #8a93a6);
    box-shadow: var(--shadow-elevated);
  }

  .progress-section::before {
    position: absolute;
    inset: 0;
    pointer-events: none;
    content: "";
    border-radius: inherit;
    box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.06);
  }

  .progress-summary {
    position: relative;
    min-width: 0;
    border: 0;
    padding: 0;
    background: transparent;
    color: inherit;
    text-align: left;
    cursor: pointer;
  }

  .progress-summary:hover strong {
    color: var(--color-text-accent-bright, #f5d48a);
  }

  .progress-summary:focus-visible {
    outline: 2px solid rgba(242, 194, 106, 0.5);
    outline-offset: 0.35rem;
  }

  .progress-detail-lines {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem 0.8rem;
    margin-top: 0.4rem;
    color: var(--color-text-secondary, #c8ccd4);
    font-size: 0.86rem;
  }

  .progress-detail-lines span {
    white-space: nowrap;
  }

  .progress-section strong {
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
    position: relative;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.95rem;
    font-weight: 700;
    color: var(--color-text-accent-bright, #f5d48a);
  }

  .progress-track {
    position: relative;
    grid-column: 1 / -1;
    display: block;
    height: 3px;
    overflow: hidden;
    border-radius: var(--radius-xs, 4px);
    background: rgba(0, 0, 0, 0.35);
    box-shadow: var(--shadow-well, inset 0 1px 3px rgba(0, 0, 0, 0.35));
  }

  .progress-track span {
    display: block;
    height: 100%;
    border-radius: inherit;
    background: linear-gradient(135deg, #7a5e20 0%, #d59a2a 58%, #f2c26a 100%);
  }

  .progress-actions {
    position: relative;
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



</style>
