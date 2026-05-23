<script lang="ts">
  import { onMount, tick, untrack } from "svelte";
  import {
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

  interface Props {
    images: ImageListItemDto[];
    initialIndex: number;
    initialMode?: ReaderMode;
    title?: string;
    nextChapterLabel?: string | null;
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

  const hasNextChapter = $derived(Boolean(onNextChapter));
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

  function handleReaderTap(event: PointerEvent) {
    const target = event.target as HTMLElement;
    if (target.closest("[data-reader-control]")) return;
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

    function onKey(event: KeyboardEvent) {
      const target = event.target as HTMLElement;
      const typing =
        target.tagName === "INPUT" ||
        target.tagName === "TEXTAREA" ||
        target.isContentEditable;
      if (typing && event.key !== "Escape") return;

      switch (event.key) {
        case "Escape":
          event.preventDefault();
          onClose();
          break;
        case "ArrowLeft":
        case "h":
        case "H":
          event.preventDefault();
          goPrev();
          break;
        case "ArrowRight":
        case "l":
        case "L":
        case " ":
          event.preventDefault();
          goNext();
          break;
      }
    }

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
  class="reader-overlay fixed inset-0 flex flex-col bg-black/95 backdrop-blur-sm"
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
      aria-label="Close"
      title="Close (Esc)"
    >
      <X class="h-5 w-5" />
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
      onpointerup={handleReaderTap}
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
    <div class="reader-stage items-center justify-center overflow-hidden bg-black p-0 sm:px-14 sm:py-3" onpointerup={handleReaderTap}>
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

  <div
    data-reader-control
    class={`reader-bottom-layer ${controlsVisible ? "reader-layer-visible" : "reader-layer-hidden"}`}
  >
    <div class="flex items-center justify-between gap-2">
      <button type="button" onclick={goPrev} class="reader-mode-button">
        <ChevronLeft class="h-4 w-4" />
        Prev
      </button>
      <div class="font-mono text-[0.68rem] text-text-muted">{counterText}</div>
      <button type="button" onclick={goNext} class="reader-mode-button">
        {showingChapterEndPage ? (hasNextChapter ? "Start" : "Close") : "Next"}
        <ChevronRight class="h-4 w-4" />
      </button>
    </div>
    {#if readerMode === "paged"}
      <div class="mt-2 flex items-center justify-center gap-2">
        <button
          type="button"
          onclick={() => (pageMode = pageMode === "single" ? "double" : "single")}
          class:active-reader-control={pageMode === "double"}
          class="reader-mode-button"
        >
          {pageMode === "double" ? "2 pages" : "1 page"}
        </button>
        {#if pageMode === "double"}
          <label class="reader-check">
            <input type="checkbox" bind:checked={firstPageIsCover} />
            <span>Cover first</span>
          </label>
        {/if}
      </div>
    {/if}
  </div>
</div>

<style>
  .reader-icon-button,
  .reader-mode-button {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid rgb(255 255 255 / 0.14);
    background: rgb(0 0 0 / 0.62);
    padding: 0.45rem 0.65rem;
    color: rgb(255 255 255 / 0.78);
    font-size: 0.72rem;
    line-height: 1;
    backdrop-filter: blur(12px);
    transition:
      border-color 150ms ease,
      color 150ms ease,
      box-shadow 150ms ease;
  }

  .reader-overlay {
    z-index: 2147483000;
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

  .reader-top-layer,
  .reader-bottom-layer {
    position: absolute;
    left: 0;
    right: 0;
    z-index: 20;
    border-color: rgb(255 255 255 / 0.12);
    background: linear-gradient(
      to bottom,
      rgb(0 0 0 / 0.78),
      rgb(0 0 0 / 0.48) 68%,
      rgb(0 0 0 / 0)
    );
    padding: max(0.5rem, env(safe-area-inset-top)) 0.75rem 1.25rem;
    backdrop-filter: blur(14px);
    transition:
      opacity 180ms ease,
      transform 180ms ease;
  }

  .reader-top-layer {
    top: 0;
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .reader-bottom-layer {
    bottom: 0;
    border-top: 1px solid rgb(255 255 255 / 0.12);
    background: linear-gradient(
      to top,
      rgb(0 0 0 / 0.78),
      rgb(0 0 0 / 0.48) 68%,
      rgb(0 0 0 / 0)
    );
    padding: 1.25rem 0.75rem max(0.5rem, env(safe-area-inset-bottom));
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

  .reader-bottom-layer.reader-layer-hidden {
    transform: translateY(0.75rem);
  }

  .reader-icon-button {
    padding: 0.4rem;
  }

  .reader-mode-button:hover,
  .reader-mode-button:focus-visible,
  .reader-icon-button:hover,
  .reader-icon-button:focus-visible,
  .active-reader-control {
    border-color: rgb(196 154 90 / 0.55);
    color: rgb(250 232 198);
    box-shadow: 0 0 18px rgb(196 154 90 / 0.24);
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
    border: 1px solid rgb(255 255 255 / 0.12);
    background: rgb(0 0 0 / 0.56);
    color: rgb(255 255 255 / 0.76);
    backdrop-filter: blur(12px);
  }

  .reader-nav-button:hover,
  .reader-nav-button:focus-visible {
    border-color: rgb(196 154 90 / 0.55);
    color: rgb(250 232 198);
    box-shadow: 0 0 18px rgb(196 154 90 / 0.2);
    outline: none;
  }

  .reader-next-chapter-button,
  .reader-next-chapter-page {
    border: 1px solid rgb(196 154 90 / 0.34);
    background:
      linear-gradient(135deg, rgb(196 154 90 / 0.13), rgb(255 255 255 / 0.04)),
      rgb(13 17 23 / 0.88);
    box-shadow: 0 0 34px rgb(196 154 90 / 0.16);
    backdrop-filter: blur(16px);
  }

  .reader-next-chapter-button {
    width: min(100%, 34rem);
    padding: 1.25rem;
    text-align: center;
    transition:
      border-color 150ms ease,
      box-shadow 150ms ease,
      transform 150ms ease;
  }

  .reader-next-chapter-button:hover,
  .reader-next-chapter-button:focus-visible,
  .reader-next-chapter-action:hover,
  .reader-next-chapter-action:focus-visible {
    border-color: rgb(196 154 90 / 0.68);
    box-shadow: 0 0 30px rgb(196 154 90 / 0.26);
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
    border: 1px solid rgb(196 154 90 / 0.46);
    background: rgb(0 0 0 / 0.62);
    padding: 0.7rem 0.95rem;
    color: rgb(250 232 198);
    font-size: 0.78rem;
    font-weight: 600;
    transition:
      border-color 150ms ease,
      box-shadow 150ms ease;
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
    border: 1px solid rgb(255 255 255 / 0.14);
    background: rgb(0 0 0 / 0.62);
    padding: 0.45rem 0.65rem;
    color: rgb(255 255 255 / 0.76);
    font-size: 0.72rem;
    line-height: 1;
  }

  .reader-check input {
    accent-color: #c49a5a;
  }

  @media (min-width: 640px) {
    .reader-stage {
      inset: 3.65rem 0 0;
    }

    .reader-top-layer {
      border-bottom: 1px solid rgb(255 255 255 / 0.12);
      background: rgb(0 0 0 / 0.72);
      padding: 0.5rem 0.75rem;
    }

    .reader-bottom-layer {
      display: none;
    }

    .reader-nav-button {
      display: flex;
    }
  }
</style>
