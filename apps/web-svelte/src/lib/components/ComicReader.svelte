<script lang="ts">
  import { READER_MODE } from "$lib/api/generated/codes";
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
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
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
  let firstPageIsCover = $state(true);
  let index = $state(untrack(() => initialIndex));
  let webtoonStage: HTMLElement | undefined = $state();
  let programmaticWebtoonScroll = false;
  let nextChapterBusy = $state(false);
  let readerPointerGesture: ReaderPointerGesture | null = null;
  const warmedImages = new Map<string, HTMLImageElement>();

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
      : comicSpreadForIndex(index, images.length, { pageMode, firstPageIsCover }),
  );
  const counterText = $derived(
    showingChapterEndPage
      ? chapterEndTitle
      : spread.length > 1
      ? `${spread[0] + 1}-${spread[spread.length - 1] + 1} / ${images.length}`
      : `${Math.min(index + 1, images.length)} / ${images.length}`,
  );
  const preloadSources = $derived(
    comicPreloadIndexes(index, images.length, { pageMode, firstPageIsCover })
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
    setReaderIndex(nextComicIndex(index, images.length, { pageMode, firstPageIsCover }));
  }

  function goPrev() {
    if (showingChapterEndPage) {
      setReaderIndex(lastReadableIndex());
      return;
    }
    setReaderIndex(previousComicIndex(index, images.length, { pageMode, firstPageIsCover }));
  }

  function lastReadableIndex() {
    return Math.max(0, images.length - 1);
  }

  function isLastReadableSpread() {
    if (images.length <= 0) return true;
    const visibleSpread = comicSpreadForIndex(index, images.length, { pageMode, firstPageIsCover });
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
    return toApiUrl(image.fullPath ?? image.thumbnailPath) ?? "";
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
          if (dx < 0) goNext();
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
    if (zone === "previous") goPrev();
    else if (zone === "next") goNext();
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
  onPrev={goPrev}
  onNext={goNext}
  onActivate={goNext}
>
  {#snippet counter()}{counterText}{/snippet}

  {#snippet controls()}
    {#if companionControls}
      {@render companionControls()}
    {/if}
    <div class="flex items-center gap-1">
      <button
        type="button"
        onclick={() => setReaderMode(READER_MODE.paged)}
        class:active-reader-control={readerMode === READER_MODE.paged}
        class="reader-mode-button"
        aria-label="Paged reader"
        title="Paged reader"
      >
        <BookOpen class="h-4 w-4" />
        <span class="hidden sm:inline">Paged</span>
      </button>
      <button
        type="button"
        onclick={() => setReaderMode(READER_MODE.webtoon)}
        class:active-reader-control={readerMode === READER_MODE.webtoon}
        class="reader-mode-button"
        aria-label="Webtoon reader"
        title="Webtoon reader"
      >
        <Rows3 class="h-4 w-4" />
        <span class="hidden sm:inline">Webtoon</span>
      </button>
    </div>

    {#if readerMode === READER_MODE.paged}
      <div class="hidden items-center gap-1 border-l border-border-subtle pl-2 sm:flex">
        <button
          type="button"
          onclick={() => (pageMode = pageMode === "single" ? "double" : "single")}
          class:active-reader-control={pageMode === "double"}
          class="reader-mode-button"
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
        </button>
        {#if pageMode === "double"}
          <label class="reader-check">
            <input type="checkbox" bind:checked={firstPageIsCover} />
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
            <button
              type="button"
              data-reader-control
              onclick={() => void goChapterEndAction()}
              disabled={nextChapterBusy}
              class="reader-next-chapter-button"
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
            </button>
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
        <button
          type="button"
          onclick={goPrev}
          data-reader-control
          class="reader-nav-button left-2 sm:left-3"
          aria-label="Previous page"
          title="Previous (←)"
        >
          <ChevronLeft class="h-6 w-6" />
        </button>
        <button
          type="button"
          onclick={goNext}
          data-reader-control
          class="reader-nav-button right-2 sm:right-3"
          aria-label="Next page"
          title="Next (→)"
        >
          <ChevronRight class="h-6 w-6" />
        </button>
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
            <button
              type="button"
              onclick={() => void goChapterEndAction()}
              disabled={nextChapterBusy}
              class="reader-next-chapter-action"
            >
              {chapterEndActionLabel}
              <ChevronRight class="h-4 w-4" />
            </button>
          </div>
        {:else}
          {#each spread as pageIndex (pageIndex)}
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
  .reader-mode-button {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    background: var(--color-overlay-heavy);
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-sm);
    color: var(--color-text-secondary);
    font-size: 0.72rem;
    line-height: 1;
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-mode-button:hover,
  .reader-mode-button:focus-visible,
  .active-reader-control {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  .reader-stage {
    position: absolute;
    inset: 0;
    display: flex;
    min-height: 0;
    flex: 1 1 auto;
    touch-action: manipulation;
  }

  /* Paged mode has no native scroll, so claim every touch gesture for our
     swipe handlers instead of letting the browser pan or navigate back. */
  .reader-stage-paged {
    touch-action: none;
  }

  .reader-nav-button {
    position: absolute;
    top: 50%;
    z-index: 10;
    display: none;
    height: 2.75rem;
    width: 2.75rem;
    transform: translateY(-50%);
    align-items: center;
    justify-content: center;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    color: var(--color-text-secondary);
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-nav-button:hover,
  .reader-nav-button:focus-visible {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  .reader-next-chapter-button,
  .reader-next-chapter-page {
    border: 1px solid var(--color-border-accent);
    border-radius: var(--radius-md);
    background:
      linear-gradient(135deg, var(--color-overlay-glass-accent), rgba(255, 255, 255, 0.04)),
      var(--color-overlay-heavy);
    box-shadow: var(--shadow-glow-accent);
    backdrop-filter: blur(var(--glass-blur-md));
  }

  .reader-next-chapter-button {
    width: min(100%, 34rem);
    padding: 1.25rem;
    text-align: center;
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical),
      transform var(--duration-normal) var(--ease-mechanical);
  }

  .reader-next-chapter-button:hover,
  .reader-next-chapter-button:focus-visible,
  .reader-next-chapter-action:hover,
  .reader-next-chapter-action:focus-visible {
    border-color: var(--color-border-accent-strong);
    box-shadow: var(--shadow-glow-accent-strong);
    outline: none;
  }

  .reader-next-chapter-button:hover,
  .reader-next-chapter-button:focus-visible {
    transform: translateY(-1px);
  }

  .reader-next-chapter-page {
    display: flex;
    min-height: min(32rem, 72vh);
    width: min(100%, 44rem);
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 2rem;
    text-align: center;
  }

  .reader-next-chapter-action {
    margin-top: 1.5rem;
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    border: 1px solid var(--color-border-accent);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    padding: 0.7rem 0.95rem;
    color: var(--color-text-accent-bright);
    font-size: 0.78rem;
    font-weight: 600;
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-next-chapter-button:disabled,
  .reader-next-chapter-action:disabled {
    cursor: wait;
    opacity: 0.65;
  }

  .reader-check {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    padding: 0.45rem 0.65rem;
    color: var(--color-text-secondary);
    font-size: 0.72rem;
    line-height: 1;
  }

  .reader-check input {
    accent-color: var(--color-accent-500);
  }

  @media (min-width: 640px) {
    .reader-nav-button {
      display: flex;
    }
  }
</style>
