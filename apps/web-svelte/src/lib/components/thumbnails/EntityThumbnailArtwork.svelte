<script lang="ts">
  import {
    ENTITY_ARTWORK_SURFACE,
    ENTITY_KIND_DEFINITIONS,
    THUMBNAIL_HOVER_KIND,
  } from "$lib/api/generated/codes";
  import { paletteFromImage, type ArtworkPalette } from "$lib/entities/artwork-palette";
  import {
    getThumbnailAsset,
    hasHoverPreview,
    iconForKind,
    placeholderGradient,
    toAspectRatioValue,
    type EntityThumbnailAsset,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import {
    lazyHoverAssetsFor,
    requestLazyHoverAssets,
  } from "$lib/entities/hover-image-loader.svelte";
  import { loadTrickplayFrames, type TrickplayFrame } from "@prismedia/ui-svelte";
  import { onDestroy } from "svelte";
  import EntityThumbnailIcon from "./EntityThumbnailIcon.svelte";
  import EntityThumbnailOverlays from "./EntityThumbnailOverlays.svelte";

  interface Props {
    artworkReactive: boolean;
    card: EntityThumbnailCard;
    density: "default" | "compact";
    focusActive: boolean;
    hoverPreviewsEnabled: boolean;
    hoverPreviewSuppressed?: () => boolean;
    imageFetchPriority: "auto" | "high" | "low";
    imageLoading: "eager" | "lazy";
    layout: "grid" | "list";
    mediaOnly: boolean;
    onActivationSuppressed: () => void;
    onArtworkLoad?: (image: HTMLImageElement) => void;
    onArtworkPalette: (entityId: string, palette: ArtworkPalette) => void;
    onHoverChange: (hovering: boolean) => void;
    onSelectedChange?: (selected: boolean) => void;
    selectable: boolean;
    selected: boolean;
    showBadges: boolean;
    showWantedBadge: boolean;
  }

  let {
    artworkReactive,
    card: cardProp,
    density,
    focusActive,
    hoverPreviewsEnabled,
    hoverPreviewSuppressed,
    imageFetchPriority,
    imageLoading,
    layout,
    mediaOnly,
    onActivationSuppressed,
    onArtworkLoad,
    onArtworkPalette,
    onHoverChange,
    onSelectedChange,
    selectable,
    selected,
    showBadges,
    showWantedBadge,
  }: Props = $props();

  // Cards no longer arrive with sampled child artwork inline; when this card has no hover
  // model, its hover intent requests the assets lazily and the card upgrades in place the
  // moment they land. Every existing derived keeps reading `card` untouched.
  const lazyAssets = $derived(
    cardProp.hover.kind === THUMBNAIL_HOVER_KIND.none && cardProp.entity.id
      ? lazyHoverAssetsFor(cardProp.entity.id)
      : undefined,
  );
  const card = $derived(
    lazyAssets !== undefined && lazyAssets.length > 0
      ? { ...cardProp, hover: { kind: THUMBNAIL_HOVER_KIND.imageSequence, assets: lazyAssets } }
      : cardProp,
  );

  let pointerRatio = $state<number | null>(null);
  let imageFailed = $state(false);
  let imageLoaded = $state(false);
  let hoverBroken = $state(false);
  let lastSrc = $state<string | undefined>(undefined);
  let hoverIntentTimer: number | null = null;
  let latestPointerRatio = 0.5;
  let pointerScrubbing = false;
  let capturedPointerId: number | null = null;
  let scrubStartClientX = 0;
  let scrubPointerType = "mouse";
  let suppressNextFocusPreview = false;
  let mediaEl: HTMLElement | undefined = $state();
  let touchScrubbing = false;
  let touchDirection: "none" | "scrub" | "scroll" = "none";
  let touchAllowHorizontalScroll = false;
  let touchStartX = 0;
  let touchStartY = 0;
  const TOUCH_DIR_SLOP = 10;
  let spriteFrames = $state<TrickplayFrame[] | null>(null);
  let spriteError = $state(false);

  const isSpriteHover = $derived(card.hover.kind === THUMBNAIL_HOVER_KIND.sprite);
  const isImageSequenceHover = $derived(card.hover.kind === THUMBNAIL_HOVER_KIND.imageSequence);
  const sequenceAssets = $derived.by(() => {
    if (card.hover.kind !== THUMBNAIL_HOVER_KIND.imageSequence) return [];
    return card.hover.assets.filter((asset, index, assets) =>
      assets.findIndex((candidate) => candidate.src === asset.src) === index,
    );
  });
  const asset = $derived(getThumbnailAsset(card, hoverBroken || isSpriteHover ? null : pointerRatio));
  const aspectRatio = $derived(toAspectRatioValue(card.aspectRatio));
  const imageFit = $derived(card.fit ?? "cover");
  const usesBrandPlate = $derived(
    ENTITY_KIND_DEFINITIONS[card.entity.kind].presentation.artworkSurface ===
      ENTITY_ARTWORK_SURFACE.brandPlate,
  );
  const placeholderIcon = $derived(iconForKind(card.entity.kind));
  const sequenceRestCover = $derived(
    isImageSequenceHover && !card.cover && sequenceAssets.length > 0 ? sequenceAssets[0] : null,
  );
  const showPlaceholder = $derived(isSpriteHover ? !card.cover : sequenceRestCover ? false : !asset || imageFailed);
  const gradient = $derived(placeholderGradient(card.entity.title));
  const hoverable = $derived(hasHoverPreview(card) && !hoverBroken && !spriteError);
  const activeSequenceIndex = $derived.by(() => {
    if (!isImageSequenceHover || hoverBroken || pointerRatio === null || sequenceAssets.length === 0) return -1;
    return Math.min(sequenceAssets.length - 1, Math.floor(Math.max(0, Math.min(1, pointerRatio)) * sequenceAssets.length));
  });
  const activeSequenceAsset = $derived(activeSequenceIndex >= 0 ? sequenceAssets[activeSequenceIndex] ?? null : null);
  const currentImageSrc = $derived(
    activeSequenceAsset?.src ?? sequenceRestCover?.src ??
      (isSpriteHover && card.cover ? card.cover.thumbSrc ?? card.cover.src : asset ? asset.thumbSrc ?? asset.src : undefined),
  );
  const showImageLoading = $derived(Boolean(currentImageSrc) && !showPlaceholder && !imageLoaded && !imageFailed);
  const activeSpriteFrame = $derived.by(() => {
    if (!isSpriteHover || !spriteFrames || pointerRatio === null) return null;
    return spriteFrames[Math.min(spriteFrames.length - 1, Math.floor(Math.max(0, Math.min(1, pointerRatio)) * spriteFrames.length))] ?? null;
  });
  const spriteDims = $derived.by(() => ({
    width: spriteFrames?.reduce((max, frame) => Math.max(max, frame.x + frame.width), 0) ?? 0,
    height: spriteFrames?.reduce((max, frame) => Math.max(max, frame.y + frame.height), 0) ?? 0,
  }));

  function coverSrcset(cover: EntityThumbnailAsset): string | undefined {
    if (!cover.thumbSrc) return undefined;
    return cover.thumbSrc2x ? `${cover.thumbSrc} 480w, ${cover.thumbSrc2x} 960w` : `${cover.thumbSrc} 480w`;
  }
  const coverSizes = "240px";

  function canUseHoverPreviews(): boolean {
    return hoverPreviewsEnabled && !(hoverPreviewSuppressed?.() ?? false);
  }

  async function ensureSpriteLoaded() {
    if (!isSpriteHover || spriteFrames || spriteError) return;
    const hover = card.hover as { kind: typeof THUMBNAIL_HOVER_KIND.sprite; spriteUrl?: string; vttUrl: string };
    try {
      if (hover.spriteUrl && typeof globalThis.Image !== "undefined") {
        const image = new globalThis.Image();
        image.src = hover.spriteUrl;
      }
      spriteFrames = await loadTrickplayFrames(hover.vttUrl);
    } catch (error) {
      console.warn("Failed to load thumbnail trickplay frames", error);
      spriteError = true;
    }
  }

  function clearHoverIntentTimer() {
    if (!hoverIntentTimer) return;
    window.clearTimeout(hoverIntentTimer);
    hoverIntentTimer = null;
  }
  function isInHorizontalScroller(): boolean {
    let element: HTMLElement | null = mediaEl?.parentElement ?? null;
    while (element) {
      if (element.scrollWidth > element.clientWidth + 1) {
        const overflowX = getComputedStyle(element).overflowX;
        if (overflowX === "auto" || overflowX === "scroll") return true;
      }
      element = element.parentElement;
    }
    return false;
  }
  function ratioFromClientX(clientX: number): number {
    if (!mediaEl) return latestPointerRatio;
    const bounds = mediaEl.getBoundingClientRect();
    return bounds.width > 0 ? Math.min(1, Math.max(0, (clientX - bounds.left) / bounds.width)) : latestPointerRatio;
  }
  function updatePointerRatio(event: PointerEvent) {
    if (!hoverable) return;
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    latestPointerRatio = bounds.width > 0 ? (event.clientX - bounds.left) / bounds.width : 0;
    if (pointerRatio !== null) pointerRatio = latestPointerRatio;
  }
  function activateHoverPreview() {
    if (!canUseHoverPreviews() || !hoverable) return;
    pointerRatio = latestPointerRatio;
    void ensureSpriteLoaded();
  }
  function clearHover() {
    clearHoverIntentTimer();
    pointerScrubbing = false;
    capturedPointerId = null;
    scrubPointerType = "mouse";
    pointerRatio = null;
  }
  function capturePointer(element: HTMLElement, pointerId: number) {
    element.setPointerCapture?.(pointerId);
    capturedPointerId = pointerId;
  }
  function releaseCapturedPointer(element: HTMLElement) {
    if (capturedPointerId === null) return;
    element.releasePointerCapture?.(capturedPointerId);
    capturedPointerId = null;
  }

  function handlePointerEnter(event: PointerEvent) {
    if (!canUseHoverPreviews()) return;
    if (cardProp.hover.kind === THUMBNAIL_HOVER_KIND.none && cardProp.entity.id) {
      requestLazyHoverAssets(cardProp.entity.id);
    }
    updatePointerRatio(event);
    clearHoverIntentTimer();
    hoverIntentTimer = window.setTimeout(() => {
      hoverIntentTimer = null;
      activateHoverPreview();
    }, 140);
  }
  function handlePointerMove(event: PointerEvent) {
    if (!canUseHoverPreviews()) return clearHover();
    if (scrubPointerType === "touch") return;
    if (pointerScrubbing) {
      updatePointerRatio(event);
      if (Math.abs(event.clientX - scrubStartClientX) > 6) onActivationSuppressed();
      event.preventDefault();
      event.stopPropagation();
      if (pointerRatio !== null) void ensureSpriteLoaded();
      return;
    }
    updatePointerRatio(event);
    if (pointerRatio !== null) void ensureSpriteLoaded();
  }
  function handlePointerDown(event: PointerEvent) {
    if (!canUseHoverPreviews() || !hoverable) return;
    scrubStartClientX = event.clientX;
    scrubPointerType = event.pointerType;
    pointerScrubbing = false;
    clearHoverIntentTimer();
    if (event.pointerType === "touch") return;
    if (mediaEl?.closest("a")) {
      suppressNextFocusPreview = true;
      return;
    }
    pointerScrubbing = true;
    updatePointerRatio(event);
    pointerRatio = latestPointerRatio;
    void ensureSpriteLoaded();
    capturePointer(event.currentTarget as HTMLElement, event.pointerId);
  }
  function handlePointerUp(event: PointerEvent) {
    suppressNextFocusPreview = false;
    if (!pointerScrubbing && scrubPointerType !== "touch") return;
    pointerScrubbing = false;
    scrubPointerType = "mouse";
    releaseCapturedPointer(event.currentTarget as HTMLElement);
  }
  function handlePointerCancel(event: PointerEvent) {
    releaseCapturedPointer(event.currentTarget as HTMLElement);
    clearHover();
  }
  function handlePointerLeave() { if (!pointerScrubbing) clearHover(); }
  function handleTouchStart(event: TouchEvent) {
    if (!hoverable || !canUseHoverPreviews() || event.touches.length !== 1) return;
    const touch = event.touches[0];
    touchStartX = touch.clientX;
    touchStartY = touch.clientY;
    touchScrubbing = false;
    touchDirection = "none";
    touchAllowHorizontalScroll = isInHorizontalScroller();
  }
  function handleTouchMove(event: TouchEvent) {
    const touch = event.touches[0];
    if (!touch || touchDirection === "scroll") return;
    if (touchDirection === "none") {
      const dx = touch.clientX - touchStartX;
      const dy = touch.clientY - touchStartY;
      if (Math.max(Math.abs(dx), Math.abs(dy)) < TOUCH_DIR_SLOP) return;
      if (Math.abs(dx) > Math.abs(dy) && !touchAllowHorizontalScroll) {
        touchDirection = "scrub";
        touchScrubbing = true;
        onActivationSuppressed();
        void ensureSpriteLoaded();
      } else {
        touchDirection = "scroll";
        clearHover();
        return;
      }
    }
    if (touchDirection === "scrub") {
      event.preventDefault();
      pointerRatio = ratioFromClientX(touch.clientX);
      void ensureSpriteLoaded();
    }
  }
  function handleTouchEnd() {
    if (touchScrubbing) pointerRatio = null;
    touchScrubbing = false;
    touchDirection = "none";
  }
  function markImageLoaded(event: Event) {
    imageLoaded = true;
    const image = event.currentTarget as HTMLImageElement;
    if (artworkReactive && pointerRatio === null) {
      const palette = paletteFromImage(image);
      if (palette) onArtworkPalette(card.entity.id, palette);
    }
    onArtworkLoad?.(image);
  }

  $effect(() => {
    if (currentImageSrc !== lastSrc) {
      lastSrc = currentImageSrc;
      imageFailed = false;
      imageLoaded = false;
    }
  });
  $effect(() => { onHoverChange(pointerRatio !== null); });
  $effect(() => {
    if (!hoverPreviewsEnabled && pointerRatio !== null) clearHover();
  });
  $effect(() => {
    if (!focusActive) return clearHover();
    if (suppressNextFocusPreview) {
      suppressNextFocusPreview = false;
      return;
    }
    if (!canUseHoverPreviews()) return;
    pointerRatio = hoverable ? 0.5 : null;
    void ensureSpriteLoaded();
  });
  $effect(() => {
    const element = mediaEl;
    if (!element) return;
    element.addEventListener("touchstart", handleTouchStart, { passive: true });
    element.addEventListener("touchmove", handleTouchMove, { passive: false });
    element.addEventListener("touchend", handleTouchEnd, { passive: true });
    element.addEventListener("touchcancel", handleTouchEnd, { passive: true });
    return () => {
      element.removeEventListener("touchstart", handleTouchStart);
      element.removeEventListener("touchmove", handleTouchMove);
      element.removeEventListener("touchend", handleTouchEnd);
      element.removeEventListener("touchcancel", handleTouchEnd);
    };
  });
  onDestroy(clearHoverIntentTimer);
</script>

<div
  bind:this={mediaEl}
  class="media"
  class:has-placeholder={showPlaceholder}
  class:has-logo-art={usesBrandPlate && !showPlaceholder}
  class:is-compact={density === "compact"}
  class:is-image-loading={showImageLoading}
  class:is-image-only={mediaOnly}
  class:is-list={layout === "list"}
  class:is-hovering={pointerRatio !== null}
  role="presentation"
  style:aspect-ratio={layout === "list" ? undefined : aspectRatio}
  style:background={showPlaceholder ? gradient : undefined}
  onpointerenter={handlePointerEnter}
  onpointerdown={handlePointerDown}
  onpointermove={handlePointerMove}
  onpointerup={handlePointerUp}
  onpointercancel={handlePointerCancel}
  onpointerleave={handlePointerLeave}
>
  {#if activeSequenceAsset}
    <img src={activeSequenceAsset.src} alt={activeSequenceAsset.alt} decoding="async" loading={imageLoading} fetchpriority={imageFetchPriority} referrerpolicy="no-referrer" style:object-fit={imageFit} onload={markImageLoaded} onerror={() => { imageFailed = true; hoverBroken = true; clearHover(); }} />
  {:else if sequenceRestCover}
    <img src={sequenceRestCover.src} alt={sequenceRestCover.alt} decoding="async" loading={imageLoading} fetchpriority={imageFetchPriority} referrerpolicy="no-referrer" style:object-fit={imageFit} onload={markImageLoaded} onerror={() => { imageFailed = true; }} />
  {:else if isSpriteHover && card.cover}
    <img src={card.cover.thumbSrc ?? card.cover.src} srcset={coverSrcset(card.cover)} sizes={card.cover.thumbSrc ? coverSizes : undefined} alt={card.cover.alt} decoding="async" loading={imageLoading} fetchpriority={imageFetchPriority} referrerpolicy="no-referrer" style:object-fit={imageFit} class:sprite-active={activeSpriteFrame !== null} onload={markImageLoaded} onerror={() => { imageFailed = true; }} />
  {:else if asset && !showPlaceholder}
    <img src={asset.thumbSrc ?? asset.src} srcset={coverSrcset(asset)} sizes={asset.thumbSrc ? coverSizes : undefined} alt={asset.alt} decoding="async" loading={imageLoading} fetchpriority={imageFetchPriority} referrerpolicy="no-referrer" style:object-fit={imageFit} onload={markImageLoaded} onerror={() => { imageFailed = true; if (pointerRatio !== null) { hoverBroken = true; pointerRatio = null; } }} />
  {:else}
    <div class="placeholder-glow" aria-hidden="true"></div>
    <div class="placeholder" aria-hidden="true">
      <EntityThumbnailIcon icon={placeholderIcon} variant="placeholder" />
    </div>
  {/if}
  {#if showImageLoading}<div class="image-loading-skeleton" aria-hidden="true"></div>{/if}
  {#if activeSpriteFrame && card.hover.kind === THUMBNAIL_HOVER_KIND.sprite && spriteDims.width > 0}
    <div class="sprite-overlay" aria-hidden="true" style:background-image="url({card.hover.spriteUrl ?? activeSpriteFrame.url})" style:background-size="{(spriteDims.width / activeSpriteFrame.width) * 100}% {(spriteDims.height / activeSpriteFrame.height) * 100}%" style:background-position="{spriteDims.width <= activeSpriteFrame.width ? 0 : (activeSpriteFrame.x / (spriteDims.width - activeSpriteFrame.width)) * 100}% {spriteDims.height <= activeSpriteFrame.height ? 0 : (activeSpriteFrame.y / (spriteDims.height - activeSpriteFrame.height)) * 100}%" style:background-repeat="no-repeat"></div>
  {/if}
  {#if isImageSequenceHover && sequenceAssets.length > 1 && !hoverBroken}
    <div class="sequence-rail" aria-hidden="true">{#each sequenceAssets as sequenceAsset, sequenceIndex (sequenceAsset.src)}<span class:is-active={activeSequenceIndex === sequenceIndex}></span>{/each}</div>
  {/if}
  <EntityThumbnailOverlays {card} {onSelectedChange} {selectable} {selected} {showWantedBadge} showBadges={showBadges && (layout !== "list" || mediaOnly)} />
</div>

<style>
  .media { position: relative; z-index: 2; box-sizing: border-box; width: 100%; min-height: 0; overflow: hidden; touch-action: pan-x pan-y; border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08)); border-radius: var(--radius-sm, 6px); background: radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.08), transparent 34%), linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92)), #111; box-shadow: var(--shadow-card); transition: transform 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)), border-color 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)), box-shadow 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)); }
  .media.is-list { flex: 0 0 auto; width: clamp(5.5rem, 30%, 7.5rem); border: 0; border-right: 1px solid rgb(255 255 255 / 0.1); border-radius: 5px 0 0 5px; box-shadow: none; transition: none; }
  .media.is-list.is-compact { width: 3.25rem; }
  .media.is-image-only { border-radius: var(--radius-sm, 6px); }
  .media.is-image-loading { background: linear-gradient(110deg, rgb(255 255 255 / 0.035) 8%, rgb(255 255 255 / 0.085) 18%, rgb(255 255 255 / 0.035) 33%), radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.08), transparent 34%), linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92)), #111; background-size: 220% 100%, auto, auto, auto; }
  .media.has-logo-art { background: radial-gradient(circle at 34% 24%, rgb(255 255 255 / 0.32), transparent 34%), linear-gradient(135deg, rgb(232 221 190 / 0.92) 0%, rgb(150 134 96 / 0.72) 45%, rgb(22 25 29 / 0.94) 100%), #b7aa86; }
  .media img, .placeholder { width: 100%; height: 100%; }
  .media img { position: relative; z-index: 1; display: block; object-fit: cover; object-position: center; transition: filter 160ms ease; }
  .media.is-list img { position: absolute; inset: 0; width: 100%; height: 100%; }
  .media.has-logo-art img { padding: clamp(0.85rem, 12%, 1.5rem); object-fit: contain !important; filter: drop-shadow(0 1px 2px rgb(0 0 0 / 0.42)); }
  .media.is-hovering img, :global(.entity-thumbnail:is(:hover, :focus-visible)) .media img { filter: brightness(0.98) contrast(1.02); }
  .media img.sprite-active { opacity: 0; }
  .image-loading-skeleton { position: absolute; inset: 0; z-index: 2; pointer-events: none; background: linear-gradient(110deg, transparent 0%, rgb(255 255 255 / 0.075) 42%, transparent 68%), linear-gradient(180deg, rgb(255 255 255 / 0.05), rgb(0 0 0 / 0.08)); background-size: 220% 100%, auto; animation: thumbnail-skeleton-shimmer 1.2s ease-in-out infinite; }
  .sprite-overlay { position: absolute; inset: 0; z-index: 1; }
  .sequence-rail { position: absolute; z-index: 3; right: 0.55rem; bottom: 0.45rem; left: 0.55rem; display: flex; gap: 0.18rem; pointer-events: none; }
  .sequence-rail span { flex: 1 1 0; min-width: 0; height: 0.16rem; background: rgb(255 255 255 / 0.24); box-shadow: 0 0 8px rgb(0 0 0 / 0.38); transition: background 120ms ease, box-shadow 120ms ease, transform 120ms ease; }
  .sequence-rail span.is-active { background: var(--entity-accent); box-shadow: none; transform: scaleY(1.35); }
  .placeholder-glow { position: absolute; inset: 0; pointer-events: none; background: radial-gradient(circle at top, rgb(245 239 213 / 0.16), transparent 38%), linear-gradient(180deg, rgb(7 8 11 / 0.06) 0%, rgb(7 8 11 / 0.55) 100%); }
  .placeholder { position: relative; display: flex; width: 100%; height: 100%; align-items: center; justify-content: center; }
  @keyframes thumbnail-skeleton-shimmer { from { background-position: 180% 0, 0 0; } to { background-position: -80% 0, 0 0; } }
  @media (prefers-reduced-motion: reduce) { .media { transition: none; } .image-loading-skeleton { animation: none; } }
</style>
