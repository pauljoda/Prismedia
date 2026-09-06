<script lang="ts">
  import { Button, Checkbox } from "@prismedia/ui-svelte";
  import {
    PAGE_READING_DIRECTION,
    READER_MODE,
    type PageReadingDirectionCode,
  } from "$lib/api/generated/codes";
  import { browser } from "$app/environment";
  import { tick, untrack, type Snippet } from "svelte";
  import {
    BookOpen,
    Columns2,
    Rows3,
    ChevronLeft,
    ChevronRight,
    Image as ImageIcon,
  } from "@lucide/svelte";
  import { apiAssetUrl as toApiUrl, apiPath } from "$lib/api/orval-fetch";
  import type { ImageListItemDto } from "$lib/entities/media-view-models";
  import ReaderShell from "$lib/components/reader/ReaderShell.svelte";
  import NsfwBlur from "./nsfw/NsfwBlur.svelte";
  import {
    comicPreloadIndexes,
    comicSpreadForIndex,
    comicTapZone,
    nextComicIndex,
    previousComicIndex,
    type ComicPageMode,
  } from "./comic-reader";

  type ReaderMode = typeof READER_MODE.paged | typeof READER_MODE.webtoon;

  interface ReaderPointerGesture {
    pointerId: number;
    startX: number;
    startY: number;
    moved: boolean;
  }

  interface Props {
    images: ImageListItemDto[];
    initialIndex: number;
    initialMode?: ReaderMode;
    readingDirection?: PageReadingDirectionCode;
    coverOrdinal?: number | null;
    doublePageOrdinals?: readonly number[];
    title?: string;
    nextChapterLabel?: string | null;
    presentation?: "overlay" | "page";
    closeIcon?: "close" | "back";
    onClose: () => void;
    onIndexChange?: (index: number) => void;
    onModeChange?: (mode: ReaderMode) => void;
    onNextChapter?: () => void | Promise<void>;
    /** Optional transport from a companion rendition, such as the matched audiobook. */
    companionControls?: Snippet;
  }

  let {
    images,
    initialIndex,
    initialMode = READER_MODE.paged,
    readingDirection = PAGE_READING_DIRECTION.leftToRight,
    coverOrdinal,
    doublePageOrdinals = [],
    title = "Comic",
    nextChapterLabel = null,
    presentation = "overlay",
    closeIcon = "close",
    onClose,
    onIndexChange,
    onModeChange,
    onNextChapter,
    companionControls,
  }: Props = $props();

  let shell = $state<ReturnType<typeof ReaderShell>>();
  let readerMode = $state<ReaderMode>(untrack(() => initialMode));
  let pageMode = $state<ComicPageMode>("single");
  let firstPageIsCover = $state(untrack(() => coverOrdinal === undefined || coverOrdinal === 0));
  let index = $state(untrack(() => initialIndex));
  let webtoonStage: HTMLElement | undefined = $state();
  let programmaticWebtoonScroll = false;
  let nextChapterBusy = $state(false);
  let readerPointerGesture: ReaderPointerGesture | null = null;
  const warmedImages = new Map<string, HTMLImageElement>();

  const rightToLeft = $derived(readingDirection === PAGE_READING_DIRECTION.rightToLeft);
  const readerOptions = $derived({
    pageMode,
    firstPageIsCover,
    singlePageIndexes: [...new Set([
      ...doublePageOrdinals,
      ...(coverOrdinal == null ? [] : [coverOrdinal]),
    ])],
  });
  const hasNextChapter = $derived(Boolean(onNextChapter));
  const hasEndAction = $derived(images.length > 0);
  const nextChapterTitle = $derived(nextChapterLabel?.trim() ? nextChapterLabel : "Next chapter");
  const chapterEndTitle = $derived(hasNextChapter ? nextChapterTitle : "No next chapter");
  const chapterEndActionLabel = $derived(hasNextChapter ? "Continue reading" : "Close reader");
  const finalPageIndex = $derived(hasEndAction ? images.length : -1);
  const showingChapterEndPage = $derived(
    readerMode === READER_MODE.paged && hasEndAction && index === finalPageIndex,
  );
  const spread = $derived(
    showingChapterEndPage
      ? []
      : comicSpreadForIndex(index, images.length, readerOptions),
  );
  const displayedSpread = $derived(rightToLeft ? [...spread].reverse() : spread);
  const counterText = $derived(
    showingChapterEndPage
      ? chapterEndTitle
      : spread.length > 1
      ? `${spread[0] + 1}-${spread[spread.length - 1] + 1} / ${images.length}`
      : `${Math.min(index + 1, images.length)} / ${images.length}`,
  );
  const preloadSources = $derived(
    comicPreloadIndexes(index, images.length, readerOptions)
      .map((pageIndex) => images[pageIndex])
      .map((image) => (image ? imageSrc(image) : ""))
      .filter(Boolean),
  );
  const visibleSources = $derived(
    spread
      .map((pageIndex) => images[pageIndex])
      .map((image) => (image ? imageSrc(image) : ""))
      .filter(Boolean),
  );
  const warmSources = $derived([...new Set([...visibleSources, ...preloadSources])]);

  function setReaderIndex(nextIndex: number) {
    const maxIndex =
      readerMode === READER_MODE.paged && hasEndAction ? images.length : Math.max(0, images.length - 1);
    const clampedIndex = Math.max(0, Math.min(nextIndex, maxIndex));
    if (clampedIndex === index) return;
    index = clampedIndex;
    if (index < images.length) {
      onIndexChange?.(index);
    } else {
      reportReadableEnd();
    }
  }

  function setReaderMode(mode: ReaderMode) {
    if (mode === readerMode) return;
    readerMode = mode;
    if (mode === READER_MODE.webtoon && index >= images.length) {
      setReaderIndex(lastReadableIndex());
    }
    onModeChange?.(mode);
  }

  function goNext() {
    if (showingChapterEndPage) {
      void goChapterEndAction();
      return;
    }
    if (hasEndAction && isLastReadableSpread()) {
      setReaderIndex(finalPageIndex);
      return;
    }
    setReaderIndex(nextComicIndex(index, images.length, readerOptions));
  }

  function goPrev() {
    if (showingChapterEndPage) {
      setReaderIndex(lastReadableIndex());
      return;
    }
    setReaderIndex(previousComicIndex(index, images.length, readerOptions));
  }

  function lastReadableIndex() {
    return Math.max(0, images.length - 1);
  }

  function isLastReadableSpread() {
    if (images.length <= 0) return true;
    const visibleSpread = comicSpreadForIndex(index, images.length, readerOptions);
    return (visibleSpread.at(-1) ?? index) >= images.length - 1;
  }

  function reportReadableEnd() {
    if (images.length <= 0) return;
    onIndexChange?.(lastReadableIndex());
  }

  async function goChapterEndAction() {
    if (hasNextChapter) {
      await goNextChapter();
      return;
    }
    reportReadableEnd();
    onClose();
  }

  async function goNextChapter() {
    if (!onNextChapter || !hasNextChapter || nextChapterBusy) return;
    reportReadableEnd();
    nextChapterBusy = true;
    try {
      await onNextChapter();
      index = 0;
      shell?.showControls();
      if (readerMode === READER_MODE.webtoon) {
        void scrollWebtoonToIndex(0);
      }
    } finally {
      nextChapterBusy = false;
    }
  }

  function imageSrc(image: ImageListItemDto) {
    const path = image.fullPath ?? image.thumbnailPath;
    if (path?.startsWith("/api/")) return apiPath(path);
    return toApiUrl(path) ?? "";
  }

  function warmImage(src: string) {
    if (!browser || !src || warmedImages.has(src)) return;

    const img = new Image();
    warmedImages.set(src, img);
    img.decoding = "async";
    img.loading = "eager";
    img.onerror = () => {
      warmedImages.delete(src);
    };
    img.src = src;

    if (typeof img.decode === "function") {
      void img.decode().catch(() => undefined);
    }
  }

  // A committed swipe must travel at least this far; shorter drags are neither a
  // tap nor a swipe and are ignored so a slightly imprecise tap does nothing.
  const READER_SWIPE_THRESHOLD = 50;

  function handleReaderPointerDown(event: PointerEvent) {
    if ((event.target as HTMLElement).closest("[data-reader-control]")) return;
    readerPointerGesture = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      moved: false,
    };
    // Capture so a paged swipe that drifts off the stage still resolves here.
    // Webtoon mode scrolls natively, so it must not capture the pointer.
    if (readerMode === READER_MODE.paged && event.pointerType !== "mouse") {
      (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
    }
  }

  function handleReaderPointerMove(event: PointerEvent) {
    if (!readerPointerGesture || readerPointerGesture.pointerId !== event.pointerId) return;
    const deltaX = event.clientX - readerPointerGesture.startX;
    const deltaY = event.clientY - readerPointerGesture.startY;
    if (Math.hypot(deltaX, deltaY) > 10) {
      readerPointerGesture.moved = true;
    }
  }

  function clearReaderPointerGesture() {
    readerPointerGesture = null;
  }

  function handleReaderPointerUp(event: PointerEvent) {
    const target = event.target as HTMLElement;
    if (target.closest("[data-reader-control]")) {
      clearReaderPointerGesture();
      return;
    }
    const gesture = readerPointerGesture;
    clearReaderPointerGesture();

    // Swipe detection needs the gesture's start point. A pointerup without a tracked
    // pointerdown (e.g. a synthetic mouse click) still falls through to the tap zones.
    if (gesture && gesture.pointerId === event.pointerId) {
      const dx = event.clientX - gesture.startX;
      const dy = event.clientY - gesture.startY;
      const absX = Math.abs(dx);
      const absY = Math.abs(dy);

      // Touch swipes (paged mode only): horizontal turns the page, a downward swipe
      // dismisses the reader — matching the lightbox gestures. Webtoon mode scrolls
      // vertically, so it keeps tap-only navigation.
      if (
        readerMode === READER_MODE.paged &&
        event.pointerType !== "mouse" &&
        Math.max(absX, absY) > READER_SWIPE_THRESHOLD
      ) {
        if (absX > absY * 1.3) {
          if ((dx < 0) !== rightToLeft) goNext();
          else goPrev();
          return;
        }
        if (absY > absX * 1.3 && dy > 0) {
          onClose();
          return;
        }
        return;
      }

      // Moved past the tap slop but didn't commit to a swipe — ignore it.
      if (gesture.moved) return;
    }

    // Tap zones: left/right turn pages on touch, centre toggles controls.
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const zone = comicTapZone(event.clientX - rect.left, rect.width);
    if (event.pointerType === "mouse") {
      if (zone === "controls") shell?.toggleControls();
      return;
    }
    if (zone === "previous") rightToLeft ? goNext() : goPrev();
    else if (zone === "next") rightToLeft ? goPrev() : goNext();
    else shell?.toggleControls();
  }

  function handleWebtoonScroll(event: Event) {
    if (programmaticWebtoonScroll) return;
    const stage = event.currentTarget as HTMLElement;
    const anchor = stage.scrollTop + stage.clientHeight * 0.45;
    let nextIndex = index;
    for (const page of stage.querySelectorAll<HTMLElement>("[data-comic-page-index]")) {
      if (page.offsetTop <= anchor) {
        nextIndex = Number(page.dataset.comicPageIndex ?? nextIndex);
      }
    }
    setReaderIndex(nextIndex);
  }

  async function scrollWebtoonToIndex(targetIndex: number) {
    await tick();
    if (!webtoonStage) return;
    const target = webtoonStage.querySelector<HTMLElement>(
      `[data-comic-page-index="${targetIndex}"]`,
    );
    if (!target) return;
    programmaticWebtoonScroll = true;
    if (typeof webtoonStage.scrollTo === "function") {
      webtoonStage.scrollTo({ top: target.offsetTop, behavior: "auto" });
    } else {
      webtoonStage.scrollTop = target.offsetTop;
    }
    queueMicrotask(() => {
      programmaticWebtoonScroll = false;
    });
  }

  $effect(() => {
    if (readerMode !== READER_MODE.webtoon) return;
    webtoonStage;
    const targetIndex = untrack(() => index);
    void scrollWebtoonToIndex(targetIndex);
  });

  $effect(() => {
    if (!browser) return;

    const desiredSources = new Set(warmSources);
    for (const src of desiredSources) {
      warmImage(src);
    }
    for (const src of warmedImages.keys()) {
      if (!desiredSources.has(src)) {
        warmedImages.delete(src);
      }
    }
  });
