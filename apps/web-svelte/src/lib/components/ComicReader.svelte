<script lang="ts">
  import { onMount, tick, untrack } from "svelte";
  import {
    ArrowLeft,
    BookOpen,
    Columns2,
    Rows3,
    ChevronLeft,
    ChevronRight,
    Image as ImageIcon,
    X,
  } from "@lucide/svelte";
  import type { ImageListItemDto } from "@prismedia/contracts";
  import { fade } from "svelte/transition";
  import { dur, ease } from "@prismedia/ui-svelte";
  import { createNavigationKeyHandler } from "$lib/keyboard/navigation-keyboard";
  import { portal } from "$lib/actions/portal";
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
  import NsfwBlur from "./nsfw/NsfwBlur.svelte";
  import {
    comicPreloadIndexes,
    comicSpreadForIndex,
    comicTapZone,
    nextComicIndex,
    previousComicIndex,
    type ComicPageMode,
  } from "./comic-reader";

  type ReaderMode = "paged" | "webtoon";

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
  }

  let {
    images,
    initialIndex,
    initialMode = "paged",
    title = "Comic",
    nextChapterLabel = null,
    presentation = "overlay",
    closeIcon = "close",
    onClose,
    onIndexChange,
    onModeChange,
    onNextChapter,
  }: Props = $props();

  let readerMode = $state<ReaderMode>(untrack(() => initialMode));
  let pageMode = $state<ComicPageMode>("single");
  let firstPageIsCover = $state(true);
  let index = $state(untrack(() => initialIndex));
  let controlsVisible = $state(true);
  let controlsTimer: number | null = null;
  let webtoonStage: HTMLElement | undefined = $state();
  let programmaticWebtoonScroll = false;
  let nextChapterBusy = $state(false);
  let readerPointerGesture: ReaderPointerGesture | null = null;

  const hasNextChapter = $derived(Boolean(onNextChapter));
  const closeLabel = $derived(closeIcon === "back" ? "Back" : "Close");
  const closeTitle = $derived(closeIcon === "back" ? "Back (Esc)" : "Close (Esc)");
  const hasEndAction = $derived(images.length > 0);
  const nextChapterTitle = $derived(nextChapterLabel?.trim() ? nextChapterLabel : "Next chapter");
  const chapterEndTitle = $derived(hasNextChapter ? nextChapterTitle : "No next chapter");
  const chapterEndActionLabel = $derived(hasNextChapter ? "Continue reading" : "Close reader");
  const finalPageIndex = $derived(hasEndAction ? images.length : -1);
  const showingChapterEndPage = $derived(
    readerMode === "paged" && hasEndAction && index === finalPageIndex,
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

  function setReaderIndex(nextIndex: number) {
    const maxIndex =
      readerMode === "paged" && hasEndAction ? images.length : Math.max(0, images.length - 1);
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
    if (mode === "webtoon" && index >= images.length) {
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
      showControlsTemporarily();
      if (readerMode === "webtoon") {
        void scrollWebtoonToIndex(0);
      }
    } finally {
      nextChapterBusy = false;
    }
  }

  function imageSrc(image: ImageListItemDto) {
    return toApiUrl(image.fullPath ?? image.thumbnailPath) ?? "";
  }

  function clearControlsTimer() {
    if (!controlsTimer) return;
    window.clearTimeout(controlsTimer);
    controlsTimer = null;
  }

  function showControlsTemporarily() {
    controlsVisible = true;
    clearControlsTimer();
    controlsTimer = window.setTimeout(() => {
      controlsVisible = false;
      controlsTimer = null;
    }, 2800);
  }

  function toggleControls() {
    if (controlsVisible) {
      controlsVisible = false;
      clearControlsTimer();
    } else {
      showControlsTemporarily();
    }
  }

  function handleReaderPointerDown(event: PointerEvent) {
    if ((event.target as HTMLElement).closest("[data-reader-control]")) return;
    readerPointerGesture = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      moved: false,
    };
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

  function handleReaderTap(event: PointerEvent) {
    const target = event.target as HTMLElement;
    if (target.closest("[data-reader-control]")) return;
    const gesture = readerPointerGesture;
    clearReaderPointerGesture();
    if (gesture && gesture.pointerId === event.pointerId && gesture.moved) return;
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const zone = comicTapZone(event.clientX - rect.left, rect.width);
    if (event.pointerType === "mouse") {
      if (zone === "controls") toggleControls();
      return;
    }
    if (zone === "previous") goPrev();
    else if (zone === "next") goNext();
    else toggleControls();
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
    if (readerMode !== "webtoon") return;
    webtoonStage;
    const targetIndex = untrack(() => index);
    void scrollWebtoonToIndex(targetIndex);
  });

  onMount(() => {
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const onKey = createNavigationKeyHandler({
      close: onClose,
      prev: goPrev,
      next: goNext,
      extraKeys: { " ": () => goNext() },
    });

    window.addEventListener("keydown", onKey);
    showControlsTemporarily();
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
      clearControlsTimer();
    };
  });
</script>

