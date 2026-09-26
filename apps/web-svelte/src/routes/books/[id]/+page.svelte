<script lang="ts">
  import { Badge as UiBadge } from "@prismedia/ui-svelte";
  import {
    ALIGNMENT_BASIS,
    BOOK_RENDITION,
    CAPABILITY_KIND,
    CONSUMPTION_MODALITY,
    PROGRESS_UNIT,
    READER_MODE,
    type BookRenditionCode,
  } from "$lib/api/generated/codes";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Headphones, Info, ListOrdered, Play, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import BookRenditionAcquisitionCard from "$lib/components/acquisitions/BookRenditionAcquisitionCard.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { getBookMetadataCapability, getCapability, hasReadableBookFile, isWanted } from "$lib/api/capabilities";
  import {
    fetchAcquisitionsForEntity,
    fetchAcquisitionSummariesForEntity,
  } from "$lib/api/acquisitions";
  import { fetchEntityMonitors, resumeMonitor, stopMonitor } from "$lib/api/monitors";
  import { commitBookRenditionsRequest, commitEntityRequest } from "$lib/api/requests";
  import { updateEntityProgress } from "$lib/api/consumption";
  import type {
    AcquisitionDetail,
    AlignedTarget,
    BookAlignmentResponse,
    BookChapterAudioMapping,
    BookContentsEntry,
    EntityThumbnail,
    ListeningTarget,
    MonitorView,
    ReadingTarget,
  } from "$lib/api/generated/model";
  import {
    fetchBookAlignment,
    fetchBookContents,
    saveBookChapterMappings,
  } from "$lib/api/books";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import {
    bookEntityProgressDisplay,
    orderedBookChildren,
    persistedPageCount,
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
  import BookChapterMappingEditor from "$lib/components/books/BookChapterMappingEditor.svelte";
  import BookManagerRequest from "$lib/components/books/BookManagerRequest.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { isHiddenEntityNotFoundError } from "$lib/nsfw/hidden-entity";
  import type { AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";
  import { formatDuration, numberValue } from "$lib/utils/format";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import type { ArtworkPalette } from "$lib/entities/artwork-palette";
  import {
    alignmentGapExplanation,
    bookChapterRowOwnsAudioTime,
    bookChapterRowsFromAlignment,
    bookSeparateProgress,
    readableChaptersFromAlignment,
    readingPositionLabel,
    type BookChapterRow,
  } from "$lib/entities/book-chapter-list";
  import { formatActiveDuration } from "$lib/stats/consumption-stats";
  import { exactWebEpubResumeLocation } from "$lib/entities/epub-contents";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import { monitorIsActive } from "$lib/requests/monitor-status";

  const playback = useAudioPlayback()!;
  const session = useSession();
  interface ChapterDetail {
    thumbnail: EntityThumbnail;
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
  let acceptedManagerRenditions = $state<{ bookId: string; renditions: BookRenditionCode[] }>({ bookId: "", renditions: [] });
  let artworkPalette = $state.raw<ArtworkPalette | null>(null);
  let alignment = $state.raw<BookAlignmentResponse | null>(null);
  let chapterMappingLoadError = $state<string | null>(null);

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
    !!book && hasReadableBookFile(book.capabilities),
  );
  const singleFileProgress = $derived(book && isSingleFileBook ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : null);
  // Started once a position has been saved (EPUB and PDF both set currentEntityId to the book id).
  const singleFileInProgress = $derived(!!singleFileProgress?.currentEntityId && !singleFileProgress?.completedAt);
  // Single-file books have no chapter entities, so they need their own progress-panel display.
  const singleFileProgressDisplay = $derived(isSingleFileBook ? singleFileBookProgressDisplay(book) : null);
  const peopleLabel = "People";
  const defaultCreditRole = CREDIT_ROLE.writer;
  const pageCount = $derived(persistedPageCount(book));
  const bookTitle = $derived(book?.title ?? "Book");
  const chapterSummaries = $derived(combineChapterSummaries(chapterDetails, progressChapterSummary));
  const progressDisplay = $derived(bookEntityProgressDisplay(book, chapterSummaries));
  const audiobookTracks = $derived(book ? audiobookTrackItems(book) : []);
  const bookConsumption = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.consumption) : undefined,
  );
  const bookProgress = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : undefined,
  );
  const audioPlayback = $derived(
    book ? getCapability(book.capabilities, CAPABILITY_KIND.playableAudio) : undefined,
  );
  const isCurrentAudiobook = $derived(
    playback.context?.playbackOwnerEntityId === book?.id,
  );
  // Alignment, pairing, and every resume destination are server-owned. The page only presents them
  // and opens the targets they name.
  const resume = $derived(alignment?.resume ?? null);
  // A Separate Book (no exact chapter link between its formats) shows two progresses and never
  // offers to switch between them; a Linked Book keeps one progress and the switching actions.
  const separateProgress = $derived(bookSeparateProgress(alignment));
  const readableChapters = $derived(readableChaptersFromAlignment(alignment));
  const readingRowId = $derived(resume?.exactReading ? resume.switchToListening.rowId : null);
  const listeningRowId = $derived(resume?.exactListening ? resume.switchToReading.rowId : null);
  const chapterRows = $derived(bookChapterRowsFromAlignment({
    alignment,
    audioTracks: audiobookTracks,
    readingRowId,
    listeningRowId,
    playingTrackId: isCurrentAudiobook ? playback.currentTrack?.id ?? null : null,
    playingSeconds: isCurrentAudiobook ? playback.currentTime : null,
  }));
  const savedAudiobookResume = $derived(resume?.exactListening ?? null);
  const currentAudiobookTrackId = $derived(
    isCurrentAudiobook
      ? playback.currentTrack?.id ?? savedAudiobookResume?.trackEntityId ?? null
      : savedAudiobookResume?.trackEntityId ?? null,
  );
  const canonicalCompleted = $derived(Boolean(bookProgress?.completedAt));
  const canonicalPercent = $derived.by(() => {
    if (canonicalCompleted) return 100;
    if (singleFileProgressDisplay) return singleFileProgressDisplay.percent;
    if (progressDisplay) return progressDisplay.percent;
    const consumedPercent = numberValue(bookProgress?.consumedPercent);
    if (consumedPercent != null) return Math.round(Math.max(0, Math.min(1, consumedPercent)) * 100);
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
  const bookActivitySeconds = $derived(numberValue(bookConsumption?.activeSeconds) ?? 0);
  const bookActivityLabel = $derived(
    bookActivitySeconds > 0 ? `${formatActiveDuration(bookActivitySeconds)} read or listened` : null,
  );
  const alignedReading = $derived(alignedSide(resume?.switchToReading)?.reading ?? null);
  const alignedListening = $derived(alignedSide(resume?.switchToListening)?.listening ?? null);
  const readAction = $derived.by(() => {
    if (resume?.exactReading) return { label: "Continue reading", hint: null };
    if (alignedReading) {
      return {
        label: resume?.switchToReading.approximate ? "Continue reading ≈" : "Continue reading",
        hint: "Reading estimated from where you stopped listening.",
      };
    }
    return { label: canonicalCompleted ? "Read again" : "Start reading", hint: null };
  });
  const listenAction = $derived.by(() => {
    if (resume?.exactListening) return { label: "Continue listening", hint: null };
    if (alignedListening) {
      return {
        label: resume?.switchToListening.approximate ? "Continue listening ≈" : "Continue listening",
        hint: "Listening estimated from where you stopped reading.",
      };
    }
    return { label: canonicalCompleted ? "Listen again" : "Start listening", hint: null };
  });
  const combinedAction = $derived.by(() => {
    const combined = resume?.combined;
    if (!combined || combined.gap) {
      return { label: "Read & listen", disabled: true, explanation: combined ? alignmentGapExplanation(combined) : null };
    }
    if (combined.basis === ALIGNMENT_BASIS.freshStart) {
      return { label: "Start both", disabled: false, explanation: null };
    }
    return {
      label: combined.approximate ? "Continue both ≈" : "Continue both",
      disabled: false,
      explanation: null,
    };
  });
  // When both formats have exact positions, offer to bring the older one to the newer one's
  // aligned spot; the exact position stays the primary action.
  const switchOffer = $derived.by(() => {
    if (!resume?.exactReading || !resume.exactListening) return null;
    const fromListening = resume.lastModality === CONSUMPTION_MODALITY.listening;
    const target = fromListening ? resume.switchToReading : resume.switchToListening;
    if (target.gap) return { label: null, note: alignmentGapExplanation(target) };
    const approximate = target.approximate ? " ≈" : "";
    return fromListening
      ? { label: `Read from your listening spot${approximate}`, note: null }
      : { label: `Listen from your reading spot${approximate}`, note: null };
  });
  const hasCombinedContent = $derived(
    separateProgress !== null || chapterRows.some((row) => row.readTarget && row.audioTrack),
  );
  const listeningStarted = $derived(
    separateProgress ? separateProgress.listeningPercent > 0 || savedAudiobookResume !== null : canonicalPercent > 0,
  );
  const canMapBookChapters = $derived(readableChapters.length > 0 && audiobookTracks.length > 0);
  const audioPartCount = $derived(Number(alignment?.coverage.audioWindowCount ?? 0) || audiobookTracks.length);
  const fallbackBookPalette = entityAccentForKind(ENTITY_KIND.book);
  const chapterPalette = $derived(artworkPalette ?? {
    primary: fallbackBookPalette.primary,
    secondary: fallbackBookPalette.secondary,
    background: "#000000",
  });
  const chapterReadingProgressLabel = $derived(readingPositionLabel(resume?.exactReading ?? null));
  const chapterListeningProgressLabel = $derived(
    savedAudiobookResume
      ? `at ${formatDuration(Number(savedAudiobookResume.offsetSeconds)) ?? "0:00"}`
      : currentAudiobookTrackId ? "Current part" : null,
  );
  const hasReadableContent = $derived(isSingleFileBook);
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
    }
    if (audiobookTracks.length > 0) {
      actions.push({
        id: "listen-book",
        label: isCurrentAudiobook && playback.playing
          ? "Pause"
          : listeningStarted && !canonicalCompleted
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
    {
      id: "chapter-mapping",
      label: "Chapter Mapping",
      icon: ListOrdered,
      hidden: !canMapBookChapters,
    },
    { id: "acquisition" },
  ]);

  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    const tabs: EntityDetailTab[] = [
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
    ];
    if (canMapBookChapters) {
      tabs.push({
        id: "chapter-mapping",
        label: "Chapter Mapping",
        icon: ListOrdered,
        sections: ["chapter-mapping"],
      });
    }
    tabs.push({ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] });
    return tabs;
  });

  // The shared acquisition controller already polls the Book's latest acquisition and reloads this
  // page when its lifecycle changes, so its fresher row replaces ours and this poll covers only the
  // other rendition's active acquisition.
  const liveRenditionAcquisitions = $derived(bookRenditionAcquisitions.map((item) =>
    item.summary.id === acq.acquisition?.summary.id ? acq.acquisition ?? item : item));

  $effect(() => {
    const watched = acq.acquisition?.summary.id;
    if (!bookRenditionAcquisitions.some((item) =>
      item.summary.id !== watched && acquisitionStatusShouldPoll(item.summary.status))) return;
    const timer = setInterval(() => void pollBookAcquisitionState().catch(() => {}), 5000);
    return () => clearInterval(timer);
  });

  async function loadBook(targetBookId: string, signal: AbortSignal): Promise<EntityCardFull> {
    const [nextBook, nextAcquisitions, nextMonitors, nextMappingState, nextContents] = await Promise.all([
      fetchEntity(targetBookId, { signal }),
      fetchAcquisitionsForEntity(targetBookId, { signal }).catch(() => {
        signal.throwIfAborted();
        return [];
      }),
      fetchEntityMonitors(targetBookId, { signal }).catch(() => {
        signal.throwIfAborted();
        return [];
      }),
      fetchBookAlignment(targetBookId, { signal })
        .then((response) => ({ alignment: response, error: null }))
        .catch((error) => {
          signal.throwIfAborted();
          return {
            alignment: null,
            error: error instanceof Error ? error.message : "Failed to load chapter alignment.",
          };
        }),
      // The readable chapter list (EPUB TOC or chapter summaries with page counts) is one small
      // server-persisted read, so it loads with the first wave instead of trailing the page.
      fetchBookContents(targetBookId, { signal })
        .then((response) => response.items)
        .catch(() => {
          signal.throwIfAborted();
          return [];
        }),
    ]);
    const parentId = nextBook.parentEntityId;
    const chapters = buildChapterDetails(nextBook, nextContents);
    const [relationships, parentThumbs, progressSummary] = await Promise.all([
      hydrateStandardRelationshipCards(nextBook, { signal }),
      parentId ? fetchOrderedEntityThumbnails([parentId], { signal }) : Promise.resolve([]),
      hydrateProgressChapterSummary(nextBook, chapters, signal),
    ]);
    signal.throwIfAborted();

    if (book?.id !== nextBook.id) {
      artworkPalette = null;
      alignment = null;
      chapterMappingLoadError = null;
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
    alignment = nextMappingState.alignment;
    chapterMappingLoadError = nextMappingState.error;

    return nextBook;
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

  async function saveChapterMappingDraft(
    mappings: readonly BookChapterAudioMapping[],
  ): Promise<BookAlignmentResponse> {
    if (!book) throw new Error("The book is not loaded.");
    const refreshed = await saveBookChapterMappings(book.id, mappings);
    alignment = refreshed;
    chapterMappingLoadError = null;
    return refreshed;
  }

  /**
   * Polls compact summaries while rendition work is active. Candidate-heavy detail is reloaded only
   * when an acquisition appears, disappears, or crosses a lifecycle boundary.
   */
  async function pollBookAcquisitionState(): Promise<void> {
    const targetBookId = bookId;
    if (!targetBookId) return;
    const [summaries, nextMonitors] = await Promise.all([
      fetchAcquisitionSummariesForEntity(targetBookId),
      fetchEntityMonitors(targetBookId),
    ]);
    if (bookId !== targetBookId) return;

    const currentById = new Map(
      bookRenditionAcquisitions.map((detail) => [detail.summary.id, detail]),
    );
    const lifecycleChanged = summaries.length !== bookRenditionAcquisitions.length
      || summaries.some((summary) => currentById.get(summary.id)?.summary.status !== summary.status);
    const nextAcquisitions = lifecycleChanged
      ? await fetchAcquisitionsForEntity(targetBookId)
      : summaries.map((summary) => ({
          ...currentById.get(summary.id)!,
          summary,
        }));
    if (bookId !== targetBookId) return;
    bookRenditionAcquisitions = nextAcquisitions;
    bookRenditionMonitors = nextMonitors;
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

  async function requestBothBookRenditions(): Promise<void> {
    if (!book) return;
    const response = await commitBookRenditionsRequest(book.id, [
      { rendition: BOOK_RENDITION.ebook },
      { rendition: BOOK_RENDITION.audiobook },
    ]);
    await refreshBookAcquisitionState().catch(() => {});
    const failures = response.bookRenditions?.filter((result) => result.error) ?? [];
    if (failures.length > 0) {
      throw new Error(failures.map((result) =>
        `${result.rendition === BOOK_RENDITION.audiobook ? "Audiobook" : "Ebook"}: ${result.error}`,
      ).join(" "));
    }
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

  /**
   * Chapter summaries come from the book card's own children plus the contents projection's
   * per-chapter page counts — the previous implementation fetched every chapter's page
   * thumbnails just to count them.
   */
  function buildChapterDetails(
    nextBook: EntityCardFull,
    contentsItems: readonly BookContentsEntry[],
  ): ChapterDetail[] {
    const pageCountByChapter = new Map(contentsItems.map((entry) => [
      entry.id,
      numberValue(entry.pageCount) ?? 0,
    ]));
    const directChapters = orderedBookChildren(nextBook, ENTITY_KIND.bookChapter).map((thumbnail, index) => ({
      thumbnail,
      sortOrder: Number(thumbnail.sortOrder ?? index),
    }));
    const chapterItems = directChapters.sort((a, b) =>
      a.sortOrder - b.sortOrder || a.thumbnail.title.localeCompare(b.thumbnail.title),
    );
    return chapterItems.map(({ thumbnail }, index) => ({
      thumbnail,
      summary: {
        id: thumbnail.id,
        title: thumbnail.title,
        sortOrder: index,
        pageCount: pageCountByChapter.get(thumbnail.id) ?? 0,
      },
    }));
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
    if (
      !progress?.currentEntityId ||
      progress.currentEntityId === nextBook.id ||
      chapters.some((chapter) => chapter.thumbnail.id === progress.currentEntityId)
    ) {
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

    const sortOrder = Number(detail.sortOrder ?? chapters.length);
    return {
      id: detail.id,
      title: detail.title,
      sortOrder: Number.isFinite(sortOrder) ? sortOrder : chapters.length,
      pageCount: persistedPageCount(detail),
    };
  }

  /** Cancel stops the download only — the wanted placeholder stays, so refresh in place. */
  function handleAcquisitionCancelled() {
    void detail.reload({ showLoading: false });
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
    if (!book || !audioPlayback) return null;
    return {
      artistName: authorLink?.title ?? null,
      coverUrl: card?.posterCard?.cover?.src ?? card?.poster?.src ?? null,
      playbackOwnerEntityId: book.id,
      playbackOwnerTitle: book.title,
      playbackOwnerEntityKind: ENTITY_KIND.book,
      progressModality: CONSUMPTION_MODALITY.listening,
      preservesQueueOrder: audioPlayback.preservesQueueOrder,
      supportsPlaybackRate: audioPlayback.supportsPlaybackRate,
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

  /** Server alignment target, or null when the server reported a gap. */
  function alignedSide(target: AlignedTarget | null | undefined): AlignedTarget | null {
    return target && !target.gap ? target : null;
  }

  /**
   * Opens a server reading target. Exact single-file positions resume through the reader's own
   * exact checkpoint; aligned EPUB positions open by chapter location at a chapter start and by
   * whole-book fraction otherwise; paged positions open their chapter at the page.
   */
  function openReadingTarget(target: ReadingTarget, options: { exact?: boolean; combined?: boolean } = {}) {
    if (!book) return;
    if (target.positionEntityId !== book.id) {
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "chapter",
        id: target.positionEntityId,
        returnId: book.id,
        pageIndex: numberValue(target.pageIndex) ?? undefined,
        combined: options.combined,
      }));
      return;
    }
    if (options.exact && !options.combined) {
      // The reader reopens its own exact checkpoint, including the precise locator.
      void goto(bookReaderHref({
        bookId: book.id,
        kind: "book",
        id: book.id,
        returnId: book.id,
        command: "resume",
      }));
      return;
    }

    const exactLocation = exactWebEpubResumeLocation(target.location);
    const chapterFraction = numberValue(target.chapterFraction);
    const chapterStart = !exactLocation && (chapterFraction === null || chapterFraction <= 0)
      ? target.chapterLocation
      : null;
    const total = numberValue(target.total) ?? 0;
    void goto(bookReaderHref({
      bookId: book.id,
      kind: "book",
      id: book.id,
      returnId: book.id,
      location: exactLocation ?? chapterStart ?? undefined,
      fraction: exactLocation || chapterStart || total <= 0
        ? undefined
        : (numberValue(target.index) ?? 0) / total,
      combined: options.combined,
    }));
  }

  function playListeningTarget(target: ListeningTarget) {
    playAudiobookTrack(target.trackEntityId, Math.max(0, Number(target.offsetSeconds)));
  }

  function listenToChapter(row: BookChapterRow) {
    const track = row.audioTrack;
    if (!track) return;
    if (isCurrentAudiobook && playback.currentTrack?.id === track.id) {
      if (bookChapterRowOwnsAudioTime(row, track.id, playback.currentTime)) {
        playback.toggle();
        return;
      }
      playback.seek(Number(row.audioStartSeconds ?? 0));
      if (!playback.playing) playback.toggle();
      return;
    }
    if (savedAudiobookResume && row.id === listeningRowId) {
      playListeningTarget(savedAudiobookResume);
      return;
    }
    playAudiobookTrack(track.id, Number(row.audioStartSeconds ?? 0));
  }

  /** Starts both sides of a chosen chapter together: the saved combined spot, or its start. */
  function openCombinedChapter(row: BookChapterRow) {
    const combined = alignedSide(resume?.combined);
    if (combined?.rowId === row.id && combined.reading && combined.listening) {
      continueCombined();
      return;
    }
    if (!book || !row.audioTrack || !row.readTarget) return;
    playAudiobookTrack(row.audioTrack.id, Number(row.audioStartSeconds ?? 0));
    const target = row.readTarget;
    void goto(target.kind === "epub"
      ? bookReaderHref({
          bookId: book.id,
          kind: "book",
          id: book.id,
          returnId: book.id,
          location: target.location,
          combined: true,
        })
      : bookReaderHref({
          bookId: book.id,
          kind: "chapter",
          id: target.chapterId,
          returnId: book.id,
          combined: true,
        }));
  }

  function continueReading() {
    if (resume?.exactReading) {
      openReadingTarget(resume.exactReading, { exact: true });
      return;
    }
    if (alignedReading) {
      openReadingTarget(alignedReading);
      return;
    }
    if (isSingleFileBook) {
      openSingleFileReader();
    }
  }

  function continueCombined() {
    const combined = alignedSide(resume?.combined);
    if (!combined?.reading || !combined.listening) return;
    playListeningTarget(combined.listening);
    openReadingTarget(combined.reading, { combined: true });
  }

  function switchToNewerPosition() {
    if (resume?.lastModality === CONSUMPTION_MODALITY.listening) {
      if (alignedReading) openReadingTarget(alignedReading);
      return;
    }
    if (alignedListening) playListeningTarget(alignedListening);
  }

  function listenToBook(options: { startOver?: boolean } = {}) {
    if (!book || audiobookTracks.length === 0) return;
    // Only a playing audiobook is paused in place. A loaded but paused player (including one restored
    // after a reload) may be behind a newer position recorded on another device, so resume from the
    // server's exact listening checkpoint instead.
    if (!options.startOver && isCurrentAudiobook && playback.playing && !canonicalCompleted) {
      playback.toggle();
      return;
    }

    const target = options.startOver ? null : savedAudiobookResume ?? alignedListening;
    if (target) {
      playListeningTarget(target);
      return;
    }
    const firstTrack = audiobookTracks[0];
    if (firstTrack) playAudiobookTrack(firstTrack.id, 0);
  }

  /** Marks the audiobook listened or not; the exact listening position rides along unchanged. */
  async function handleToggleListened(listened: boolean) {
    const firstTrack = audiobookTracks[0];
    const position = savedAudiobookResume ?? (firstTrack
      ? { trackEntityId: firstTrack.id, markerId: null, offsetSeconds: 0 }
      : null);
    if (!book || !position || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityProgress(book.id, {
        modality: CONSUMPTION_MODALITY.listening,
        listening: {
          trackEntityId: position.trackEntityId,
          markerId: position.markerId ?? null,
          offsetSeconds: Math.max(0, Number(position.offsetSeconds)),
        },
        completed: listened,
      });
      await detail.reload({ showLoading: false });
    } finally {
      listeningBusy = false;
    }
  }

  /** Starts listening over from the first part; the reading position is untouched. */
  async function startListeningOver() {
    const firstTrack = audiobookTracks[0];
    if (!book || !firstTrack || listeningBusy) return;
    listeningBusy = true;
    try {
      await updateEntityProgress(book.id, {
        modality: CONSUMPTION_MODALITY.listening,
        listening: { trackEntityId: firstTrack.id, markerId: null, offsetSeconds: 0 },
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
        modality: CONSUMPTION_MODALITY.reading,
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
        modality: CONSUMPTION_MODALITY.reading,
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
        modality: CONSUMPTION_MODALITY.reading,
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
        modality: CONSUMPTION_MODALITY.reading,
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

<div class="book-detail-page">
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
      allowExternalAcquisitionTab={true}
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
        {/if}
        {#if pageCount > 0}
          <span class="meta-sep"></span>
          <span class="meta-item">{pageCount} page{pageCount === 1 ? "" : "s"}</span>
        {/if}
        {#if bookActivityLabel}
          <span class="meta-sep"></span>
          <span class="meta-item">{bookActivityLabel}</span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if separateProgress}
          {#if separateProgress.readingPercent > 0}
            <UiBadge variant="outline">Read {separateProgress.readingPercent}%</UiBadge>
          {/if}
          {#if separateProgress.listeningPercent > 0}
            <UiBadge variant="outline">Listened {separateProgress.listeningPercent}%</UiBadge>
          {/if}
        {:else if canonicalPercent > 0}
          <UiBadge variant="outline">Progress {canonicalPercent}%</UiBadge>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "chapter-mapping"}
          {#key book.id}
            <BookChapterMappingEditor
              resetKey={book.id}
              {alignment}
              audioTracks={audiobookTracks}
              loadError={chapterMappingLoadError}
              onSave={saveChapterMappingDraft}
            />
          {/key}
        {:else if section.id === "acquisition"}
          {#if !card.externalLibraryProvenance}
            <EntityAcquisitionCard
              {acq}
              entity={book}
              {fileManagement}
              showEntityRequestControls={false}
              showAcquisitionPanel={false}
              onCancelled={handleAcquisitionCancelled}
              onImported={() => detail.reload({ showLoading: false })}
            />
          {/if}
          <BookRenditionAcquisitionCard
            ownership={{
              ebook: hasReadableContent,
              audiobook: audiobookTracks.length > 0,
            }}
            acquisitions={liveRenditionAcquisitions}
            monitors={bookRenditionMonitors}
            managedRenditions={card.externalLibraryProvenance?.bookRenditions ?? []}
            pendingManagerRenditions={acceptedManagerRenditions.bookId === book.id ? acceptedManagerRenditions.renditions : []}
            onRequest={requestBookRendition}
            onRequestBoth={requestBothBookRenditions}
            onToggleMonitor={toggleBookRenditionMonitor}
            onChanged={handleBookAcquisitionChanged}
          />
          {#if session.isAdmin && (!hasReadableContent || audiobookTracks.length === 0)}
            <BookManagerRequest bookId={book.id} title={book.title}
              hasEbook={hasReadableContent} hasAudiobook={audiobookTracks.length > 0}
              acquisitions={liveRenditionAcquisitions} monitors={bookRenditionMonitors}
              managedRenditions={card.externalLibraryProvenance?.bookRenditions ?? []}
              onAccepted={rendition => {
                const existing = acceptedManagerRenditions.bookId === book.id ? acceptedManagerRenditions.renditions : [];
                if (!existing.includes(rendition))
                  acceptedManagerRenditions = { bookId: book.id, renditions: [...existing, rendition] };
              }}
              onChanged={() => detail.reload({ showLoading: false })} />
          {/if}
        {/if}
      {/snippet}
      </EntityDetail>

    {#if hasCombinedContent}
      <BookCombinedProgressCard
        separate={separateProgress}
        progressPercent={canonicalPercent}
        progressLabel={canonicalPositionLabel}
        activityLabel={bookActivityLabel}
        primaryColor={chapterPalette.primary}
        secondaryColor={chapterPalette.secondary}
        readLabel={readAction.label}
        readHint={readAction.hint}
        listenLabel={listenAction.label}
        listenHint={listenAction.hint}
        combinedLabel={combinedAction.label}
        combinedDisabled={combinedAction.disabled}
        explanation={combinedAction.explanation}
        switchLabel={switchOffer?.label ?? null}
        switchNote={switchOffer?.note ?? null}
        onRead={continueReading}
        onListen={() => listenToBook()}
        onCombined={continueCombined}
        onSwitch={switchToNewerPosition}
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
          countLabel={`${audioPartCount} part${audioPartCount === 1 ? "" : "s"}`}
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
        onCombined={separateProgress ? undefined : openCombinedChapter}
      />
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
  .book-detail-page {
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

</style>