</script>

<svelte:head>
  {#each preloadSources as src (src)}
    <link rel="preload" as="image" href={src} />
  {/each}
</svelte:head>

<ReaderShell
  bind:this={shell}
  {title}
  {presentation}
  {closeIcon}
  {onClose}
  onPrev={rightToLeft ? goNext : goPrev}
  onNext={rightToLeft ? goPrev : goNext}
  onActivate={goNext}
>
  {#snippet counter()}{counterText}{/snippet}

  {#snippet controls()}
    {#if companionControls}
      {@render companionControls()}
    {/if}
    <div class="flex items-center gap-1">
      <Button variant="outline" size="sm"
        type="button"
        onclick={() => setReaderMode(READER_MODE.paged)}
        aria-pressed={readerMode === READER_MODE.paged}
        class="aria-pressed:bg-accent aria-pressed:text-accent-foreground"
        aria-label="Paged reader"
        title="Paged reader"
      >
        <BookOpen class="h-4 w-4" />
        <span class="hidden sm:inline">Paged</span>
      </Button>
      <Button variant="outline" size="sm"
        type="button"
        onclick={() => setReaderMode(READER_MODE.webtoon)}
        aria-pressed={readerMode === READER_MODE.webtoon}
        class="aria-pressed:bg-accent aria-pressed:text-accent-foreground"
        aria-label="Webtoon reader"
        title="Webtoon reader"
      >
        <Rows3 class="h-4 w-4" />
        <span class="hidden sm:inline">Webtoon</span>
      </Button>
    </div>

    {#if readerMode === READER_MODE.paged}
      <div class="hidden items-center gap-1 border-l border-border-subtle pl-2 sm:flex">
        <Button variant="outline" size="sm"
          type="button"
          onclick={() => (pageMode = pageMode === "single" ? "double" : "single")}
          aria-pressed={pageMode === "double"}
          class="aria-pressed:bg-accent aria-pressed:text-accent-foreground"
          aria-label="Toggle one or two pages"
          title="Toggle one or two pages"
        >
          {#if pageMode === "double"}
            <Columns2 class="h-4 w-4" />
            <span>2 pages</span>
          {:else}
            <ImageIcon class="h-4 w-4" />
            <span>1 page</span>
          {/if}
        </Button>
        {#if pageMode === "double"}
          <label class="inline-flex items-center gap-2 px-2 text-xs">
            <Checkbox checked={firstPageIsCover} onchange={next => firstPageIsCover = next} aria-label="First page is cover" />
            <span>First page is cover</span>
          </label>
        {/if}
      </div>
    {/if}
  {/snippet}

  {#if readerMode === READER_MODE.webtoon}
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      bind:this={webtoonStage}
      class="reader-stage overflow-y-auto bg-black"
      onpointerdown={handleReaderPointerDown}
      onpointermove={handleReaderPointerMove}
      onpointerup={handleReaderPointerUp}
      onpointercancel={clearReaderPointerGesture}
      onscroll={handleWebtoonScroll}
    >
      <div class="mx-auto flex min-h-full w-full max-w-4xl flex-col items-center">
        {#each images as image, pageIndex (image.id)}
          <div class="w-full" data-comic-page-index={pageIndex}>
            <NsfwBlur isNsfw={false} class="w-full">
              <img
                src={imageSrc(image)}
                alt={image.title}
                class="block h-auto w-full bg-surface-1"
                loading="lazy"
                decoding="async"
              />
            </NsfwBlur>
          </div>
        {/each}
        {#if hasEndAction}
          <div class="flex w-full justify-center px-4 py-10 sm:py-14">
            <Button variant="outline" size="sm"
              type="button"
              data-reader-control
              onclick={() => void goChapterEndAction()}
              disabled={nextChapterBusy}
              class="h-auto w-[min(100%,34rem)] flex-col whitespace-normal p-5 text-center"
            >
              <span class="font-mono text-[0.62rem] uppercase tracking-[0.16em] text-text-accent">
                {hasNextChapter ? "Next Chapter" : "No next chapter"}
              </span>
              <span class="mt-2 block max-w-[26rem] truncate text-lg font-semibold text-text-primary">
                {chapterEndTitle}
              </span>
              <span class="mt-3 inline-flex items-center gap-2 text-[0.76rem] text-white/70">
                {chapterEndActionLabel}
                <ChevronRight class="h-4 w-4" />
              </span>
            </Button>
          </div>
        {/if}
      </div>
    </div>
  {:else}
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="reader-stage reader-stage-paged items-center justify-center overflow-hidden bg-black p-0 sm:px-14 sm:py-3"
      onpointerdown={handleReaderPointerDown}
      onpointermove={handleReaderPointerMove}
      onpointerup={handleReaderPointerUp}
      onpointercancel={clearReaderPointerGesture}
    >
      {#if images.length > 1 || hasEndAction}
        <Button variant="outline" size="sm"
          type="button"
          onclick={rightToLeft ? goNext : goPrev}
          data-reader-control
          class="absolute top-1/2 z-10 hidden size-11 -translate-y-1/2 sm:inline-flex left-2 sm:left-3"
          aria-label={rightToLeft ? "Next page" : "Previous page"}
          title={rightToLeft ? "Next (←)" : "Previous (←)"}
        >
          <ChevronLeft class="h-6 w-6" />
        </Button>
        <Button variant="outline" size="sm"
          type="button"
          onclick={rightToLeft ? goPrev : goNext}
          data-reader-control
          class="absolute top-1/2 z-10 hidden size-11 -translate-y-1/2 sm:inline-flex right-2 sm:right-3"
          aria-label={rightToLeft ? "Previous page" : "Next page"}
          title={rightToLeft ? "Previous (→)" : "Next (→)"}
        >
          <ChevronRight class="h-6 w-6" />
        </Button>
      {/if}

      <div
        class={`flex h-full w-full items-center justify-center gap-2 ${
          spread.length > 1 ? "max-w-7xl" : "max-w-5xl"
        }`}
      >
        {#if showingChapterEndPage}
          <div class="reader-next-chapter-page" data-reader-control>
            <div class="font-mono text-[0.64rem] uppercase tracking-[0.18em] text-text-accent">
              {hasNextChapter ? "Next Chapter" : "No next chapter"}
            </div>
            <h3 class="mt-3 max-w-[32rem] text-center font-heading text-2xl font-semibold text-text-primary sm:text-4xl">
              {chapterEndTitle}
            </h3>
            <Button variant="outline" size="sm"
              type="button"
              onclick={() => void goChapterEndAction()}
              disabled={nextChapterBusy}
              class="mt-6"
            >
              {chapterEndActionLabel}
              <ChevronRight class="h-4 w-4" />
            </Button>
          </div>
        {:else}
          {#each displayedSpread as pageIndex (pageIndex)}
            {@const image = images[pageIndex]}
            {#if image}
              <NsfwBlur isNsfw={false} class="flex h-full min-w-0 flex-1 items-center justify-center">
                <img
                  src={imageSrc(image)}
                  alt={image.title}
                  class="max-h-full max-w-full object-contain shadow-[0_0_30px_rgba(0,0,0,0.45)]"
                  loading="eager"
                  decoding="sync"
                />
              </NsfwBlur>
            {/if}
          {/each}
        {/if}
      </div>
    </div>
  {/if}
</ReaderShell>

<style>

</style>
