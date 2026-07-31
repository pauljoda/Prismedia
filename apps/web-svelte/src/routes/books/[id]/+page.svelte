<script lang="ts">
  import { CAPABILITY_KIND, PROGRESS_UNIT, READER_MODE, type BookRenditionCode } from "$lib/api/generated/codes";
  import { onDestroy } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Headphones, Info, Play, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import BookRenditionAcquisitionCard from "$lib/components/acquisitions/BookRenditionAcquisitionCard.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { getBookMetadataCapability, getCapability, isWanted } from "$lib/api/capabilities";
  import { fetchAcquisitionsForEntity } from "$lib/api/acquisitions";
  import { fetchEntityMonitors, resumeMonitor, stopMonitor } from "$lib/api/monitors";
  import { commitEntityRequest } from "$lib/api/requests";
  import { updateEntityProgress } from "$lib/api/playback";
  import { BookFormat, type AcquisitionDetail, type EntityThumbnail, type MonitorView } from "$lib/api/generated/model";
  import { fetchEntity, fetchEntityChildren, type EntityCardFull } from "$lib/api/entities";
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
  import { audiobookTrackItems } from "$lib/entities/audiobook-playback";
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
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import BookCombinedProgressCard from "$lib/components/books/BookCombinedProgressCard.svelte";
  import BookChapterList from "$lib/components/books/BookChapterList.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { isHiddenEntityNotFoundError } from "$lib/nsfw/hidden-entity";
  import type { AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
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
    buildBookProgressMappings,
    bookProgressCursor,
    epubChapterFraction,
    resolveBookAudioResume,
    resolveBookCombinedResume,
    resolveChapterCombinedLaunch,
    type BookCombinedLaunch,
    type BookReadingPosition,
  } from "$lib/entities/book-combined-progress";
  import { useLegacyBookProgressMigration } from "$lib/entities/book-legacy-progress-migration.svelte";
  import { formatWatchDuration } from "$lib/stats/playback-stats";
  import {
    loadEpubContents,
    type EpubContentsEntry,
  } from "$lib/entities/epub-contents";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import { monitorIsActive } from "$lib/requests/monitor-status";

  const playback = useAudioPlayback()!;
  interface ChapterDetail {
    thumbnail: EntityThumbnail;
    pages: EntityThumbnail[];
    summary: BookReaderChapter;
  }

  // The acquisition backing this book (wanted placeholder still searching/downloading, or the import
  // that produced it), so its state is managed right here instead of only under /request.
  // The book's parent author grouping, when scanned under an Author/ folder, for a breadcrumb back-link.
  let authorLink = $state<{ id: string; title: string } | null>(null);
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
  let loadedEpubKey: string | null = null;
  let epubContentsAbort: AbortController | null = null;

  const bookId = $derived(page.params.id ?? "");
  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => bookId,
    load: ({ signal }) => loadBook(bookId, signal),
    breadcrumbs: (nextBook) => {
      const crumbs: AppBreadcrumb[] = [{ label: "Books", href: "/books" }];
      // When the book sits under an author, surface it ("Books / Andy Weir / Project Hail Mary").
      if (authorLink) {
        crumbs.push({
          label: authorLink.title,
          href: resolveEntityHref(ENTITY_KIND.bookAuthor, authorLink.id),
        });
      }
      crumbs.push({ label: nextBook.title });
      return crumbs;
    },
  });
  const book = $derived(detail.entity);
  const bookMetadata = $derived(book ? getBookMetadataCapability(book.capabilities) : undefined);
  const bookType = $derived(bookMetadata?.bookType ?? null);
  // A wanted placeholder has metadata but no file yet; reading is offered only once the file lands.
  // Its acquisition/monitoring surface is the Acquisition detail tab.
  const entityWanted = $derived(!!book && isWanted(book.capabilities));
  // Single-file books (EPUB/PDF) are read straight from the source file with no chapter entities.
  const isSingleFileBook = $derived(
    !!book && (bookMetadata?.format === BookFormat.epub || bookMetadata?.format === BookFormat.pdf),
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
    chapterDetails.find((chapter) => chapter.thumbnail.id === selectedChapterId) ?? chapterDetails[0] ?? null,
  );
  const selectedProgress = $derived(
    progressDisplay?.chapterId === selectedChapter?.thumbnail.id ? progressDisplay : null,
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
  const audiobookPlayback = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.playback) : undefined,
  );
  const bookProgress = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : undefined,
  );
  const isCurrentAudiobook = $derived(
    playback.context?.playbackOwnerEntityId === book?.id &&
      playback.context?.playbackOwnerEntityKind === ENTITY_KIND.book,
  );
  const readableChapters = $derived.by((): ReadableBookChapter[] => {
    if (bookMetadata?.format === BookFormat.epub) {
      return epubContents.map((entry) => ({
        id: entry.id,
        title: entry.title,
        order: entry.order,
        depth: entry.depth,
        target: {
          kind: "epub",
          location: entry.location,
          startFraction: entry.startFraction,
          endFraction: entry.endFraction,
        },
        pageCount: null,
      }));
    }
    return chapterDetails.map((chapter, index) => ({
      id: chapter.thumbnail.id,
      title: chapter.thumbnail.title,
      order: index,
      depth: 0,
      target: { kind: "entity-chapter", chapterId: chapter.thumbnail.id },
      pageCount: chapter.pages.length,
    }));
  });
  const baseChapterRows = $derived(buildBookChapterRows({
    readableChapters,
    audioTracks: audiobookTracks,
    currentReadableId: bookMetadata?.format === BookFormat.epub
      ? currentEpubChapterId
      : progressDisplay?.isComplete
        ? null
        : progressDisplay?.chapterId ?? null,
    currentAudioTrackId: isCurrentAudiobook ? playback.currentTrack?.id ?? null : null,
  }));
  const bookProgressMappings = $derived(buildBookProgressMappings(
    book?.id ?? "",
    baseChapterRows,
    bookProgress?.mode ?? READER_MODE.paged,
  ));
  useLegacyBookProgressMigration(
    () => book,
    () => baseChapterRows,
    () => bookProgressMappings,
    () => detail.reload({ showLoading: false }),
  );
  const savedAudiobookResume = $derived(resolveBookAudioResume(
    baseChapterRows,
    bookProgressMappings,
    bookProgressCursor(bookProgress),
  ));
  const currentAudiobookTrackId = $derived(
    isCurrentAudiobook
      ? playback.currentTrack?.id ?? savedAudiobookResume?.trackId ?? null
      : savedAudiobookResume?.trackId ?? null,
  );
  const chapterRows = $derived(baseChapterRows.map((row) => ({
    ...row,
    isCurrentAudio: row.audioTrack?.id === currentAudiobookTrackId,
  })));
  const canonicalCompleted = $derived(Boolean(bookProgress?.completedAt));
  const canonicalPercent = $derived.by(() => {
    if (canonicalCompleted) return 100;
    if (singleFileProgressDisplay) return singleFileProgressDisplay.percent;
    if (progressDisplay) return progressDisplay.percent;
    const total = numberValue(bookProgress?.total) ?? 0;
    const index = numberValue(bookProgress?.index) ?? 0;
    return total > 0 ? Math.max(0, Math.min(100, Math.round((index / total) * 100))) : 0;
  });
  const canonicalPositionLabel = $derived(
    singleFileProgressDisplay?.positionLabel
      ?? progressDisplay?.workPageLabel
      ?? progressDisplay?.chapterPageLabel
      ?? progressDisplay?.pageLabel
      ?? (savedAudiobookResume ? `${canonicalPercent}% of book` : null),
  );
  const bookActivitySeconds = $derived(numberValue(audiobookPlayback?.playDurationSeconds) ?? 0);
  const bookActivityLabel = $derived(
    bookActivitySeconds > 0 ? `${formatWatchDuration(bookActivitySeconds)} read or listened` : null,
  );
  const bookReadingPosition = $derived.by((): BookReadingPosition | null => {
    if (singleFileProgressDisplay && !singleFileProgressDisplay.isComplete && currentEpubChapterId) {
      const row = chapterRows.find((candidate) => candidate.isCurrentReading);
      if (!row) return null;
      const overallFraction = singleFileProgressDisplay.total > 0
        ? singleFileProgressDisplay.index / singleFileProgressDisplay.total
        : 0;
      return {
        rowId: row.id,
        overallFraction,
        chapterFraction: epubChapterFraction(row, overallFraction),
        location: singleFileProgressDisplay.location,
        pageIndex: null,
      };
    }
    if (!progressDisplay || progressDisplay.isComplete) return null;
    const row = chapterRows.find((candidate) => candidate.isCurrentReading);
    if (!row) return null;
    return {
      rowId: row.id,
      overallFraction: progressDisplay.workTotal > 0
        ? progressDisplay.workPage / progressDisplay.workTotal
        : progressDisplay.percent / 100,
      chapterFraction: progressDisplay.pageCount > 0
        ? progressDisplay.currentPage / progressDisplay.pageCount
        : 0,
      location: null,
      pageIndex: Math.max(0, progressDisplay.currentPage - 1),
    };
  });
  const combinedResumePlan = $derived(
    resolveBookCombinedResume(chapterRows, bookReadingPosition),
  );
  const hasCombinedContent = $derived(chapterRows.some((row) => row.readTarget && row.audioTrack));
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
    canonicalPositionLabel ?? (currentAudiobookTrackId ? "Current part" : null),
  );
  const hasReadableContent = $derived(
    isSingleFileBook ||
      (bookMetadata?.format === BookFormat["image-archive"] &&
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
    onStatusChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/books"),
  });
  const wantedStateLabel = $derived(acquisitionStatusDisplay(acq.acquisition?.summary.status).label);
  const fileManagement = {
    onDeleted: () => goto("/books"),
    onReverted: () => refreshAfterManagedFileRevert(
      acq,
      () => detail.reload({ showLoading: false }),
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
        label: canonicalCompleted ? "Re-read" : canonicalPercent > 0 ? "Resume" : "Read",
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: continueReading,
      });
    } else if (hasReadableContent) {
      actions.push({
        id: "read-book",
        label: primaryReadLabel,
        icon: Play,
        iconFill: "currentColor",
        variant: "primary",
        onClick: continueReading,
      });
    }
    if (audiobookTracks.length > 0) {
      actions.push({
        id: "listen-book",
        label: isCurrentAudiobook && playback.playing
          ? "Pause"
          : canonicalPercent > 0 && !canonicalCompleted
            ? "Continue listening"
            : canonicalCompleted
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

  onDestroy(() => epubContentsAbort?.abort());

  $effect(() => {
    if (!bookRenditionAcquisitions.some((item) => acquisitionStatusShouldPoll(item.summary.status))) return;
    const timer = setInterval(() => void refreshBookAcquisitionState().catch(() => {}), 5000);
    return () => clearInterval(timer);
  });

  async function loadBook(targetBookId: string, signal: AbortSignal): Promise<EntityCardFull> {
    const [nextBook, nextAcquisitions, nextMonitors] = await Promise.all([
      fetchEntity(targetBookId, { signal }),
      fetchAcquisitionsForEntity(targetBookId, { signal }).catch(() => {
        signal.throwIfAborted();
        return [];
      }),
      fetchEntityMonitors(targetBookId, { signal }).catch(() => {
        signal.throwIfAborted();
        return [];
      }),
    ]);
    const parentId = nextBook.parentEntityId;
    const [relationships, chapters, parentThumbs] = await Promise.all([
      hydrateStandardRelationshipCards(nextBook, { signal }),
      hydrateChapters(nextBook, signal),
      parentId ? fetchOrderedEntityThumbnails([parentId], { signal }) : Promise.resolve([]),
    ]);
    const progressSummary = await hydrateProgressChapterSummary(nextBook, chapters, signal);
    signal.throwIfAborted();

    if (book?.id !== nextBook.id) {
      epubContents = [];
      currentEpubChapterId = null;
      loadedEpubKey = null;
      artworkPalette = null;
    }

    // A book scanned under an Author/ folder is parented to a book-author; surface it as a back-link.
    const authorThumb = parentThumbs.find((thumbnail) => thumbnail.kind === ENTITY_KIND.bookAuthor);
    authorLink = authorThumb ? { id: authorThumb.id, title: authorThumb.title } : null;

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
    selectedChapterId = nextProgress?.chapterId ?? chapters[0]?.thumbnail.id ?? null;
    void hydrateEpubContents(nextBook);
    return nextBook;
  }

  async function hydrateEpubContents(nextBook: EntityCardFull): Promise<void> {
    epubContentsAbort?.abort();
    if (getBookMetadataCapability(nextBook.capabilities)?.format !== BookFormat.epub) {
      epubContents = [];
      currentEpubChapterId = null;
      epubContentsLoading = false;
      loadedEpubKey = null;
      return;
    }

    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
    const currentLocation = progress?.completedAt ? null : progress?.location;
    const progressTotal = numberValue(progress?.total) ?? 0;
    const currentFraction = progress?.completedAt || progressTotal <= 0
      ? null
      : (numberValue(progress?.index) ?? 0) / progressTotal;
    const key = `${nextBook.id}:${currentLocation ?? ""}:${currentFraction ?? ""}`;
    if (key === loadedEpubKey && epubContents.length > 0) return;

    const controller = new AbortController();
    epubContentsAbort = controller;
    epubContentsLoading = true;
    try {
      const contents = await loadEpubContents(
        `/entities/${nextBook.id}/files/source`,
        currentLocation,
        controller.signal,
        currentFraction,
      );
      if (controller.signal.aborted || bookId !== nextBook.id) return;
      epubContents = contents.entries;
      currentEpubChapterId = contents.currentChapterId;
      loadedEpubKey = key;
    } catch (error) {
      if (controller.signal.aborted || (error instanceof DOMException && error.name === "AbortError")) return;
      if (bookId !== nextBook.id) return;
      epubContents = [];
      currentEpubChapterId = null;
    } finally {
      if (epubContentsAbort === controller) {
        epubContentsAbort = null;
        epubContentsLoading = false;
      }
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
      detail.reload({ showLoading: false }),
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

  async function hydrateChapters(
    nextBook: EntityCardFull,
    signal: AbortSignal,
  ): Promise<ChapterDetail[]> {
    const directChapters = orderedBookChildren(nextBook, ENTITY_KIND.bookChapter).map((thumbnail, index) => ({
      thumbnail,
      sortOrder: Number(thumbnail.sortOrder ?? index),
    }));
    const chapterItems = directChapters.sort((a, b) =>
      a.sortOrder - b.sortOrder || a.thumbnail.title.localeCompare(b.thumbnail.title),
    );
    const childGroups = await fetchEntityChildren(
      chapterItems.map((item) => item.thumbnail.id),
      { signal },
    );
    const pagesByChapter = new Map(childGroups.map((group) => [group.parentId, group.items]));
    return chapterItems.map(({ thumbnail }, index) => {
      const pages = (pagesByChapter.get(thumbnail.id) ?? [])
        .filter((child) => child.kind === ENTITY_KIND.bookPage);
      return {
        thumbnail,
        pages,
        summary: {
          id: thumbnail.id,
          title: thumbnail.title,
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
    nextBook: EntityCardFull,
    chapters: ChapterDetail[],
    signal: AbortSignal,
  ): Promise<BookReaderChapter | null> {
    const progress = getCapability(nextBook.capabilities, CAPABILITY_KIND.progress);
    if (!progress?.currentEntityId || chapters.some((chapter) => chapter.thumbnail.id === progress.currentEntityId)) {
      return null;
    }

    let detail: EntityCardFull;
    try {
      detail = await fetchEntity(progress.currentEntityId, { signal });
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

  /** Cancel stops the download only — the wanted placeholder stays, so refresh in place. */
  function handleAcquisitionCancelled() {
    void detail.reload({ showLoading: false });
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
        id: selectedChapter.thumbnail.id,
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
      bookProgressMappings,
    };
  }

  function playAudiobookTrack(trackId: string, startSeconds: number) {
    const context = audiobookPlaybackContext();
    if (!context) return;
    playback.play(audiobookTracks, trackId, context, { shuffle: false, startSeconds });
  }

  function openChapterRow(row: BookChapterRow) {
    if (!book || !row.readTarget) return;
    const target = row.readTarget;
    if (target.kind === "epub") {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "book",
        id: book.id,
        returnId: book.id,
        location: target.location,
      }));
      return;
    }
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: target.chapterId,
      returnId: book.id,
    }));
  }

  function openReadingLaunch(plan: BookCombinedLaunch) {
    if (!book) return;
    const row = chapterRows.find((candidate) => candidate.id === plan.rowId);
    const target = row?.readTarget;
    if (!row || !target) return;

    if (target.kind === "epub") {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "book",
        id: book.id,
        returnId: book.id,
        location: plan.readerLocation ?? (plan.readerFraction === null ? target.location : undefined),
        fraction: plan.readerFraction ?? undefined,
        combined: true,
      }));
      return;
    }
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "chapter",
      id: target.chapterId,
      returnId: book.id,
      pageIndex: plan.readerPageIndex ?? undefined,
      combined: true,
    }));
  }

  function openCombinedLaunch(plan: BookCombinedLaunch) {
    const track = chapterRows.find((candidate) => candidate.id === plan.rowId)?.audioTrack;
    if (!track) return;
    playAudiobookTrack(track.id, plan.audioStartSeconds);
    openReadingLaunch(plan);
  }

  function listenToChapter(row: BookChapterRow) {
    const track = row.audioTrack;
    if (!track) return;
    if (isCurrentAudiobook && playback.currentTrack?.id === track.id) {
      playback.toggle();
      return;
    }
    const startSeconds = currentAudiobookTrackId === track.id && savedAudiobookResume?.trackId === track.id
      ? savedAudiobookResume.trackOffsetSeconds
      : 0;
    playAudiobookTrack(track.id, startSeconds);
  }

  function openCombinedChapter(row: BookChapterRow) {
    const plan = resolveChapterCombinedLaunch(
      row,
      bookReadingPosition,
    );
    if (plan) openCombinedLaunch(plan);
  }

  function continueReading() {
    if (combinedResumePlan && !canonicalCompleted) {
      openReadingLaunch(combinedResumePlan);
      return;
    }
    if (isSingleFileBook) {
      openSingleFileReader();
      return;
    }
    openSelectedReader();
  }

  function continueCombined() {
    if (combinedResumePlan) openCombinedLaunch(combinedResumePlan);
  }

  function listenToBook(options: { startOver?: boolean } = {}) {
    if (!book || audiobookTracks.length === 0) return;
    if (!options.startOver && isCurrentAudiobook && !canonicalCompleted) {
      playback.toggle();
      return;
    }

    const firstTrack = audiobookTracks[0];
    const resume = !options.startOver && !canonicalCompleted
      ? savedAudiobookResume
      : firstTrack
        ? { trackId: firstTrack.id, trackOffsetSeconds: 0 }
        : null;
    if (!resume) return;
    playAudiobookTrack(resume.trackId, resume.trackOffsetSeconds);
  }

  async function handleToggleListened(listened: boolean) {
    if (!book || !bookProgress?.currentEntityId || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: bookProgress.currentEntityId,
        unit: bookProgress.unit,
        index: numberValue(bookProgress.index) ?? 0,
        total: numberValue(bookProgress.total) ?? 0,
        mode: bookProgress.mode,
        location: bookProgress.location,
        completed: listened,
      });
      await detail.reload({ showLoading: false });
    } finally {
      listeningBusy = false;
    }
  }

  async function startListeningOver() {
    const firstTrack = audiobookTracks[0];
    const firstMapping = firstTrack
      ? bookProgressMappings.find((mapping) => mapping.trackId === firstTrack.id)
      : null;
    if (!book || !firstTrack || !firstMapping || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityProgress(book.id, {
        currentEntityId: firstMapping.currentEntityId,
        unit: firstMapping.unit,
        index: numberValue(firstMapping.startIndex) ?? 0,
        total: numberValue(firstMapping.total) ?? 0,
        mode: firstMapping.mode,
        location: null,
        reset: true,
      });
      listenToBook({ startOver: true });
      await detail.reload({ showLoading: false });
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
      await detail.reload({ showLoading: false });
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
        mode: progressDisplay?.readerMode ?? READER_MODE.paged,
        reset: true,
      });
      await detail.reload({ showLoading: false });
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
      await detail.reload({ showLoading: false });
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
      await detail.reload({ showLoading: false });
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
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load book."
    onRetry={detail.retry}
  >
    {#if card && book}
      <EntityDetail
      {card}
      wantedStatus={acq.acquisition?.summary.status ?? null}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
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
        {#if bookActivityLabel}
          <span class="meta-sep"></span>
          <span class="meta-item">{bookActivityLabel}</span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if entityWanted}
          <span class="hero-badge wanted">{wantedStateLabel}</span>
        {/if}
        {#if canonicalPercent > 0}
          <span class="hero-badge">Progress {canonicalPercent}%</span>
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
            onImported={() => detail.reload({ showLoading: false })}
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

    {#if hasCombinedContent}
      <BookCombinedProgressCard
        progressPercent={canonicalPercent}
        progressLabel={canonicalPositionLabel}
        completed={canonicalCompleted}
        activityLabel={bookActivityLabel}
        primaryColor={chapterPalette.primary}
        secondaryColor={chapterPalette.secondary}
        onRead={continueReading}
        onListen={() => listenToBook()}
        onCombined={continueCombined}
      />
    {/if}

    {#if !hasCombinedContent && progressDisplay}
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
    {:else if !hasCombinedContent && singleFileProgressDisplay}
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

    {#if audiobookTracks.length > 0 && !hasReadableContent}
      <section class="progress-section">
        <MediaProgressPanel
          kind="listen"
          completed={canonicalCompleted}
          percent={canonicalPercent}
          positionLabel={canonicalPositionLabel}
          countLabel={`${audiobookTracks.length} part${audiobookTracks.length === 1 ? "" : "s"}`}
          canResume={!canonicalCompleted && canonicalPercent > 0}
          canStartOver={canonicalCompleted || canonicalPercent > 0}
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
  </EntityDetailPageState>
</div>

<style>
  .book-page {
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
