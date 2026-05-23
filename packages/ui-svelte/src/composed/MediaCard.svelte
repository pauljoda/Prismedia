<script module lang="ts">
  import type { Snippet } from "svelte";

  export type PerformerRef = string | { name: string; imagePath?: string | null };

  export interface MediaCardProps {
    title: string;
    thumbnail?: string;
    cardThumbnail?: string;
    imageLoading?: "eager" | "lazy";
    trickplaySprite?: string;
    trickplayVtt?: string;
    scrubDurationSeconds?: number;
    gradientClass?: string;
    duration?: string;
    resolution?: string;
    codec?: string;
    hasSubtitles?: boolean;
    fileSize?: string;
    studio?: string;
    performers?: PerformerRef[];
    tags?: string[];
    tagsSlot?: Snippet;
    tagColors?: Record<string, string>;
    rating?: number;
    views?: number;
    href?: string;
    class?: string;
    thumbnailOverlay?: Snippet;
    topLeftBadge?: Snippet;
  }
</script>

<script lang="ts">
  import { Captions, Film, Clock, HardDrive, Eye, Star } from "@lucide/svelte";
  import { cn } from "../lib/utils";
  import { loadTrickplayFrames, type TrickplayFrame } from "../lib/trickplay";

  function formatHoverTime(seconds: number) {
    const wholeSeconds = Math.max(0, Math.floor(seconds));
    const hours = Math.floor(wholeSeconds / 3600);
    const minutes = Math.floor((wholeSeconds % 3600) / 60);
    const remainder = wholeSeconds % 60;
    if (hours > 0) {
      return `${hours}:${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`;
    }
    return `${minutes}:${String(remainder).padStart(2, "0")}`;
  }

  let {
    title,
    thumbnail,
    cardThumbnail,
    imageLoading = "lazy",
    trickplaySprite,
    trickplayVtt,
    scrubDurationSeconds,
    gradientClass,
    duration,
    resolution,
    codec,
    hasSubtitles,
    fileSize,
    studio,
    performers,
    tags,
    tagsSlot,
    tagColors,
    rating,
    views,
    class: className,
    thumbnailOverlay,
    topLeftBadge,
  }: MediaCardProps = $props();

  let cardEl: HTMLElement | undefined = $state();
  let thumbEl: HTMLDivElement | undefined = $state();
  let frames = $state<TrickplayFrame[] | null>(null);
  let trickplayError = $state(false);
  let activeFrameIndex = $state<number | null>(null);
  let lastFrameIndex: number | null = null;
  let touchScrubbing = false;
  let touchStartPos: { x: number; y: number } | null = null;
  let touchLocked: "scrub" | "scroll" | null = null;

  const hasScrubPreview = $derived(
    Boolean(
      trickplaySprite &&
        trickplayVtt &&
        scrubDurationSeconds &&
        scrubDurationSeconds > 0,
    ) && !trickplayError,
  );

  const activeFrame = $derived(
    activeFrameIndex != null && frames && activeFrameIndex < frames.length
      ? frames[activeFrameIndex]
      : null,
  );

  const spriteDims = $derived.by(() => {
    if (!frames) return { spriteWidth: 0, spriteHeight: 0 };
    return {
      spriteWidth: frames.reduce((max, f) => Math.max(max, f.x + f.width), 0),
      spriteHeight: frames.reduce((max, f) => Math.max(max, f.y + f.height), 0),
    };
  });

  async function ensureTrickplayLoaded() {
    if (!trickplayVtt || frames || trickplayError) return;
    try {
      if (trickplaySprite) {
        const img = new Image();
        img.src = trickplaySprite;
      }
      const nextFrames = await loadTrickplayFrames(trickplayVtt);
      frames = nextFrames;
    } catch {
      trickplayError = true;
    }
  }

  function updateActiveFrame(normalizedPosition: number) {
    if (!frames || !scrubDurationSeconds) return;
    const clamped = Math.max(0, Math.min(1, normalizedPosition));
    const targetTime = clamped * scrubDurationSeconds;
    let nextIdx = frames.findIndex(
      (frame) => targetTime >= frame.start && targetTime < frame.end,
    );
    if (nextIdx === -1) {
      nextIdx = Math.min(frames.length - 1, Math.floor(clamped * frames.length));
    }
    if (lastFrameIndex === nextIdx) return;
    lastFrameIndex = nextIdx;
    activeFrameIndex = nextIdx;
  }

  function handlePointerMove(event: PointerEvent) {
    if (!hasScrubPreview || !cardEl) return;
    const bounds = cardEl.getBoundingClientRect();
    if (bounds.width === 0) return;
    updateActiveFrame((event.clientX - bounds.left) / bounds.width);
  }

  function handlePointerLeave() {
    lastFrameIndex = null;
    activeFrameIndex = null;
  }

  const LOCK_THRESHOLD = 8;

  function handleTouchStart(event: TouchEvent) {
    if (!hasScrubPreview) return;
    const t = event.touches[0];
    touchStartPos = { x: t.clientX, y: t.clientY };
    touchLocked = null;
    touchScrubbing = false;
    void ensureTrickplayLoaded();
  }

  function handleTouchMove(event: TouchEvent) {
    if (!hasScrubPreview || !touchStartPos || !thumbEl) return;
    const t = event.touches[0];
    const dx = Math.abs(t.clientX - touchStartPos.x);
    const dy = Math.abs(t.clientY - touchStartPos.y);
    if (!touchLocked && (dx > LOCK_THRESHOLD || dy > LOCK_THRESHOLD)) {
      touchLocked = dx >= dy ? "scrub" : "scroll";
    }
    if (touchLocked === "scroll") return;
    event.preventDefault();
    touchScrubbing = true;
    const bounds = thumbEl.getBoundingClientRect();
    if (bounds.width === 0) return;
    updateActiveFrame((t.clientX - bounds.left) / bounds.width);
  }

  function handleTouchEnd() {
    touchStartPos = null;
    touchLocked = null;
    if (touchScrubbing) {
      touchScrubbing = false;
      lastFrameIndex = null;
      activeFrameIndex = null;
    }
  }
