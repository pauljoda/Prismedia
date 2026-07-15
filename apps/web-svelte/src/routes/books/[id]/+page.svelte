<script lang="ts">
  import { CAPABILITY_KIND, PROGRESS_UNIT, type BookRenditionCode } from "$lib/api/generated/codes";
  import { onMount } from "svelte";
  import { afterNavigate, goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Headphones, Info, Play, SlidersHorizontal, Users } from "@lucide/svelte";
  import { formatDuration } from "@prismedia/contracts";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import BookRenditionAcquisitionCard from "$lib/components/acquisitions/BookRenditionAcquisitionCard.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { getCapability, isWanted } from "$lib/api/capabilities";
  import { fetchAcquisitionsForEntity } from "$lib/api/acquisitions";
  import { fetchEntityMonitors, resumeMonitor, stopMonitor } from "$lib/api/monitors";
  import { commitEntityRequest } from "$lib/api/requests";
  import { updateEntityPlayback, updateEntityProgress } from "$lib/api/playback";
  import { fetchBook, type BookDetail } from "$lib/api/media";
  import { BookFormat, type AcquisitionDetail, type MonitorView } from "$lib/api/generated/model";
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
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    orderedBookChildren,
    singleFileBookProgressDisplay,
    type BookEntityProgressDisplay,
    type BookReaderChapter,
  } from "$lib/entities/book-entity-reader";
  import { bookReaderHref } from "$lib/entities/book-reader-route";
  import {
    audiobookAbsoluteTime,
    audiobookDuration,
    audiobookTrackItems,
    resolveAudiobookResume,
  } from "$lib/entities/audiobook-playback";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import BookChapterList from "$lib/components/books/BookChapterList.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import {
    isHiddenEntityNotFoundError,
    redirectHiddenEntityNotFound,
  } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome, type AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";
  import { numberValue } from "$lib/utils/format";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import type { ArtworkPalette } from "$lib/entities/artwork-palette";
  import {
    buildBookChapterRows,
    type BookChapterRow,
    type ReadableBookChapter,
  } from "$lib/entities/book-chapter-list";
  import {
    loadEpubContents,
    type EpubContentsEntry,
  } from "$lib/entities/epub-contents";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import { monitorIsActive } from "$lib/requests/monitor-status";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playback = useAudioPlayback()!;

  interface ChapterDetail {
    detail: EntityCardFull;
    pages: ReturnType<typeof orderedBookChildren>;
    summary: BookReaderChapter;
  }

  let loadState: LoadState = $state("loading");
  let book = $state<BookDetail | null>(null);
  // The acquisition backing this book (wanted placeholder still searching/downloading, or the import
  // that produced it), so its state is managed right here instead of only under /request.
  // The book's parent author grouping, when scanned under an Author/ folder, for a breadcrumb back-link.
  let authorLink = $state<{ id: string; title: string } | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let progressBusy = $state(false);
  let listeningBusy = $state(false);
  let chapterDetails = $state.raw<ChapterDetail[]>([]);
  let progressChapterSummary = $state.raw<BookReaderChapter | null>(null);
  let childBookCards = $state<EntityThumbnailCard[]>([]);
  let volumeCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let bookRenditionAcquisitions = $state.raw<AcquisitionDetail[]>([]);
  let bookRenditionMonitors = $state.raw<MonitorView[]>([]);
  let selectedChapterId: string | null = $state(null);
  let epubContents = $state.raw<EpubContentsEntry[]>([]);
  let currentEpubChapterId = $state<string | null>(null);
  let epubContentsLoading = $state(false);
  let artworkPalette = $state.raw<ArtworkPalette | null>(null);
  let loadedBookId: string | null = null;
  let loadedEpubKey: string | null = null;
  let epubContentsAbort: AbortController | null = null;
  let loadToken = 0;

  const bookId = $derived(page.params.id ?? "");
  const bookType = $derived(book?.bookType ?? null);
  // A wanted placeholder has metadata but no file yet; reading is offered only once the file lands.
  // Its acquisition/monitoring surface is the Acquisition detail tab.
  const entityWanted = $derived(!!book && isWanted(book.capabilities));
  // Single-file books (EPUB/PDF) are read straight from the source file with no chapter entities.
  const isSingleFileBook = $derived(
    !!book && (book.format === BookFormat.epub || book.format === BookFormat.pdf),
  );
  const singleFileProgress = $derived(book && isSingleFileBook ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : null);
  // Started once a position has been saved (EPUB and PDF both set currentEntityId to the book id).
  const singleFileInProgress = $derived(!!singleFileProgress?.currentEntityId && !singleFileProgress?.completedAt);
  // Single-file books have no chapter entities, so they need their own progress-panel display.
  const singleFileProgressDisplay = $derived(isSingleFileBook ? singleFileBookProgressDisplay(book) : null);
  const peopleLabel = $derived(bookType === "comic" || bookType === "manga" ? "Artists" : "People");
  const defaultCreditRole = $derived(
    bookType === "comic" || bookType === "manga" ? CREDIT_ROLE.artist : CREDIT_ROLE.writer,
  );
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
  // Started/completed come straight from the progress capability (same source the grid card uses),
  // so the label is correct even for volume-only comics whose in-progress chapter isn't a direct child.
  const comicProgress = $derived(book && !isSingleFileBook ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : undefined);
  const comicStarted = $derived(!!comicProgress?.currentEntityId);
  const comicCompleted = $derived(!!comicProgress?.completedAt);
  const primaryReadLabel = $derived(
    comicCompleted ? "Re-read" : comicStarted ? "Resume" : "Read",
  );
  const audiobookTracks = $derived(book ? audiobookTrackItems(book) : []);
  const audiobookTotalSeconds = $derived(audiobookDuration(audiobookTracks));
  const audiobookPlayback = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.playback) : undefined,
  );
  const isCurrentAudiobook = $derived(
    playback.context?.playbackOwnerEntityId === book?.id &&
      playback.context?.playbackOwnerEntityKind === ENTITY_KIND.book,
  );
  const audiobookResumeSeconds = $derived.by(() => {
    const savedSeconds = numberValue(audiobookPlayback?.resumeSeconds) ?? 0;
    const currentTrack = playback.currentTrack;
    const seconds = isCurrentAudiobook && currentTrack
      ? audiobookAbsoluteTime(audiobookTracks, currentTrack.id, playback.currentTime)
      : savedSeconds;
    return Math.max(0, Math.min(seconds, audiobookTotalSeconds));
  });
  const audiobookCompleted = $derived(Boolean(audiobookPlayback?.completedAt));
  const audiobookPercent = $derived(
    audiobookCompleted
      ? 100
      : audiobookTotalSeconds > 0
        ? Math.round((audiobookResumeSeconds / audiobookTotalSeconds) * 100)
        : 0,
  );
  const audiobookPositionLabel = $derived(
    audiobookResumeSeconds > 0 && audiobookTotalSeconds > 0
      ? `${formatDuration(audiobookResumeSeconds) ?? "0:00"} / ${formatDuration(audiobookTotalSeconds) ?? "0:00"}`
      : null,
  );
  const savedAudiobookResume = $derived(
    audiobookCompleted || audiobookResumeSeconds <= 0 || audiobookTotalSeconds <= 0
      ? null
      : resolveAudiobookResume(audiobookTracks, audiobookResumeSeconds),
  );
  const currentAudiobookTrackId = $derived(
    isCurrentAudiobook ? playback.currentTrack?.id ?? savedAudiobookResume?.trackId ?? null : savedAudiobookResume?.trackId ?? null,
  );
  const readableChapters = $derived.by((): ReadableBookChapter[] => {
    if (book?.format === BookFormat.epub) {
      return epubContents.map((entry) => ({
        id: entry.id,
        title: entry.title,
        order: entry.order,
        depth: entry.depth,
        target: { kind: "epub", location: entry.location },
      }));
    }
    return chapterDetails.map((chapter, index) => ({
      id: chapter.detail.id,
      title: chapter.detail.title,
      order: index,
      depth: 0,
      target: { kind: "entity-chapter", chapterId: chapter.detail.id },
    }));
  });
  const chapterRows = $derived(buildBookChapterRows({
    readableChapters,
    audioTracks: audiobookTracks,
    currentReadableId: book?.format === BookFormat.epub
      ? currentEpubChapterId
      : progressDisplay?.isComplete
        ? null
        : progressDisplay?.chapterId ?? null,
    currentAudioTrackId: currentAudiobookTrackId,
  }));
  const fallbackBookPalette = entityAccentForKind(ENTITY_KIND.book);
  const chapterPalette = $derived(artworkPalette ?? {
    primary: fallbackBookPalette.primary,
    secondary: fallbackBookPalette.secondary,
    background: "#000000",
  });
  const chapterReadingProgressLabel = $derived(
    singleFileProgressDisplay
      ? `${singleFileProgressDisplay.percent}% of book`
      : progressDisplay?.chapterPageLabel ?? progressDisplay?.pageLabel ?? null,
  );
  const chapterListeningProgressLabel = $derived(
    audiobookPositionLabel ?? (currentAudiobookTrackId ? "Current part" : null),
  );
  const hasReadableContent = $derived(
    isSingleFileBook ||
      (book?.format === BookFormat["image-archive"] &&
        (readerPageCount > 0 || chapterDetails.length > 0 || volumeCards.length > 0)),
  );
  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!book) return null;
    return {
      ...entityCardToDetailCard(book),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => book);

  // Wanted/tracking state lives on the entity itself: search, releases, live download, monitoring,
  // cancel — one Acquisition detail tab, absent entirely for an ordinary owned book.
  const acq = useEntityAcquisition({
    entityId: () => book?.id,
    capabilities: () => book?.capabilities,
    childCards: () => requestableDirectChildCards(book?.id, childBookCards),
    onChanged: handleBookAcquisitionChanged,
    onPruned: () => goto("/books"),
  });
  const fileManagement = {
    onDeleted: () => goto("/books"),
    onReverted: () => refreshAfterManagedFileRevert(
      acq,
      () => loadBook(bookId, { showLoading: false }),
    ),
  };

  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (identifyAction.action) actions.push(identifyAction.action);
    if (entityWanted) {
      // No file yet — the Acquisition tab owns the actionable state (search for release,
      // release picker, live download, monitoring, cancel).
      return actions;
    }
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
    if (audiobookTracks.length > 0) {
      actions.push({
        id: "listen-book",
        label: isCurrentAudiobook && playback.playing
          ? "Pause"
          : audiobookResumeSeconds > 0 && !audiobookCompleted
            ? "Continue listening"
            : audiobookCompleted
              ? "Listen again"
              : "Listen",
        icon: Headphones,
        variant: hasReadableContent ? "default" : "primary",
        onClick: listenToBook,
      });
    }
    return actions;
  });

  // Built-in sections come from EntityDetail's core catalog; only label overrides
  // are declared here.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "credits",
      label: peopleLabel,
      icon: Users,
    },
    { id: "acquisition" },
  ]);

  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    return [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "tags", "studio", "credits"],
      },
      {
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        sections: ["stats", "dates", "classification", "source", "links"],
        layout: "grid",
      },
      { id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] },
    ];
  });

  onMount(() => {
    loadCurrentBookIfNeeded();
    return () => epubContentsAbort?.abort();
  });

  afterNavigate(() => {
    loadCurrentBookIfNeeded();
  });

  function loadCurrentBookIfNeeded() {
    if (!bookId || bookId === loadedBookId) return;
    loadedBookId = bookId;
    void loadBook(bookId);
  }

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadBook();
  });

  $effect(() => {
    if (!bookRenditionAcquisitions.some((item) => acquisitionStatusShouldPoll(item.summary.status))) return;
    const timer = setInterval(() => void refreshBookAcquisitionState().catch(() => {}), 5000);
    return () => clearInterval(timer);
  });

  $effect(() => {
    if (!book) return;
    const crumbs: AppBreadcrumb[] = [{ label: "Books", href: "/books" }];
    // When the book sits under an author, surface it ("Books / Andy Weir / Project Hail Mary").
    if (authorLink) {
      crumbs.push({ label: authorLink.title, href: resolveEntityHref("book-author", authorLink.id) });
    }
    crumbs.push({ label: book.title });
    return appChrome.setBreadcrumbs(crumbs);
  });

  async function loadBook(targetBookId = bookId, options = { showLoading: true }) {
    const token = ++loadToken;
    // Silent for acquisition-driven refreshes: update in place instead of flashing the skeleton.
    if (options.showLoading || !book) loadState = "loading";
    errorMessage = null;
    try {
      const [nextBook, nextAcquisitions, nextMonitors] = await Promise.all([
        fetchBook(targetBookId),
        fetchAcquisitionsForEntity(targetBookId).catch(() => []),
        fetchEntityMonitors(targetBookId).catch(() => []),
      ]);
      const parentId = nextBook.parentEntityId;
      const [relationships, chapters, parentThumbs] = await Promise.all([
        hydrateStandardRelationshipCards(nextBook),
        hydrateChapters(nextBook),
        parentId ? fetchOrderedEntityThumbnails([parentId]) : Promise.resolve([]),
      ]);
      const progressSummary = await hydrateProgressChapterSummary(nextBook, chapters);
      if (token !== loadToken) return;

      if (book?.id !== nextBook.id) {
        epubContents = [];
        currentEpubChapterId = null;
        loadedEpubKey = null;
        artworkPalette = null;
      }

      // A book scanned under an Author/ folder is parented to a book-author; surface it as a back-link.
      const authorThumb = parentThumbs.find((thumbnail) => thumbnail.kind === ENTITY_KIND.bookAuthor);
      authorLink = authorThumb ? { id: authorThumb.id, title: authorThumb.title } : null;

      book = nextBook;
      chapterDetails = chapters;
      progressChapterSummary = progressSummary;
      childBookCards = thumbnailsToCards(orderedBookChildren(nextBook, ENTITY_KIND.book), {
        hrefFor: (childBook) => `/books/${childBook.id}`,
      });
      volumeCards = thumbnailsToCards(orderedBookChildren(nextBook, ENTITY_KIND.bookVolume), {
        hrefFor: (volume) => `/books/${nextBook.id}/volumes/${volume.id}`,
      });
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      bookRenditionAcquisitions = nextAcquisitions;
      bookRenditionMonitors = nextMonitors;

      const nextProgress = bookEntityProgressDisplay(nextBook, combineChapterSummaries(chapters, progressSummary));
      selectedChapterId = nextProgress?.chapterId ?? chapters[0]?.detail.id ?? null;
      loadState = "ready";
      void hydrateEpubContents(nextBook, token);
    } catch (err) {
      if (token !== loadToken) return;
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function hydrateEpubContents(nextBook: BookDetail, token: number): Promise<void> {
    epubContentsAbort?.abort();
    if (nextBook.format !== BookFormat.epub) {
      epubContents = [];
      currentEpubChapterId = null;
      epubContentsLoading = false;
      loadedEpubKey = null;
      return;
    }

    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
    const currentLocation = progress?.completedAt ? null : progress?.location;
    const key = `${nextBook.id}:${currentLocation ?? ""}`;
    if (key === loadedEpubKey && epubContents.length > 0) return;

    const controller = new AbortController();
    epubContentsAbort = controller;
    epubContentsLoading = true;
    try {
      const contents = await loadEpubContents(
        `/entities/${nextBook.id}/files/source`,
        currentLocation,
        controller.signal,
      );
      if (controller.signal.aborted || token !== loadToken || bookId !== nextBook.id) return;
      epubContents = contents.entries;
      currentEpubChapterId = contents.currentChapterId;
      loadedEpubKey = key;
    } catch (error) {
      if (controller.signal.aborted || (error instanceof DOMException && error.name === "AbortError")) return;
      if (token !== loadToken || bookId !== nextBook.id) return;
      epubContents = [];
      currentEpubChapterId = null;
    } finally {
      if (epubContentsAbort === controller) epubContentsAbort = null;
      if (token === loadToken) epubContentsLoading = false;
    }
  }

  async function refreshBookAcquisitionState(): Promise<void> {
    const targetBookId = bookId;
    if (!targetBookId) return;
    const [nextAcquisitions, nextMonitors] = await Promise.all([
      fetchAcquisitionsForEntity(targetBookId),
      fetchEntityMonitors(targetBookId),
    ]);
    if (bookId !== targetBookId) return;
    bookRenditionAcquisitions = nextAcquisitions;
    bookRenditionMonitors = nextMonitors;
    await acq.refresh();
  }

  async function handleBookAcquisitionChanged(): Promise<void> {
    await Promise.all([
      loadBook(bookId, { showLoading: false }),
      refreshBookAcquisitionState(),
    ]);
  }

  async function requestBookRendition(rendition: BookRenditionCode): Promise<void> {
    if (!book) return;
    await commitEntityRequest(book.id, rendition);
    await refreshBookAcquisitionState().catch(() => {});
  }

  async function toggleBookRenditionMonitor(monitor: MonitorView): Promise<void> {
    if (monitorIsActive(monitor)) {
      const outcome = await stopMonitor(monitor.id);
      if (outcome.entityPruned) {
        await goto("/books");
        return;
      }
    } else {
      await resumeMonitor(monitor.id);
    }
    await refreshBookAcquisitionState().catch(() => {});
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
      const pages = orderedBookChildren(detail, ENTITY_KIND.bookPage);
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
    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
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


  /** Cancel stops the download only — the wanted placeholder stays, so refresh in place. */
  function handleAcquisitionCancelled() {
    void loadBook(bookId, { showLoading: false });
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

  function audiobookPlaybackContext() {
    if (!book) return null;
    return {
      artistName: authorLink?.title ?? null,
      coverUrl: card?.posterCard?.cover?.src ?? card?.poster?.src ?? null,
      playbackOwnerEntityId: book.id,
      playbackOwnerTitle: book.title,
      playbackOwnerEntityKind: ENTITY_KIND.book,
    };
  }

  function playAudiobookTrack(trackId: string, startSeconds: number) {
    const context = audiobookPlaybackContext();
    if (!context) return;
    playback.play(audiobookTracks, trackId, context, { shuffle: false, startSeconds });
  }

  function openChapterRow(row: BookChapterRow, combined = false) {
    if (!book || !row.readTarget) return;
    const target = row.readTarget;
    if (target.kind === "epub") {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "book",
        id: book.id,
        returnId: book.id,
        location: target.location,
        combined,
      }));
      return;
    }
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: target.chapterId,
      returnId: book.id,
      combined,
    }));
  }

  function listenToChapter(row: BookChapterRow) {
    const track = row.audioTrack;
    if (!track) return;
    if (isCurrentAudiobook && playback.currentTrack?.id === track.id) {
      playback.toggle();
      return;
    }
    const startSeconds = savedAudiobookResume?.trackId === track.id
      ? savedAudiobookResume.trackOffsetSeconds
      : 0;
    playAudiobookTrack(track.id, startSeconds);
  }

  function openCombinedChapter(row: BookChapterRow) {
    const track = row.audioTrack;
    if (!row.readTarget || !track) return;
    if (isCurrentAudiobook && playback.currentTrack?.id === track.id) {
      if (!playback.playing) playback.toggle();
    } else {
      playAudiobookTrack(track.id, 0);
    }
    openChapterRow(row, true);
  }

  function listenToBook(options: { startOver?: boolean } = {}) {
    if (!book || audiobookTracks.length === 0) return;
    if (!options.startOver && isCurrentAudiobook && !audiobookCompleted) {
      playback.toggle();
      return;
    }

    const resume = resolveAudiobookResume(
      audiobookTracks,
      options.startOver || audiobookCompleted ? 0 : audiobookResumeSeconds,
    );
    if (!resume) return;
    playAudiobookTrack(resume.trackId, resume.trackOffsetSeconds);
  }

  async function handleToggleListened(listened: boolean) {
    if (!book || audiobookTotalSeconds <= 0 || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityPlayback(book.id, {
        resumeSeconds: audiobookResumeSeconds,
        completed: listened,
      });
      await loadBook();
    } finally {
      listeningBusy = false;
    }
  }

  async function startListeningOver() {
    if (!book || audiobookTotalSeconds <= 0 || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityPlayback(book.id, {
        resumeSeconds: 0,
        completed: false,
      });
      listenToBook({ startOver: true });
      await loadBook();
    } finally {
      listeningBusy = false;
    }
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
        unit: PROGRESS_UNIT.page,
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
        unit: PROGRESS_UNIT.page,
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

  function resumeSingleFile() {
    if (!book) return;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
      command: "resume",
    }));
  }

  /** Marks a single-file book read or unread without moving the saved reading position. */
  async function handleToggleSingleFileRead(read: boolean) {
    if (!book || !singleFileProgressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: book.id,
        unit: singleFileProgressDisplay.unit,
        index: singleFileProgressDisplay.index,
        total: singleFileProgressDisplay.total,
        mode: singleFileProgressDisplay.mode,
        location: singleFileProgressDisplay.location,
        completed: read,
      });
      await loadBook();
    } catch {
      // best-effort; the panel reflects the last known state on failure
    } finally {
      progressBusy = false;
    }
  }

  /** Resets a single-file book to the beginning and clears completion. */
  async function startSingleFileOver() {
    if (!book || !singleFileProgressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: book.id,
        unit: singleFileProgressDisplay.unit,
        index: 0,
        total: singleFileProgressDisplay.total,
        mode: singleFileProgressDisplay.mode,
        location: null,
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
      onArtworkPaletteChange={(palette) => (artworkPalette = palette)}
      {defaultCreditRole}
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
        {#if entityWanted}
          <span class="hero-badge wanted">Wanted</span>
        {/if}
        {#if progressDisplay}
          <span class="hero-badge">Read {progressDisplay.percent}%</span>
        {:else if singleFileProgressDisplay}
          <span class="hero-badge">Read {singleFileProgressDisplay.percent}%</span>
        {/if}
        {#if audiobookTracks.length > 0 && audiobookPercent > 0}
          <span class="hero-badge">Listened {audiobookPercent}%</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={book}
            {fileManagement}
            showEntityRequestControls={false}
            showAcquisitionPanel={false}
            onCancelled={handleAcquisitionCancelled}
            onImported={() => loadBook(bookId, { showLoading: false })}
          />
          <BookRenditionAcquisitionCard
            ownership={{
              ebook: hasReadableContent,
              audiobook: audiobookTracks.length > 0,
            }}
            acquisitions={bookRenditionAcquisitions}
            monitors={bookRenditionMonitors}
            onRequest={requestBookRendition}
            onToggleMonitor={toggleBookRenditionMonitor}
            onChanged={handleBookAcquisitionChanged}
          />
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
    {:else if singleFileProgressDisplay}
      <section class="progress-section">
        <MediaProgressPanel
          kind="read"
          completed={singleFileProgressDisplay.isComplete}
          percent={singleFileProgressDisplay.percent}
          positionLabel={singleFileProgressDisplay.positionLabel}
          canResume={!singleFileProgressDisplay.isComplete}
          canStartOver
          busy={progressBusy}
          onToggleCompleted={handleToggleSingleFileRead}
          onResume={resumeSingleFile}
          onStartOver={startSingleFileOver}
        />
      </section>
    {/if}

    {#if audiobookTracks.length > 0}
      <section class="progress-section">
        <MediaProgressPanel
          kind="listen"
          completed={audiobookCompleted}
          percent={audiobookPercent}
          positionLabel={audiobookPositionLabel}
          countLabel={`${audiobookTracks.length} part${audiobookTracks.length === 1 ? "" : "s"}`}
          canResume={!audiobookCompleted && audiobookResumeSeconds > 0}
          canStartOver={audiobookCompleted || audiobookResumeSeconds > 0}
          busy={listeningBusy}
          onToggleCompleted={handleToggleListened}
          onResume={() => listenToBook()}
          onStartOver={startListeningOver}
        />
      </section>
    {/if}

    {#if chapterRows.length > 0}
      <BookChapterList
        rows={chapterRows}
        primaryColor={chapterPalette.primary}
        secondaryColor={chapterPalette.secondary}
        readingProgressLabel={chapterReadingProgressLabel}
        listeningProgressLabel={chapterListeningProgressLabel}
        onRead={openChapterRow}
        onListen={listenToChapter}
        onCombined={openCombinedChapter}
      />
    {:else if epubContentsLoading}
      <section class="chapter-loading" aria-live="polite">Reading the EPUB contents…</section>
    {/if}

    {#if childBookCards.length > 0}
      <EntityGridSection
        title="Books"
        count={childBookCards.length}
        icon={BookOpen}
        prefsKey={`book-${book.id}-books-section`}
      >
        <EntityGrid
          cards={childBookCards}
          prefsKey={`book-${book.id}-books`}
          initialSortBy="position"
          emptyTitle="No books"
          emptyMessage="No books found for this series."
        />
      </EntityGridSection>
    {/if}

    {#if volumeCards.length > 0}
      <EntityGridSection
        title="Volumes"
        count={volumeCards.length}
        icon={BookOpen}
        prefsKey={`book-${book.id}-volumes-section`}
      >
        <EntityGrid
          cards={volumeCards}
          prefsKey={`book-${book.id}-volumes`}
          initialSortBy="position"
          emptyTitle="No volumes"
          emptyMessage="No volumes found for this book."
        />
      </EntityGridSection>
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
    border-color: var(--color-border-accent-strong, rgba(199, 201, 204, 0.52));
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

  .chapter-loading {
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    padding: 1rem;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    letter-spacing: 0.04em;
  }

</style>
