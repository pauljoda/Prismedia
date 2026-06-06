<script lang="ts">
  import { browser } from "$app/environment";
  import { onMount, untrack, type Snippet } from "svelte";
  import {
    ChevronLeft,
    ChevronRight,
    Download,
    Info,
    RotateCcw,
    Star,
    Volume2,
    VolumeX,
    X,
    ZoomIn,
    ZoomOut,
  } from "@lucide/svelte";
  import { Button, buttonVariants, cn, dur, ease } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";
  import { getCapability } from "$lib/api/capabilities";
  import { positiveNumberValue } from "$lib/utils/format";
  import { createNavigationKeyHandler } from "$lib/keyboard/navigation-keyboard";
  import { portal } from "$lib/actions/portal";
  import { CAPABILITY_KIND } from "$lib/entities/entity-codes";
  import NsfwBlur from "./nsfw/NsfwBlur.svelte";
  import VideoPlayer, { type VideoPlayerHandle } from "./VideoPlayer.svelte";
  import {
    buildLightboxImageSource,
    buildLightboxPreloadSources,
    buildLightboxVideoSources,
    isLightboxVideoCapable,
    type UniversalLightboxEntity,
  } from "./universal-lightbox-media";

  interface Props {
    entities: UniversalLightboxEntity[];
    initialIndex: number;
    onClose: () => void;
    onIndexChange?: (index: number) => void;
    onRatingChange?: (entityId: string, rating: number | null) => void;
    detailsContent?: Snippet<[UniversalLightboxEntity]>;
    sharedKey?: string;
    showRatingControls?: boolean;
  }

  interface WarmedImageDimensions {
    width: number;
    height: number;
  }

  let {
    entities,
    initialIndex,
    onClose,
    onIndexChange,
    onRatingChange,
    detailsContent,
    showRatingControls = true,
  }: Props = $props();

  const MIN_SCALE = 0.3;
  const MAX_SCALE = 8;
  const DOUBLE_TAP_MS = 300;

  let index = $state(untrack(() => initialIndex));
  let scale = $state(1);
  let translateX = $state(0);
  let translateY = $state(0);
  let fitScale = $state(1);
  let infoOpen = $state(false);
  let ready = $state(false);
  let naturalW = $state(0);
  let naturalH = $state(0);
  let ratingOverrides = $state<Record<string, number | null>>({});
  let stageEl: HTMLDivElement | undefined = $state();
  let imageEl: HTMLImageElement | undefined = $state();
  let videoPlayerHandle: VideoPlayerHandle | undefined = $state();
  let videoMuted = $state(true);
  let videoReady = $state(false);
  let videoIntrinsicW = $state(0);
  let videoIntrinsicH = $state(0);
  let warmedImages = $state<Record<string, WarmedImageDimensions>>({});
  let stripThumbEls: Array<HTMLButtonElement | undefined> = $state([]);
  let pointerStart: { x: number; y: number; t: number } | null = null;
  let panning = $state(false);
  let lastTapAt = 0;
  let activeMediaKey: string | null = null;
  const pendingImageWarmers = new Map<string, HTMLImageElement>();
  // When a swipe over a video is consumed as navigation/dismiss, the browser
  // still fires a trailing click that would otherwise toggle play. This flag
  // lets a capture-phase click handler swallow exactly that one click.
  let suppressStageClick = false;

  const current = $derived(entities[index] ?? null);
  const currentTechnical = $derived(current ? getCapability(current.capabilities, CAPABILITY_KIND.technical) : null);
  const currentRating = $derived.by(() => {
    if (!current) return null;
    const override = ratingOverrides[current.id];
    if (override !== undefined) return override;
    const rating = getCapability(current.capabilities, CAPABILITY_KIND.rating)?.value ?? current.rating ?? null;
    return rating == null ? null : Number(rating);
  });
  const currentImageSource = $derived(current ? buildLightboxImageSource(current) : null);
  const isCurrentVideo = $derived(current ? isLightboxVideoCapable(current) : false);
  const currentVideoSources = $derived(current ? buildLightboxVideoSources(current, { preferOriginal: true }) : []);
  const primaryVideoSource = $derived(currentVideoSources[0] ?? null);
  const currentMediaKey = $derived(current ? `${current.id}:${currentImageSource?.src ?? primaryVideoSource?.src ?? ""}` : null);
  const hasCurrentVideoPlayback = $derived(Boolean(isCurrentVideo && primaryVideoSource));
  const primaryVideoCodec = $derived(primaryVideoSource?.quality === "original" ? currentTechnical?.codec : null);
  const currentVideoFit = $derived.by(() => {
    const width =
      positiveNumberValue(currentTechnical?.width) ??
      positiveNumberValue(videoIntrinsicW) ??
      positiveNumberValue(current?.initialAspectRatio?.width);
    const height =
      positiveNumberValue(currentTechnical?.height) ??
      positiveNumberValue(videoIntrinsicH) ??
      positiveNumberValue(current?.initialAspectRatio?.height);
    if (!width || !height) return null;

    return {
      aspectRatio: `${width} / ${height}`,
      widthToHeightRatio: width / height,
    };
  });
  const fallbackPoster = $derived(current?.coverUrl ?? undefined);
  const preloadSources = $derived(buildLightboxPreloadSources(entities, index, { preferOriginal: true }));
  const preloadImageSources = $derived(
    preloadSources.filter((source) => source.as === "image").map((source) => source.src),
  );
  const counterText = $derived(`${index + 1} / ${entities.length}`);
  const canOpenDetails = $derived(Boolean(detailsContent && current));
  const keyboardHintText = $derived.by(() => {
    const ratingHint = showRatingControls ? " · 1-5 rate" : "";
    return canOpenDetails
      ? `← → navigate · +/- zoom · 0 reset · i details${ratingHint} · esc close`
      : `← → navigate · +/- zoom · 0 reset${ratingHint} · esc close`;
  });
  const downloadHref = $derived(currentImageSource?.src ?? primaryVideoSource?.src ?? undefined);

  $effect(() => {
    onIndexChange?.(index);
  });

  $effect(() => {
    const el = stripThumbEls[index];
    if (!el?.scrollIntoView) return;
    el.scrollIntoView({ inline: "center", block: "nearest", behavior: "smooth" });
  });

  $effect(() => {
    if (!current) return;
    if (activeMediaKey === currentMediaKey) return;
    activeMediaKey = currentMediaKey;

    const warmed = !isCurrentVideo && currentImageSource
      ? untrack(() => warmedImages[currentImageSource.src])
      : undefined;
    ready = isCurrentVideo || Boolean(warmed);
    naturalW = positiveNumberValue(currentTechnical?.width) ?? warmed?.width ?? 0;
    naturalH = positiveNumberValue(currentTechnical?.height) ?? warmed?.height ?? 0;
    translateX = 0;
    translateY = 0;
    scale = 1;
    fitScale = 1;
    videoMuted = true;
    videoReady = false;
    videoIntrinsicW = 0;
    videoIntrinsicH = 0;
    videoPlayerHandle = undefined;
    if (warmed) {
      scheduleFitForCurrentMedia(currentMediaKey);
    }
  });

  $effect(() => {
    if (!browser) return;
    for (const src of preloadImageSources) {
      warmImage(src);
    }
  });

  $effect(() => {
    if (!current || isCurrentVideo || ready || !currentImageSource) return;
    const warmed = warmedImages[currentImageSource.src];
    if (!warmed) return;
    naturalW = naturalW || positiveNumberValue(currentTechnical?.width) || warmed.width;
    naturalH = naturalH || positiveNumberValue(currentTechnical?.height) || warmed.height;
    scheduleFitForCurrentMedia(currentMediaKey);
  });

  function goPrev() {
    if (entities.length === 0) return;
    index = (index - 1 + entities.length) % entities.length;
  }

  function goNext() {
    if (entities.length === 0) return;
    index = (index + 1) % entities.length;
  }

  function resetTransform() {
    scale = fitScale || 1;
    translateX = 0;
    translateY = 0;
  }

  function clampTranslate() {
    if (!stageEl || !naturalW || !naturalH) return;
    const rect = stageEl.getBoundingClientRect();
    const dispW = naturalW * scale;
    const dispH = naturalH * scale;
    const maxX = Math.max(0, (dispW - rect.width) / 2);
    const maxY = Math.max(0, (dispH - rect.height) / 2);
    const marginX = Math.max(40, rect.width * 0.1);
    const marginY = Math.max(40, rect.height * 0.1);
    translateX = Math.min(maxX + marginX, Math.max(-(maxX + marginX), translateX));
    translateY = Math.min(maxY + marginY, Math.max(-(maxY + marginY), translateY));
  }

  function applyFit() {
    if (!stageEl || !naturalW || !naturalH) return;
    const rect = stageEl.getBoundingClientRect();
    const next = Math.min((rect.width * 0.98) / naturalW, (rect.height * 0.98) / naturalH, MAX_SCALE);
    fitScale = next;
    scale = next;
    translateX = 0;
    translateY = 0;
    ready = true;
  }

  function scheduleFitForCurrentMedia(mediaKey: string | null) {
    if (!mediaKey) return;
    queueMicrotask(() => {
      if (activeMediaKey !== mediaKey) return;
      applyFit();
    });
  }

  function rememberWarmedImage(src: string | null | undefined, width: number, height: number) {
    if (!src) return;
    const next = {
      width: width > 0 ? width : 1,
      height: height > 0 ? height : 1,
    };
    const currentDimensions = warmedImages[src];
    if (currentDimensions?.width === next.width && currentDimensions.height === next.height) return;
    warmedImages = { ...warmedImages, [src]: next };
  }

  function warmImage(src: string | null | undefined) {
    if (!browser || !src) return;
    if (untrack(() => warmedImages[src]) || pendingImageWarmers.has(src)) return;

    const img = new Image();
    pendingImageWarmers.set(src, img);
    img.decoding = "async";
    img.onload = () => {
      pendingImageWarmers.delete(src);
      rememberWarmedImage(src, img.naturalWidth, img.naturalHeight);
    };
    img.onerror = () => {
      pendingImageWarmers.delete(src);
    };
    img.src = src;
    if (img.complete && img.naturalWidth > 0) {
      img.onload?.(new Event("load"));
    }
  }

  function handleImageLoad(event: Event) {
    const el = event.currentTarget as HTMLImageElement;
    naturalW = el.naturalWidth || naturalW || 1;
    naturalH = el.naturalHeight || naturalH || 1;
    rememberWarmedImage(el.getAttribute("src") ?? currentImageSource?.src, naturalW, naturalH);
    applyFit();
  }

  function handleVideoCanPlay() {
    const video = stageEl?.querySelector("video");
    videoIntrinsicW = video?.videoWidth || videoIntrinsicW;
    videoIntrinsicH = video?.videoHeight || videoIntrinsicH;
    videoReady = true;
  }

  function zoomBy(delta: number, centerX?: number, centerY?: number) {
    const old = scale;
    let next = old * (1 + delta);
    next = Math.min(MAX_SCALE, Math.max(MIN_SCALE * fitScale, next));
    if (next === old) return;
    if (centerX != null && centerY != null && stageEl) {
      const rect = stageEl.getBoundingClientRect();
      const cx = centerX - rect.left - rect.width / 2;
      const cy = centerY - rect.top - rect.height / 2;
      translateX = cx - ((cx - translateX) * next) / old;
      translateY = cy - ((cy - translateY) * next) / old;
    }
    scale = next;
    clampTranslate();
  }

  function handleWheel(event: WheelEvent) {
    if (isCurrentVideo) return;
    event.preventDefault();
    zoomBy(event.deltaY < 0 ? 0.12 : -0.12, event.clientX, event.clientY);
  }

  function handlePointerDown(event: PointerEvent) {
    suppressStageClick = false;
    if (isCurrentVideo) {
      // Track touch gestures so a horizontal swipe navigates (or a downward
      // swipe dismisses) even over a video. Mouse and the timeline scrubber
      // (which stops propagation) keep their normal behaviour: a plain tap
      // still toggles play because we never capture the pointer here.
      pointerStart =
        event.pointerType === "mouse" ? null : { x: event.clientX, y: event.clientY, t: Date.now() };
      panning = false;
      return;
    }
    if (event.pointerType === "mouse" && event.button !== 0) return;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    pointerStart = { x: event.clientX, y: event.clientY, t: Date.now() };
    panning = scale > fitScale + 0.02;
  }

  function handlePointerMove(event: PointerEvent) {
    if (!pointerStart || !panning) return;
    translateX += event.movementX;
    translateY += event.movementY;
    clampTranslate();
  }

  function handlePointerUp(event: PointerEvent) {
    if (isCurrentVideo) {
      const start = pointerStart;
      pointerStart = null;
      panning = false;
      if (!start) return;

      const dx = event.clientX - start.x;
      const dy = event.clientY - start.y;
      const elapsed = Date.now() - start.t;
      const absX = Math.abs(dx);
      const absY = Math.abs(dy);
      if (elapsed < 700 && Math.max(absX, absY) > 60) {
        if (absX > absY * 1.3) {
          if (dx < 0) goNext();
          else goPrev();
          // Swallow the trailing click so it doesn't toggle play on the
          // outgoing video.
          suppressStageClick = true;
          return;
        }
        if (absY > absX * 1.3 && dy > 0) {
          onClose();
          suppressStageClick = true;
          return;
        }
      }
      return;
    }

    const start = pointerStart;
    pointerStart = null;
    if (!start) return;

    const dx = event.clientX - start.x;
    const dy = event.clientY - start.y;
    const elapsed = Date.now() - start.t;
    const absX = Math.abs(dx);
    const absY = Math.abs(dy);
    const moved = Math.hypot(dx, dy) > 6;
    const target = event.target as HTMLElement;

    if (!panning && !moved && event.pointerType === "mouse" && !target.closest("[data-lightbox-media]")) {
      onClose();
      return;
    }

    if (panning) {
      panning = false;
      return;
    }

    if (scale <= fitScale + 0.02 && event.pointerType !== "mouse") {
      if (elapsed < 700 && Math.max(absX, absY) > 60) {
        if (absX > absY * 1.3) {
          if (dx < 0) goNext();
          else goPrev();
          return;
        }
        if (absY > absX * 1.3 && dy > 0) {
          onClose();
          return;
        }
      }
    }

    if (!moved && event.pointerType !== "mouse" && !isCurrentVideo) {
      const now = Date.now();
      if (now - lastTapAt < DOUBLE_TAP_MS) {
        lastTapAt = 0;
        if (scale > fitScale + 0.02) resetTransform();
        else scale = fitScale * 2.5;
        return;
      }
      lastTapAt = now;
    }
  }

  function handleStageClickCapture(event: MouseEvent) {
    if (!suppressStageClick) return;
    suppressStageClick = false;
    event.preventDefault();
    event.stopPropagation();
  }

  function handleDoubleClick(event: MouseEvent) {
    if (isCurrentVideo) return;
    if (scale > fitScale + 0.02) resetTransform();
    else zoomBy(1.5, event.clientX, event.clientY);
  }

  function handleRate(value: number) {
    if (!current || !showRatingControls) return;
    const next = currentRating === value ? null : value;
    ratingOverrides = { ...ratingOverrides, [current.id]: next };
    onRatingChange?.(current.id, next);
  }

  function toggleVideoMute() {
    videoPlayerHandle?.toggleMute();
    videoMuted = !videoMuted;
  }

  onMount(() => {
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const onKey = createNavigationKeyHandler({
      close: onClose,
      prev: goPrev,
      next: goNext,
      extraKeys: {
        "+": () => zoomBy(0.25),
        "=": () => zoomBy(0.25),
        "-": () => zoomBy(-0.2),
        "_": () => zoomBy(-0.2),
        "0": () => resetTransform(),
        "i": () => { if (canOpenDetails) infoOpen = !infoOpen; },
        "I": () => { if (canOpenDetails) infoOpen = !infoOpen; },
        ...(showRatingControls
          ? {
              "1": () => handleRate(1),
              "2": () => handleRate(2),
              "3": () => handleRate(3),
              "4": () => handleRate(4),
              "5": () => handleRate(5),
            }
          : {}),
      },
    });

    window.addEventListener("keydown", onKey);
    const observer = new ResizeObserver(() => {
      if (naturalW && naturalH) applyFit();
    });
    if (stageEl) observer.observe(stageEl);

    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
      observer.disconnect();
    };
  });