<svelte:head>
  {#each preloadSources as src (src)}
    <link rel="preload" as="image" href={src} />
  {/each}
</svelte:head>

<div
  use:portal
  class={`reader-overlay fixed inset-0 flex flex-col bg-black backdrop-blur-sm ${presentation === "page" ? "reader-page-presentation" : ""}`}
  role="dialog"
  aria-modal="true"
  in:fade={{ duration: dur.normal, easing: ease.enter }}
  out:fade={{ duration: dur.fast, easing: ease.exit }}
>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    data-reader-hover-zone="top"
    class="reader-hover-zone reader-hover-zone-top"
    onpointerenter={showControlsTemporarily}
  ></div>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    data-reader-hover-zone="bottom"
    class="reader-hover-zone reader-hover-zone-bottom"
    onpointerenter={showControlsTemporarily}
  ></div>

  <div
    data-reader-control
    class={`reader-top-layer ${controlsVisible ? "reader-layer-visible" : "reader-layer-hidden"}`}
  >
    <button
      type="button"
      onclick={onClose}
      class="reader-icon-button"
      aria-label={closeLabel}
      title={closeTitle}
    >
      {#if closeIcon === "back"}
        <ArrowLeft class="h-5 w-5" />
      {:else}
        <X class="h-5 w-5" />
      {/if}
    </button>

    <div class="min-w-0 flex-1">
      <h2 class="truncate text-sm font-medium text-text-primary">{title}</h2>
      <div class="font-mono text-[0.6rem] uppercase tracking-[0.14em] text-text-muted">
        {counterText}
      </div>
    </div>

    <div class="flex items-center gap-1">
      <button
        type="button"
        onclick={() => setReaderMode("paged")}
        class:active-reader-control={readerMode === "paged"}
        class="reader-mode-button"
        aria-label="Paged reader"
        title="Paged reader"
      >
        <BookOpen class="h-4 w-4" />
        <span class="hidden sm:inline">Paged</span>
      </button>
      <button
        type="button"
        onclick={() => setReaderMode("webtoon")}
        class:active-reader-control={readerMode === "webtoon"}
        class="reader-mode-button"
        aria-label="Webtoon reader"
        title="Webtoon reader"
      >
        <Rows3 class="h-4 w-4" />
        <span class="hidden sm:inline">Webtoon</span>
      </button>
    </div>

    {#if readerMode === "paged"}
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
  </div>

  {#if readerMode === "webtoon"}
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      bind:this={webtoonStage}
      class="reader-stage overflow-y-auto bg-black"
      onpointerdown={handleReaderPointerDown}
      onpointermove={handleReaderPointerMove}
      onpointerup={handleReaderTap}
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
      class="reader-stage items-center justify-center overflow-hidden bg-black p-0 sm:px-14 sm:py-3"
      onpointerdown={handleReaderPointerDown}
      onpointermove={handleReaderPointerMove}
      onpointerup={handleReaderTap}
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
                  decoding="async"
                />
              </NsfwBlur>
            {/if}
          {/each}
        {/if}
      </div>
    </div>
  {/if}
</div>

<style>
  .reader-icon-button,
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

  .reader-overlay {
    z-index: 2147483000;
    width: 100vw;
    height: 100vh;
    min-height: 100vh;
    overflow: hidden;
  }

  @supports (height: 100lvh) {
    .reader-overlay {
      height: 100lvh;
      min-height: 100lvh;
    }
  }

  .reader-stage {
    position: absolute;
    inset: 0;
    display: flex;
    min-height: 0;
    flex: 1 1 auto;
    touch-action: manipulation;
  }

  .reader-hover-zone {
    position: absolute;
    left: 0;
    right: 0;
    z-index: 15;
    height: 5rem;
  }

  .reader-hover-zone-top {
    top: 0;
  }

  .reader-hover-zone-bottom {
    bottom: 0;
  }

  .reader-top-layer {
    position: absolute;
    left: 0;
    right: 0;
    z-index: 20;
    border-color: var(--color-border-default);
    background: linear-gradient(
      to bottom,
      var(--color-overlay-heavy),
      rgba(7, 8, 11, 0.48) 68%,
      transparent
    );
    padding: max(0.5rem, env(safe-area-inset-top)) 0.75rem 1.25rem;
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      opacity var(--duration-normal) var(--ease-mechanical),
      transform var(--duration-normal) var(--ease-mechanical);
  }

  .reader-top-layer {
    top: 0;
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .reader-layer-visible {
    opacity: 1;
    pointer-events: auto;
    transform: translateY(0);
  }

  .reader-layer-hidden {
    opacity: 0;
    pointer-events: none;
  }

  .reader-top-layer.reader-layer-hidden {
    transform: translateY(-0.75rem);
  }

  .reader-icon-button {
    padding: 0.4rem;
  }

  .reader-mode-button:hover,
  .reader-mode-button:focus-visible,
  .reader-icon-button:hover,
  .reader-icon-button:focus-visible,
  .active-reader-control {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
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
    .reader-stage {
      inset: 0;
    }

    .reader-top-layer {
      border-bottom: 1px solid var(--color-border-default);
      background: var(--color-overlay-glass);
      padding: 0.5rem 0.75rem;
    }

    .reader-nav-button {
      display: flex;
    }
  }
</style>
