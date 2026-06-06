<script lang="ts">
  import { onDestroy } from "svelte";
  import { browser } from "$app/environment";
  import NsfwBlur from "../nsfw/NsfwBlur.svelte";
  import { getCapability, getImagesCapability, isNsfw as hasNsfwFlag } from "$lib/api/capabilities";
  import { entityFileUrl } from "$lib/api/files";
  import { fetchImage, fetchVideo } from "$lib/api/media";
  import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import {
    type EntityThumbnailCard,
    placeholderGradient,
    resolveEntityThumbnailHref,
  } from "$lib/entities/entity-thumbnail";
  import {
    buildLightboxVideoSources,
    isAnimatedStillImage,
    isLightboxVideoCapable,
    lightboxEntityFromCard,
    type UniversalLightboxEntity,
  } from "../universal-lightbox-media";

  interface Props {
    cards: EntityThumbnailCard[];
    /**
     * Invoked when a feed item is tapped. When provided (image route) the feed is
     * purely visual and a tap opens the shared lightbox. When omitted (galleries)
     * each item links to its entity page instead.
     */
    onActivate?: (card: EntityThumbnailCard, cards: EntityThumbnailCard[]) => void;
    /**
     * Media-wall variant: drop the caption block and edge-to-edge framing so each
     * item is just its media, mirroring the grid's media-wall mode.
     */
    mediaWall?: boolean;
  }

  let { cards, onActivate, mediaWall = false }: Props = $props();

  // How many items on either side of the centered item keep a live, autoplaying
  // player mounted. The centered item plus these neighbours stay "always playing"
  // as the feed scrolls; everything else falls back to a static poster image.
  const WINDOW = 2;

  let activeIndex = $state(0);
  let hydrated = $state.raw<Record<string, UniversalLightboxEntity>>({});
  const inFlight = new Set<string>();
  const ratios = new Map<number, number>();

  const observer =
    browser && typeof IntersectionObserver === "function"
      ? new IntersectionObserver(handleIntersections, { threshold: [0, 0.25, 0.5, 0.75, 1] })
      : undefined;

  onDestroy(() => observer?.disconnect());

  function handleIntersections(entries: IntersectionObserverEntry[]) {
    for (const entry of entries) {
      const index = Number((entry.target as HTMLElement).dataset.feedIndex);
      if (Number.isNaN(index)) continue;
      if (entry.isIntersecting) ratios.set(index, entry.intersectionRatio);
      else ratios.delete(index);
    }

    let best = -1;
    let bestRatio = -1;
    for (const [index, ratio] of ratios) {
      if (ratio > bestRatio) {
        bestRatio = ratio;
        best = index;
      }
    }
    if (best >= 0) activeIndex = best;
  }

  // Svelte action: register each rendered item with the shared observer and keep
  // its index in sync as the list re-renders across pages.
  function trackVisibility(node: HTMLElement, index: number) {
    node.dataset.feedIndex = String(index);
    observer?.observe(node);
    return {
      update(next: number) {
        node.dataset.feedIndex = String(next);
      },
      destroy() {
        observer?.unobserve(node);
      },
    };
  }

  function isWithinWindow(index: number): boolean {
    return Math.abs(index - activeIndex) <= WINDOW;
  }

  // Mirror the lightbox: only the full entity detail carries the files/technical
  // capabilities needed to tell an animated image from a still and to build a
  // playable source, so hydrate window items on demand and cache the result.
  async function hydrate(card: EntityThumbnailCard) {
    const id = card.entity.id;
    if (card.entity.kind !== ENTITY_KIND.image && card.entity.kind !== ENTITY_KIND.video) return;
    if (hydrated[id] || inFlight.has(id)) return;
    inFlight.add(id);
    try {
      const entity = card.entity.kind === ENTITY_KIND.video
        ? await fetchVideo(id)
        : await fetchImage(id);
      hydrated = {
        ...hydrated,
        [id]: {
          id: entity.id,
          kind: entity.kind,
          title: entity.title,
          capabilities: entity.capabilities,
          coverUrl: getImagesCapability(entity.capabilities)?.coverUrl ?? card.cover?.src ?? null,
          isNsfw: hasNsfwFlag(entity.capabilities),
        },
      };
    } catch {
      // Leave the card as a static cover when detail loading fails.
    } finally {
      inFlight.delete(id);
    }
  }

  $effect(() => {
    for (let index = activeIndex - WINDOW; index <= activeIndex + WINDOW; index++) {
      const card = cards[index];
      if (card) void hydrate(card);
    }
  });

  function lightboxEntity(card: EntityThumbnailCard): UniversalLightboxEntity {
    return hydrated[card.entity.id] ?? lightboxEntityFromCard(card);
  }

  function isVideoCard(card: EntityThumbnailCard): boolean {
    const entity = hydrated[card.entity.id];
    return entity ? isLightboxVideoCapable(entity) : false;
  }

  function videoSourceFor(card: EntityThumbnailCard, allowOriginalFallback = false): string | null {
    const entity = hydrated[card.entity.id];
    if (!entity) return null;

    if (entity.kind === ENTITY_KIND.video) {
      return buildLightboxVideoSources(entity, { preferOriginal: true })[0]?.src ?? null;
    }

    if (allowOriginalFallback) {
      return buildLightboxVideoSources(entity, { preferOriginal: true })[0]?.src ?? null;
    }

    return buildLightboxVideoSources(entity)[0]?.src ?? null;
  }

  // Source URL for an in-window animated image, or null when the item isn't an
  // animated still (videos are handled separately as a <video> overlay).
  function animatedImageSourceFor(card: EntityThumbnailCard): string | null {
    const entity = hydrated[card.entity.id];
    if (!entity || entity.kind !== ENTITY_KIND.image) return null;
    if (!isAnimatedStillImage(entity)) return null;
    const source = getCapability(entity.capabilities, CAPABILITY_KIND.files)?.items.find(
      (file) => file.role === ENTITY_FILE_ROLE.source,
    );
    return source ? entityFileUrl(card.entity.id, ENTITY_FILE_ROLE.source) : null;
  }

  function numberOf(value: number | string | null | undefined): number | null {
    if (typeof value === "number") return Number.isFinite(value) && value > 0 ? value : null;
    if (typeof value === "string" && value.trim() !== "") {
      const parsed = Number(value);
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    }
    return null;
  }

  function technicalOf(card: EntityThumbnailCard) {
    const entity = hydrated[card.entity.id];
    return entity ? getCapability(entity.capabilities, CAPABILITY_KIND.technical) : null;
  }

  // Cache of each item's known aspect ratio so its box reserves the right height
  // up front — the standard way to avoid layout shift (CLS) as media loads. Once
  // an item's size is known it never changes, so scrolling away and back, paging,
  // and swapping poster<->player are all reflow-free.
  let aspectById = $state(new Map<string, string>());

  function reservedDimensions(card: EntityThumbnailCard): { width: number; height: number } | undefined {
    const cached = aspectById.get(card.entity.id);
    if (cached) {
      const [w, h] = cached.split(" / ").map(Number);
      if (w > 0 && h > 0) return { width: w, height: h };
    }

    // Authoritative dimensions from the hydrated detail, when available.
    const technical = technicalOf(card);
    const width = numberOf(technical?.width);
    const height = numberOf(technical?.height);
    if (width && height) return { width, height };

    // Dimensions the card already carried (full entity cards). The per-kind
    // fallback ("square" etc.) is intentionally ignored so browse images are not
    // forced into a placeholder shape before their real size is measured.
    const ratio = card.aspectRatio;
    if (typeof ratio === "object" && ratio.width > 0 && ratio.height > 0) {
      return { width: ratio.width, height: ratio.height };
    }

    return undefined;
  }

  function reservedAspect(card: EntityThumbnailCard): string | undefined {
    const dims = reservedDimensions(card);
    return dims ? `${dims.width} / ${dims.height}` : undefined;
  }

  // Numeric width/height ratio used to cap a portrait item's width so its
  // aspect box, at the capped height, fits without pillarboxing.
  function reservedRatio(card: EntityThumbnailCard): number | undefined {
    const dims = reservedDimensions(card);
    return dims ? dims.width / dims.height : undefined;
  }

  // Record the poster's intrinsic size the first time it loads so the slot is
  // reserved for the rest of the session.
  function measureAspect(id: string, img: HTMLImageElement) {
    if (aspectById.has(id)) return;
    const { naturalWidth, naturalHeight } = img;
    if (naturalWidth > 0 && naturalHeight > 0) {
      const next = new Map(aspectById);
      next.set(id, `${naturalWidth} / ${naturalHeight}`);
      aspectById = next;
    }
  }

  // Cap each item's media to what fits in the scroll viewport beneath the sticky
  // toolbar, so even a tall portrait clip stays fully visible without scrolling.
  // Measured from the scroll container (which sits below the toolbar and does not
  // move as the feed scrolls); falls back to a safe viewport fraction via CSS.
  let feedEl = $state<HTMLElement | null>(null);
  let maxMediaHeight = $state<number | null>(null);

  function measureAvailableHeight() {
    if (!browser || !feedEl) return;
    const scroller = feedEl.closest(".grid-viewport") ?? feedEl.parentElement;
    const top = scroller ? scroller.getBoundingClientRect().top : 0;
    const available = window.innerHeight - top - 24;
    maxMediaHeight = available > 240 ? Math.round(available) : null;
  }

  $effect(() => {
    if (!browser) return;
    // Re-measure when the card set changes (the feed may mount after the viewport).
    void cards.length;
    measureAvailableHeight();
    window.addEventListener("resize", measureAvailableHeight);
    return () => window.removeEventListener("resize", measureAvailableHeight);
  });

  // Playback fraction (0..1) per inline video, driving the thin bottom progress
  // bar that mirrors the lightbox player's minimal timeline.
  let progressById = $state(new Map<string, number>());
  let readyVideoSourceById = $state(new Map<string, string>());

  function trackVideoProgress(id: string, video: HTMLVideoElement) {
    const duration = video.duration;
    const fraction =
      Number.isFinite(duration) && duration > 0 ? Math.min(1, video.currentTime / duration) : 0;
    const next = new Map(progressById);
    next.set(id, fraction);
    progressById = next;
  }

  function videoReadyFor(id: string, source: string | null): boolean {
    return Boolean(source && readyVideoSourceById.get(id) === source);
  }

  function markVideoReady(id: string, source: string | null) {
    if (!source || videoReadyFor(id, source)) return;
    const next = new Map(readyVideoSourceById);
    next.set(id, source);
    readyVideoSourceById = next;
  }