</script>

<svelte:head>
  {#each preloadSources as source (`${source.rel}:${source.as}:${source.src}`)}
    <link data-lightbox-preload rel={source.rel} as={source.as} href={source.src} />
  {/each}
</svelte:head>

<div
  use:portal
  class="universal-lightbox"
  role="dialog"
  aria-modal="true"
  in:fade={{ duration: dur.normal, easing: ease.enter }}
  out:fade={{ duration: dur.fast, easing: ease.exit }}
>
  <div class="top-bar">
    <Button variant="ghost" size="icon" onclick={onClose} class="lightbox-button" aria-label="Close" title="Close (Esc)">
      <X class="h-5 w-5" />
    </Button>
    <div class="title-block">
      {#if current?.title}
        <h2>{current.title}</h2>
      {/if}
      <div class="counter">{counterText}</div>
    </div>
    {#if showRatingControls}
      <div class="rating-buttons">
        {#each [1, 2, 3, 4, 5] as n (n)}
          <Button variant="ghost" size="icon" onclick={() => handleRate(n)} class="lightbox-button" aria-label={`Rate ${n}`} title={`${n} stars`}>
            <Star class={cn("h-4 w-4", (currentRating ?? 0) >= n && "is-filled")} />
          </Button>
        {/each}
      </div>
    {/if}
    {#if canOpenDetails}
      <Button
        variant="ghost"
        size="icon"
        onclick={() => (infoOpen = !infoOpen)}
        class={cn("lightbox-button", infoOpen && "is-active")}
        aria-label="Details"
        title="Details (I)"
      >
        <Info class="h-4 w-4" />
      </Button>
    {/if}
    {#if hasCurrentVideoPlayback}
      <Button
        variant="ghost"
        size="icon"
        onclick={toggleVideoMute}
        class={cn("lightbox-button", !videoMuted && "is-active")}
        aria-label={videoMuted ? "Unmute" : "Mute"}
        title={videoMuted ? "Unmute" : "Mute"}
      >
        {#if videoMuted}
          <VolumeX class="h-4 w-4" />
        {:else}
          <Volume2 class="h-4 w-4" />
        {/if}
      </Button>
    {/if}
    {#if downloadHref}
      <a href={downloadHref} download={current?.title ?? "media"} class={cn(buttonVariants({ variant: "ghost", size: "icon" }), "lightbox-button")} aria-label="Download" title="Download">
        <Download class="h-4 w-4" />
      </a>
    {/if}
  </div>

  {#if infoOpen && detailsContent && current}
    <div class="details-back-page">
      {@render detailsContent(current)}
    </div>
  {:else}
    <div class="stage-row">
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div
        bind:this={stageEl}
        class="stage"
        onwheel={handleWheel}
        onpointerdown={handlePointerDown}
        onpointermove={handlePointerMove}
        onpointerup={handlePointerUp}
        onpointercancel={handlePointerUp}
        onclickcapture={handleStageClickCapture}
        ondblclick={handleDoubleClick}
        style:cursor={!isCurrentVideo && scale > fitScale + 0.02 ? (panning ? "grabbing" : "grab") : "default"}
      >
        {#if current}
          <div
            class="media-frame"
            data-lightbox-media
            style:transform={isCurrentVideo ? undefined : `translate(${translateX}px, ${translateY}px) scale(${scale})`}
            style:opacity={ready ? 1 : 0}
          >
            <NsfwBlur isNsfw={current.isNsfw === true} class="lightbox-media-guard">
              {#if isCurrentVideo && primaryVideoSource}
                <div
                  class="lightbox-video-shell"
                  class:has-natural-ratio={Boolean(currentVideoFit)}
                  class:is-ready={videoReady}
                  style={currentVideoFit
                    ? `--lightbox-video-aspect-ratio: ${currentVideoFit.aspectRatio}; --lightbox-video-width-ratio: ${currentVideoFit.widthToHeightRatio};`
                    : undefined}
                >
                  {#if fallbackPoster}
                    <img class="lightbox-video-poster" src={fallbackPoster} alt="" aria-hidden="true" />
                  {/if}
                  <VideoPlayer
                    bind:handle={videoPlayerHandle}
                    directSrc={primaryVideoSource.src}
                    codec={primaryVideoCodec}
                    sourceWidth={positiveNumberValue(currentTechnical?.width)}
                    sourceHeight={positiveNumberValue(currentTechnical?.height)}
                    poster={fallbackPoster}
                    defaultPlaybackMode="direct"
                    showCastControls={false}
                    chrome="minimal"
                    enableKeyboardShortcuts={false}
                    initialMuted={videoMuted}
                    onCanPlay={handleVideoCanPlay}
                    autoPlay
                    autoRepeat
                  />
                </div>
              {:else if isCurrentVideo && fallbackPoster}
                <img
                  bind:this={imageEl}
                  src={fallbackPoster}
                  alt={current.title}
                  class="lightbox-image"
                  referrerpolicy="no-referrer"
                  style:width="{naturalW || "auto"}px"
                  style:height="{naturalH || "auto"}px"
                  onload={handleImageLoad}
                  draggable="false"
                />
              {:else if currentImageSource}
                <img
                  bind:this={imageEl}
                  src={currentImageSource.src}
                  alt={current.title}
                  class="lightbox-image"
                  referrerpolicy="no-referrer"
                  style:width="{naturalW || "auto"}px"
                  style:height="{naturalH || "auto"}px"
                  onload={handleImageLoad}
                  draggable="false"
                />
              {:else}
                <div class="unsupported">No displayable media source</div>
              {/if}
            </NsfwBlur>
          </div>
        {/if}

        <div class="keyboard-hints">
          {keyboardHintText}
        </div>
      </div>

    </div>
  {/if}

  <div class="bottom-bar">
    <div class="bottom-controls">
      <Button variant="ghost" size="icon" onclick={goPrev} class="lightbox-button" aria-label="Previous">
        <ChevronLeft class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={goNext} class="lightbox-button" aria-label="Next">
        <ChevronRight class="h-4 w-4" />
      </Button>
    </div>
    <div class="counter">{counterText}</div>
    <div class="bottom-controls">
      <Button variant="ghost" size="icon" onclick={() => zoomBy(-0.2)} class="lightbox-button" aria-label="Zoom out">
        <ZoomOut class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={() => zoomBy(0.25)} class="lightbox-button" aria-label="Zoom in">
        <ZoomIn class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={resetTransform} class="lightbox-button" aria-label="Reset zoom">
        <RotateCcw class="h-4 w-4" />
      </Button>
    </div>
  </div>

  {#if entities.length > 1}
    <div class="thumb-strip">
      {#each entities as item, i (item.id)}
        <button
          type="button"
          onclick={() => (index = i)}
          bind:this={stripThumbEls[i]}
          class={cn("thumb-button", i === index && "is-active")}
          aria-label={`Image ${i + 1}`}
        >
          {#if item.coverUrl}
            <img src={item.coverUrl} alt="" loading="lazy" />
          {:else}
            <span>{i + 1}</span>
          {/if}
        </button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .universal-lightbox {
    position: fixed;
    inset: 0;
    z-index: 2000;
    display: flex;
    flex-direction: column;
    width: 100dvw;
    height: 100dvh;
    max-width: 100dvw;
    max-height: 100dvh;
    overflow: hidden;
    isolation: isolate;
    background: rgb(0 0 0 / 0.95);
    backdrop-filter: blur(var(--glass-blur-sm));
    color: var(--color-text-primary, #f2eed8);
  }

  .top-bar,
  .bottom-bar,
  .thumb-strip {
    position: relative;
    z-index: 20;
    border-color: var(--color-border-subtle, #1c2235);
    background: rgb(0 0 0 / 0.72);
    backdrop-filter: blur(var(--glass-blur-md));
  }

  .top-bar,
  .bottom-bar {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.5rem 0.75rem;
  }

  .top-bar {
    border-bottom: 1px solid var(--color-border-subtle, #1c2235);
  }

  .bottom-bar {
    justify-content: space-between;
    border-top: 1px solid var(--color-border-subtle, #1c2235);
  }

  .title-block {
    min-width: 0;
    flex: 1;
  }

  .title-block h2 {
    margin: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.9rem;
    font-weight: 600;
  }

  .counter {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--color-text-muted, #8a93a6);
  }

  .rating-buttons,
  .bottom-controls {
    display: flex;
    align-items: center;
    gap: 0.15rem;
  }

  .lightbox-button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: 1px solid transparent;
    color: var(--color-text-muted, #8a93a6);
    cursor: pointer;
    text-decoration: none;
    transition: border-color 150ms ease, color 150ms ease, box-shadow 150ms ease;
  }

  .lightbox-button:hover,
  .lightbox-button.is-active {
    border-color: rgb(196 154 90 / 0.45);
    color: var(--color-text-accent, #c49a5a);
    box-shadow: 0 0 18px rgb(196 154 90 / 0.22);
  }

  :global(.is-filled) {
    fill: currentColor;
    color: var(--color-text-accent, #c49a5a);
  }

  .stage-row {
    display: flex;
    overflow: hidden;
    min-height: 0;
    flex: 1;
  }

  .details-back-page {
    min-height: 0;
    flex: 1;
    overflow-y: auto;
    background:
      radial-gradient(circle at top left, rgb(196 154 90 / 0.1), transparent 34rem),
      var(--color-bg, #050508);
  }

  .details-back-page :global(.entity-detail) {
    min-height: 100%;
  }

  .stage {
    position: relative;
    min-width: 0;
    flex: 1;
    overflow: hidden;
    user-select: none;
    /* Deliver all touch gestures to our pointer handlers; without this the
       browser claims horizontal swipes for back-navigation and fires
       pointercancel mid-swipe, so swipe-to-navigate/dismiss never registers. */
    touch-action: none;
  }

  .media-frame {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    transform-origin: center center;
  }

  .media-frame :global(.lightbox-media-guard) {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
  }

  .media-frame :global(.prismedia-player-surface) {
    border: 0;
    background: transparent;
  }

  .media-frame :global([data-testid="vidstack-video-player"]) {
    aspect-ratio: inherit;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: auto;
    max-width: 100%;
    max-height: 100%;
  }

  .media-frame :global([data-testid="vidstack-video-player"] .prismedia-player-surface) {
    width: 100%;
    height: auto;
    max-height: 100%;
    aspect-ratio: inherit;
  }

  .media-frame :global(.prismedia-media-engine) {
    width: 100%;
    height: auto;
    max-width: 100%;
    max-height: 100%;
    aspect-ratio: inherit;
  }

  .lightbox-video-shell {
    --lightbox-video-aspect-ratio: 16 / 9;
    --lightbox-video-max-height: calc((100dvh - 10rem) * 0.98);
    --lightbox-video-width-ratio: 1.7777777778;
    position: relative;
    display: flex;
    width: min(98dvw, 100%, calc(var(--lightbox-video-max-height) * var(--lightbox-video-width-ratio)));
    max-width: min(98dvw, 100%);
    height: auto;
    max-height: min(98%, var(--lightbox-video-max-height));
    aspect-ratio: var(--lightbox-video-aspect-ratio);
    align-items: center;
    justify-content: center;
    background: #000;
  }

  .lightbox-video-shell.has-natural-ratio {
    width: min(98dvw, 100%, calc(var(--lightbox-video-max-height) * var(--lightbox-video-width-ratio)));
  }

  .lightbox-video-poster {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: contain;
    opacity: 1;
    transition: opacity 160ms ease;
  }

  .lightbox-video-shell :global([data-testid="vidstack-video-player"]) {
    width: 100%;
    max-width: 100%;
    height: auto;
    opacity: 0;
    transition: opacity 160ms ease;
  }

  .lightbox-video-shell.is-ready :global([data-testid="vidstack-video-player"]) {
    opacity: 1;
  }

  .lightbox-video-shell.is-ready .lightbox-video-poster {
    opacity: 0;
  }

  .lightbox-image {
    max-width: none;
    pointer-events: none;
  }

  .unsupported {
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    padding: 1rem 1.25rem;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .keyboard-hints {
    position: absolute;
    bottom: 0.75rem;
    left: 0.75rem;
    z-index: 10;
    pointer-events: none;
    color: rgb(255 255 255 / 0.25);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.56rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
  }

  .thumb-strip {
    display: flex;
    gap: 0.35rem;
    overflow-x: auto;
    border-top: 1px solid var(--color-border-subtle, #1c2235);
    /* Keep the strip clear of the device's home indicator / gesture area so the
       thumbnails stay tappable instead of sitting flush against the edge. */
    padding: 0.4rem 0.5rem calc(0.4rem + env(safe-area-inset-bottom, 0px));
  }

  .thumb-button {
    width: 3rem;
    aspect-ratio: 1;
    flex: 0 0 auto;
    overflow: hidden;
    border: 1px solid transparent;
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    opacity: 0.6;
    transition: border-color 150ms ease, opacity 150ms ease, box-shadow 150ms ease;
  }

  .thumb-button:hover,
  .thumb-button.is-active {
    border-color: rgb(196 154 90 / 0.45);
    opacity: 1;
    box-shadow: 0 0 16px rgb(196 154 90 / 0.2);
  }

  .thumb-button img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  @media (max-width: 640px) {
    .rating-buttons {
      display: none;
    }

    .keyboard-hints {
      display: none;
    }

    /* Larger, more separated targets and extra bottom clearance make the strip
       comfortable to use on touch devices. */
    .thumb-strip {
      gap: 0.45rem;
      padding: 0.5rem 0.5rem calc(0.55rem + env(safe-area-inset-bottom, 0px));
    }

    .thumb-button {
      width: 3.25rem;
    }

    /* When a single item (e.g. a video) is open there is no strip, so the
       bottom control bar is the lowest element — keep it off the edge too. */
    .bottom-bar {
      padding-bottom: calc(0.5rem + env(safe-area-inset-bottom, 0px));
    }
  }
</style>