</script>

<article
  bind:this={cardEl}
  class={cn(
    "surface-card-sharp media-card-shell group cursor-pointer overflow-hidden",
    className,
  )}
  onpointerenter={() => void ensureTrickplayLoaded()}
  onpointermove={handlePointerMove}
  onpointerleave={handlePointerLeave}
>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    bind:this={thumbEl}
    class="relative aspect-video overflow-hidden bg-surface-1"
    ontouchstart={handleTouchStart}
    ontouchmove={handleTouchMove}
    ontouchend={handleTouchEnd}
  >
    {#if thumbnail}
      <img
        src={cardThumbnail || thumbnail}
        alt={title}
        loading={imageLoading}
        decoding="async"
        class={cn(
          "h-full w-full object-cover transition-transform duration-normal",
          activeFrame ? "scale-[1.01] opacity-0" : "group-hover:scale-[1.03]",
        )}
      />
    {:else}
      <div
        class={cn(
          "flex h-full w-full items-center justify-center",
          gradientClass || "bg-surface-1",
          activeFrame && "opacity-0",
        )}
      >
        <Film class="h-7 w-7 text-white/10" />
      </div>
    {/if}

    {#if activeFrame && trickplaySprite && spriteDims.spriteWidth > 0 && spriteDims.spriteHeight > 0}
      <div class="absolute inset-0 overflow-hidden">
        <div
          aria-hidden="true"
          class="absolute inset-0"
          style:background-image="url({trickplaySprite})"
          style:background-size="{(spriteDims.spriteWidth / activeFrame.width) * 100}% {(spriteDims.spriteHeight / activeFrame.height) * 100}%"
          style:background-position="{spriteDims.spriteWidth <= activeFrame.width
            ? 0
            : (activeFrame.x / (spriteDims.spriteWidth - activeFrame.width)) * 100}% {spriteDims.spriteHeight <= activeFrame.height
            ? 0
            : (activeFrame.y / (spriteDims.spriteHeight - activeFrame.height)) * 100}%"
          style:background-repeat="no-repeat"
        ></div>
        <div
          class="absolute inset-x-0 top-0 h-14 bg-gradient-to-b from-black/70 via-black/30 to-transparent pointer-events-none"
        ></div>
        <div
          class="absolute left-2 top-2 media-chip-accent px-2 py-1 text-[0.65rem] font-mono tracking-[0.12em] text-accent-100"
        >
          SCRUB {formatHoverTime(activeFrame.start)}
        </div>
      </div>
    {/if}

    <div
      class="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/70 to-transparent pointer-events-none"
    ></div>

    {#if duration}
      <span
        class="absolute bottom-1.5 left-1.5 flex items-center gap-1 media-chip px-1.5 py-0.5 text-[0.65rem] font-mono text-white/90"
      >
        <Clock class="h-2.5 w-2.5 text-white/60" />
        {duration}
      </span>
    {/if}

    <div class="absolute top-1.5 right-1.5 flex items-center gap-1">
      {#if hasSubtitles}
        <span
          class="media-chip flex items-center gap-0.5 px-1.5 py-0.5 text-[0.58rem] font-mono text-accent-100 border-accent-500/40"
          title="Closed captions available"
        >
          <Captions class="h-2.5 w-2.5" />
          CC
        </span>
      {/if}
      {#if resolution}
        <span class="pill-accent px-1.5 py-0.5 text-[0.58rem] font-semibold tracking-wide">
          {resolution}
        </span>
      {/if}
      {#if codec}
        <span class="media-chip px-1.5 py-0.5 text-[0.58rem] font-mono text-white/70">
          {codec}
        </span>
      {/if}
    </div>

    {#if hasScrubPreview}
      <div class="pointer-events-none absolute inset-x-0 bottom-0 flex items-center">
        <div class="h-1 flex-1 overflow-hidden bg-black/55">
          <div
            class="h-full bg-gradient-to-r from-accent-700 via-accent-500 to-accent-300 shadow-[0_0_6px_rgba(199,155,92,0.3)] transition-[width] duration-75"
            style:width={activeFrame && scrubDurationSeconds
              ? `${Math.min(100, (activeFrame.start / scrubDurationSeconds) * 100)}%`
              : "0%"}
          ></div>
        </div>
      </div>
    {/if}

    {#if thumbnailOverlay}
      <div class="pointer-events-none absolute right-2 bottom-2 z-[25]">
        {@render thumbnailOverlay()}
      </div>
    {/if}

    {#if topLeftBadge}
      <div class="pointer-events-none absolute left-1.5 top-1.5 z-[25]">
        {@render topLeftBadge()}
      </div>
    {/if}
  </div>

  <div class="p-2.5 space-y-1.5">
    <h4 class="truncate text-[0.8rem] font-medium text-text-primary leading-tight">
      {title}
    </h4>

    {#if studio || (performers && performers.length > 0)}
      <div class="flex items-center gap-1.5 text-text-muted min-w-0">
        {#if studio}
          <span class="text-[0.7rem] text-text-accent truncate flex-shrink-0">
            {studio}
          </span>
        {/if}
        {#if studio && performers?.length}
          <span class="text-text-disabled text-[0.6rem]">/</span>
        {/if}
        {#if performers && performers.length > 0}
          <span class="inline-flex items-center gap-1 text-[0.7rem] truncate">
            {#each performers.slice(0, 2) as p, i}
              {@const name = typeof p === "string" ? p : p.name}
              {@const imgPath = typeof p === "string" ? null : p.imagePath}
              <span class="inline-flex items-center gap-1">
                {#if i > 0}<span class="text-text-disabled">,</span>{/if}
                {#if imgPath}
                  <img
                    src={imgPath}
                    alt=""
                    loading="lazy"
                    decoding="async"
                    class="h-4 w-3 object-cover flex-shrink-0"
                  />
                {/if}
                <span>{name}</span>
              </span>
            {/each}
            {#if performers.length > 2}
              <span class="text-text-disabled"> +{performers.length - 2}</span>
            {/if}
          </span>
        {/if}
      </div>
    {/if}

    {#if tagsSlot}
      {@render tagsSlot()}
    {:else if tags && tags.length > 0}
      <div class="flex flex-wrap gap-1">
        {#each tags.slice(0, 3) as tag}
          {@const colorClass = tagColors?.[tag] || "tag-chip-default"}
          <span class={cn("tag-chip", colorClass)}>{tag}</span>
        {/each}
        {#if tags.length > 3}
          <span class="tag-chip tag-chip-default text-text-disabled">
            +{tags.length - 3}
          </span>
        {/if}
      </div>
    {/if}

    {#if fileSize || views !== undefined || (rating != null && rating > 0)}
      <div class="flex items-center gap-3 pt-1 border-t border-border-subtle">
        {#if rating != null && rating > 0}
          <span class="flex items-center gap-0.5 text-[0.62rem] text-glow-accent">
            <Star class="h-2.5 w-2.5 fill-current" />
            {Math.round(rating / 20)}
          </span>
        {/if}
        {#if fileSize}
          <span class="flex items-center gap-1 text-ephemeral">
            <HardDrive class="h-2.5 w-2.5" />
            {fileSize}
          </span>
        {/if}
        {#if views !== undefined}
          <span class="flex items-center gap-1 text-ephemeral">
            <Eye class="h-2.5 w-2.5" />
            {views}
          </span>
        {/if}
      </div>
    {/if}
  </div>
</article>