</script>

<div
  class="feed"
  class:is-media-wall={mediaWall}
  bind:this={feedEl}
  style:--feed-max-h={maxMediaHeight ? `${maxMediaHeight}px` : undefined}
>
  {#each cards as card, index (card.entity.id)}
    {@const href = onActivate ? undefined : resolveEntityThumbnailHref(card)}
    {@const cover = card.cover}
    {@const showVideo = isWithinWindow(index) && isVideoCard(card)}
    {@const source = showVideo ? videoSourceFor(card, index === activeIndex) : null}
    {@const animatedSrc = !showVideo && isWithinWindow(index) ? animatedImageSourceFor(card) : null}
    {@const aspect = reservedAspect(card)}
    <article class="feed-item" use:trackVisibility={index} style:--ratio={reservedRatio(card)}>
      <div class="feed-media" class:has-aspect={Boolean(aspect)} style:aspect-ratio={aspect}>
        <NsfwBlur isNsfw={lightboxEntity(card).isNsfw === true}>
          <!--
            The poster image always stays mounted and owns the item's height, so
            mounting or unmounting the inline player (as items scroll in and out
            of the play window) never reflows the feed. The player is an absolute
            overlay that fills the same box.
          -->
          {#if cover}
            <img
              class="feed-poster"
              src={cover.src}
              alt={card.entity.title}
              loading="lazy"
              onload={(event) => measureAspect(card.entity.id, event.currentTarget as HTMLImageElement)}
            />
          {:else}
            <div class="feed-placeholder" style:background={placeholderGradient(card.entity.title)}></div>
          {/if}

          {#if showVideo && source}
            <!--
              Inline preview uses a plain muted, looping autoplay video. Muted
              autoplay is the only playback browsers allow without a user gesture,
              so it reliably plays inline — unlike the full player, which on an
              autoplay block tries to fall back to an HLS stream these single-file
              entities don't have and then silently stays on the poster. A tap
              still opens the lightbox for full playback with controls and sound.
            -->
            <video
              class="feed-video"
              class:is-ready={videoReadyFor(card.entity.id, source)}
              src={source}
              poster={cover?.src ?? undefined}
              muted
              autoplay
              loop
              playsinline
              preload="metadata"
              onloadeddata={() => markVideoReady(card.entity.id, source)}
              oncanplay={() => markVideoReady(card.entity.id, source)}
              ontimeupdate={(event) => trackVideoProgress(card.entity.id, event.currentTarget)}
            ></video>
          {:else if animatedSrc}
            <!--
              Animated stills (GIF/APNG) animate on their own as an <img>; the
              browser loops them with no controls. Overlaying the original source
              over the static poster keeps the same zero-reflow behaviour as the
              inline video and stops animating once the item scrolls out of the
              play window.
            -->
            <img class="feed-animated" src={animatedSrc} alt={card.entity.title} loading="lazy" />
          {/if}
        </NsfwBlur>

        {#if showVideo && source}
          <!-- Thin playback progress bar mirroring the lightbox player's minimal timeline. -->
          <div class="feed-progress" aria-hidden="true">
            <div
              class="feed-progress-fill"
              style:width={`${(progressById.get(card.entity.id) ?? 0) * 100}%`}
            ></div>
          </div>
        {/if}

        <!-- Capture taps above the player so the feed stays purely visual: a tap
             opens the lightbox (or links to the entity) rather than toggling play. -->
        {#if onActivate}
          <button
            type="button"
            class="feed-open"
            aria-label={`Open ${card.entity.title}`}
            onclick={() => onActivate?.(card, cards)}
          ></button>
        {:else if href}
          <a class="feed-open" {href} aria-label={`Open ${card.entity.title}`}></a>
        {/if}
      </div>

      <div class="feed-caption">
        <h3 class="feed-title">{card.entity.title}</h3>
        {#if card.meta && card.meta.length > 0}
          <div class="feed-meta">
            {#each card.meta as item (item.icon + item.label)}
              <span class="feed-chip">{item.label}</span>
            {/each}
          </div>
        {/if}
      </div>
    </article>
  {/each}
</div>

<style>
  .feed {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1.5rem;
    width: 100%;
  }

  .feed-item {
    width: 100%;
    /*
     * Default cap of 640px, further narrowed for tall items so that at the
     * available viewport height (--feed-max-h) the item's aspect box fits fully
     * on screen: max usable width = available-height x (w/h). Landscape items
     * keep the 640px cap; portrait items shrink instead of overflowing.
     */
    max-width: min(640px, calc(var(--feed-max-h, 70vh) * var(--ratio, 1)));
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-lg, 16px);
    background: var(--color-surface-raised, rgb(20 16 12 / 0.6));
    overflow: hidden;
    box-shadow: 0 1px 0 rgb(255 255 255 / 0.03) inset;
    /*
     * Contain the inline player's internal stacking (progress/control bars use
     * high z-indexes) so they can never paint over the sticky grid toolbar.
     */
    isolation: isolate;
    position: relative;
    z-index: 0;
  }

  .feed-media {
    position: relative;
    width: 100%;
    background: #000;
    line-height: 0;
  }

  /*
   * Until an item's size is known, the poster flows at its natural aspect so the
   * feed keeps variable heights; once measured, the box reserves that aspect and
   * the poster fills it (see `.has-aspect`).
   */
  .feed-poster {
    display: block;
    width: 100%;
    height: auto;
  }

  /*
   * Reserved-aspect mode: the box owns the height, so the poster and the inline
   * video both absolutely fill it (zero layout shift as the video mounts). The
   * box is capped to the available viewport height (set per-feed as --feed-max-h,
   * with a safe 70vh fallback); width follows from the capped height so a tall
   * portrait clip narrows to stay fully on screen rather than being cropped.
   */
  .feed-media.has-aspect {
    max-height: var(--feed-max-h, 70vh);
  }

  .feed-media.has-aspect .feed-poster {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  /* The inline video (or animated still) overlays the poster without affecting
     layout height. The item width already matches the asset aspect, so cover
     fills it exactly. */
  .feed-video,
  .feed-animated {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .feed-video {
    opacity: 0;
    transition: opacity 160ms ease;
  }

  .feed-video.is-ready {
    opacity: 1;
  }

  .feed-placeholder {
    width: 100%;
    aspect-ratio: 4 / 3;
  }

  /*
   * Thin playback progress bar pinned to the bottom of the media, above the tap
   * overlay (z-index 2) but non-interactive so taps still open the lightbox.
   */
  .feed-progress {
    position: absolute;
    inset-inline: 0;
    bottom: 0;
    z-index: 3;
    height: 3px;
    background: rgb(255 255 255 / 0.16);
    pointer-events: none;
  }

  .feed-progress-fill {
    height: 100%;
    width: 0;
    background: var(--color-text-accent, #f2c26a);
    box-shadow: 0 0 6px rgb(242 194 106 / 0.5);
    transition: width 0.12s linear;
  }

  .feed-open {
    position: absolute;
    inset: 0;
    z-index: 2;
    display: block;
    width: 100%;
    height: 100%;
    border: 0;
    margin: 0;
    padding: 0;
    background: transparent;
    cursor: pointer;
  }

  .feed-caption {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    padding: 0.85rem 1rem 1rem;
  }

  .feed-title {
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.95rem;
    font-weight: 600;
    line-height: 1.35;
    /* Distinct caption block that shows the entire title, wrapped. */
    overflow-wrap: anywhere;
    word-break: break-word;
    white-space: normal;
  }

  .feed-meta {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
  }

  .feed-chip {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.65rem;
    letter-spacing: 0.04em;
    color: var(--color-text-muted, rgb(220 220 220 / 0.7));
    background: var(--color-surface-well, rgb(255 255 255 / 0.05));
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs, 4px);
    padding: 0.15rem 0.45rem;
  }

  /*
   * Media-wall variant: just the media, no caption or card chrome, with tighter
   * spacing — the feed counterpart of the grid's media-wall mode.
   */
  .feed.is-media-wall {
    gap: 0.75rem;
  }

  .feed.is-media-wall .feed-item {
    border: none;
    border-radius: var(--radius-md, 10px);
    background: transparent;
    box-shadow: none;
  }

  .feed.is-media-wall .feed-caption {
    display: none;
  }
</style>
