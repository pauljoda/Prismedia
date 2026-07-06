<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
  import { onDestroy, type Snippet } from "svelte";
  import {
    Album,
    BookOpen,
    Building2,
    Calendar,
    Clock3,
    Disc3,
    Film,
    Flame,
    FolderOpen,
    Hash,
    Image,
    Images,
    Layers,
    Music,
    Star,
    Tag,
    Users,
  } from "@lucide/svelte";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import { getRatingValue, isNsfw, isWanted } from "$lib/api/capabilities";
  import OverflowTicker from "$lib/components/OverflowTicker.svelte";
  import {
    getThumbnailAsset,
    hasHoverPreview,
    iconForKind,
    placeholderGradient,
    resolveEntityThumbnailHref,
    toAspectRatioValue,
    type EntityThumbnailCard,
    type EntityThumbnailMetaIcon,
  } from "$lib/entities/entity-thumbnail";
  import { loadTrickplayFrames, type TrickplayFrame } from "@prismedia/ui-svelte";

  type EntityThumbnailTitleAlign = "left" | "center" | "right";
  type EntityThumbnailTitleSize = "default" | "compact";
  type EntityThumbnailImageLoading = "eager" | "lazy";
  type EntityThumbnailImageFetchPriority = "auto" | "high" | "low";

  interface Props {
    card: EntityThumbnailCard;
    imageFetchPriority?: EntityThumbnailImageFetchPriority;
    imageLoading?: EntityThumbnailImageLoading;
    layout?: "grid" | "list";
    linkable?: boolean;
    linkTarget?: "_self" | "_blank" | "_parent" | "_top";
    mediaOnly?: boolean;
    hoverPreviewsEnabled?: boolean;
    hoverPreviewSuppressed?: () => boolean;
    interactive?: boolean;
    onActivate?: (card: EntityThumbnailCard) => void;
    onSelectedChange?: (selected: boolean) => void;
    selectable?: boolean;
    selectMode?: boolean;
    selected?: boolean;
    /** Show the wanted/status badge on the artwork. Off where a host surface renders status itself. */
    showWantedBadge?: boolean;
    subtitleContent?: Snippet<[EntityThumbnailCard]>;
    titleAlign?: EntityThumbnailTitleAlign;
    titleSize?: EntityThumbnailTitleSize;
  }

  let {
    card,
    imageFetchPriority = "low",
    imageLoading = "lazy",
    layout = "grid",
    linkable = true,
    linkTarget,
    mediaOnly = false,
    hoverPreviewsEnabled = true,
    hoverPreviewSuppressed,
    interactive = true,
    onActivate,
    onSelectedChange,
    selectable = false,
    selectMode = false,
    selected = false,
    showWantedBadge = true,
    subtitleContent,
    titleAlign = "left",
    titleSize = "default",
  }: Props = $props();

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
  let scrubStartClientY = 0;
  let scrubPointerType = "mouse";
  let suppressNextFocusPreview = false;
  let suppressNextActivation = false;
  // Touch scrubbing uses directional disambiguation: a tap navigates, a vertical drag
  // scrolls the page, and a horizontal drag scrubs the preview — no press-and-hold. A
  // non-passive touchmove preventDefault keeps the page still once a horizontal scrub is
  // locked in. Inside a horizontal scroller (e.g. a cast rail) horizontal means scroll, so
  // scrub is left off there and the row pans normally.
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
  const sequenceAssets = $derived(card.hover.kind === THUMBNAIL_HOVER_KIND.imageSequence ? card.hover.assets : []);
  const asset = $derived(getThumbnailAsset(card, hoverBroken || isSpriteHover ? null : pointerRatio));
  const aspectRatio = $derived(toAspectRatioValue(card.aspectRatio));
  const imageOnly = $derived(mediaOnly);
  const imageFit = $derived(card.fit ?? "cover");
  const isLogoLikeArtwork = $derived(card.cover?.role === "logo" || card.entity.kind === "studio");
  const placeholderIcon = $derived(iconForKind(card.entity.kind));
  const sequenceRestCover = $derived(
    isImageSequenceHover && !card.cover && sequenceAssets.length > 0 ? sequenceAssets[0] : null,
  );
  const showPlaceholder = $derived(
    isSpriteHover ? !card.cover : sequenceRestCover ? false : !asset || imageFailed,
  );
  const gradient = $derived(placeholderGradient(card.entity.title));

  const activeSequenceIndex = $derived.by(() => {
    if (!isImageSequenceHover || hoverBroken || pointerRatio === null || sequenceAssets.length === 0) return -1;
    const clamped = Math.max(0, Math.min(1, pointerRatio));
    return Math.min(sequenceAssets.length - 1, Math.floor(clamped * sequenceAssets.length));
  });
  const activeSequenceAsset = $derived(
    activeSequenceIndex >= 0 ? sequenceAssets[activeSequenceIndex] ?? null : null,
  );
  const currentImageSrc = $derived(
    activeSequenceAsset?.src ??
      sequenceRestCover?.src ??
      (isSpriteHover && card.cover ? card.cover.src : asset?.src),
  );
  const showImageLoading = $derived(Boolean(currentImageSrc) && !showPlaceholder && !imageLoaded && !imageFailed);

  const activeSpriteFrame = $derived.by(() => {
    if (!isSpriteHover || !spriteFrames || pointerRatio === null) return null;
    const clamped = Math.max(0, Math.min(1, pointerRatio));
    const idx = Math.min(spriteFrames.length - 1, Math.floor(clamped * spriteFrames.length));
    return spriteFrames[idx] ?? null;
  });

  const spriteDims = $derived.by(() => {
    if (!spriteFrames) return { width: 0, height: 0 };
    return {
      width: spriteFrames.reduce((max, f) => Math.max(max, f.x + f.width), 0),
      height: spriteFrames.reduce((max, f) => Math.max(max, f.y + f.height), 0),
    };
  });

  async function ensureSpriteLoaded() {
    if (!isSpriteHover || spriteFrames || spriteError) return;
    const hover = card.hover as { kind: typeof THUMBNAIL_HOVER_KIND.sprite; spriteUrl?: string; vttUrl: string };
    try {
      if (hover.spriteUrl && typeof globalThis.Image !== "undefined") {
        const img = new globalThis.Image();
        img.src = hover.spriteUrl;
      }
      spriteFrames = await loadTrickplayFrames(hover.vttUrl);
    } catch (err) {
      console.warn("Failed to load thumbnail trickplay frames", err);
      spriteError = true;
    }
  }

  function clearHoverIntentTimer() {
    if (!hoverIntentTimer) return;
    window.clearTimeout(hoverIntentTimer);
    hoverIntentTimer = null;
  }

  // Walks up from the media element to see if any ancestor scrolls horizontally.
  // When one does, a horizontal drag is meant to pan that row, so touch scrub is
  // disabled and the gesture is left to the browser.
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
    if (bounds.width <= 0) return latestPointerRatio;
    return Math.min(1, Math.max(0, (clientX - bounds.left) / bounds.width));
  }

  function handleTouchStart(event: TouchEvent) {
    if (!hoverable || !canUseHoverPreviews() || event.touches.length !== 1) {
      return;
    }

    const touch = event.touches[0];
    touchStartX = touch.clientX;
    touchStartY = touch.clientY;
    touchScrubbing = false;
    touchDirection = "none";
    // Decided once per gesture: inside a horizontal rail, horizontal drags scroll the row.
    touchAllowHorizontalScroll = isInHorizontalScroller();
  }

  function handleTouchMove(event: TouchEvent) {
    const touch = event.touches[0];
    if (!touch) {
      return;
    }

    // A vertical drag already resolved to a scroll — leave it to the browser.
    if (touchDirection === "scroll") {
      return;
    }

    if (touchDirection === "none") {
      const dx = touch.clientX - touchStartX;
      const dy = touch.clientY - touchStartY;
      if (Math.max(Math.abs(dx), Math.abs(dy)) < TOUCH_DIR_SLOP) {
        return;
      }
      // Horizontal drags scrub, unless this card lives in a horizontal scroller where
      // horizontal means scroll. Everything else (vertical) is a scroll gesture.
      if (Math.abs(dx) > Math.abs(dy) && !touchAllowHorizontalScroll) {
        touchDirection = "scrub";
        touchScrubbing = true;
        suppressNextActivation = true;
        void ensureSpriteLoaded();
      } else {
        touchDirection = "scroll";
        clearHoverIntentTimer();
        pointerRatio = null;
        return;
      }
    }

    if (touchDirection === "scrub") {
      // Cancel the scroll so the horizontal drag scrubs. A non-passive touchmove
      // preventDefault stops scrolling even when touch-action permits panning.
      event.preventDefault();
      pointerRatio = ratioFromClientX(touch.clientX);
      void ensureSpriteLoaded();
    }
  }

  function handleTouchEnd() {
    if (touchScrubbing) {
      pointerRatio = null;
    }

    touchScrubbing = false;
    touchDirection = "none";
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

  function activateHoverPreview() {
    if (!canUseHoverPreviews() || !hoverable) return;
    pointerRatio = latestPointerRatio;
    void ensureSpriteLoaded();
  }

  $effect(() => {
    if (currentImageSrc !== lastSrc) {
      lastSrc = currentImageSrc;
      imageFailed = false;
      imageLoaded = false;
    }
  });
  $effect(() => {
    if (!hoverPreviewsEnabled && pointerRatio !== null) {
      clearHover();
    }
  });
  const hoverable = $derived(hasHoverPreview(card) && !hoverBroken && !spriteError);
  const nsfw = $derived(isNsfw(card.entity.capabilities));
  const wanted = $derived(isWanted(card.entity.capabilities));
  // A wanted item's badge reflects what its acquisition is doing (icon + short label + tone), so its
  // state reads from the grid — falling back to a plain "Wanted" when no acquisition status is known.
  const wantedDisplay = $derived(acquisitionStatusDisplay(card.wantedStatus));
  const rating = $derived(getRatingValue(card.entity.capabilities));
  const bottomLeft = $derived(card.custom?.bottomLeft);
  // Playback/reading progress meter percentage, only when there is something to show.
  const progressPercent = $derived(
    card.progress != null && card.progress > 0
      ? Math.min(100, Math.max(0, card.progress * 100))
      : null,
  );
  const href = $derived(interactive && linkable ? resolveEntityThumbnailHref(card) : undefined);
  const inSelectMode = $derived(selectMode && selectable);
  const effectiveHref = $derived(inSelectMode ? undefined : href);
  const selectionRole = $derived(
    !interactive
      ? undefined
      : (onActivate && !effectiveHref)
        ? "button"
        : inSelectMode || (!href && selectable) ? "checkbox" : href ? undefined : "group",
  );
  const selectionTabIndex = $derived(interactive && !effectiveHref ? 0 : undefined);

  function canUseHoverPreviews(): boolean {
    return hoverPreviewsEnabled && !(hoverPreviewSuppressed?.() ?? false);
  }

  function updatePointerRatio(event: PointerEvent) {
    if (!hoverable) return;
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    latestPointerRatio = bounds.width > 0 ? (event.clientX - bounds.left) / bounds.width : 0;
    if (pointerRatio !== null) pointerRatio = latestPointerRatio;
  }

  function handlePointerEnter(event: PointerEvent) {
    if (!canUseHoverPreviews()) return;
    updatePointerRatio(event);
    clearHoverIntentTimer();
    hoverIntentTimer = window.setTimeout(() => {
      hoverIntentTimer = null;
      activateHoverPreview();
    }, 140);
  }

  function handlePointerMove(event: PointerEvent) {
    if (!canUseHoverPreviews()) {
      clearHover();
      return;
    }
    // Touch drags scroll the row (governed by touch-action); scrubbing is a pointer-hover
    // interaction, so a touch move never hijacks the gesture into the scrubber.
    if (scrubPointerType === "touch") {
      return;
    }

    if (pointerScrubbing) {
      updatePointerRatio(event);
      if (Math.abs(event.clientX - scrubStartClientX) > 6) {
        suppressNextActivation = true;
      }
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
    scrubStartClientY = event.clientY;
    scrubPointerType = event.pointerType;
    pointerScrubbing = false;
    suppressNextActivation = false;
    clearHoverIntentTimer();
    if (event.pointerType === "touch") {
      // Touch never scrubs: let the gesture scroll the row or resolve to a tap.
      return;
    }
    if (effectiveHref) {
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

  function handlePointerLeave() {
    if (pointerScrubbing) return;
    clearHover();
  }

  function handleFocus() {
    if (suppressNextFocusPreview) {
      suppressNextFocusPreview = false;
      return;
    }
    if (!canUseHoverPreviews()) return;
    pointerRatio = hoverable ? 0.5 : null;
    void ensureSpriteLoaded();
  }

  function clearHover() {
    clearHoverIntentTimer();
    pointerScrubbing = false;
    capturedPointerId = null;
    scrubPointerType = "mouse";
    pointerRatio = null;
  }

  function handleSelectionChange(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    onSelectedChange?.(input.checked);
  }

  function toggleSurfaceSelection() {
    if (!selectable) return;
    if (!inSelectMode && href) return;
    onSelectedChange?.(!selected);
  }

  function handleSurfaceClick(event: MouseEvent) {
    if (!interactive) return;

    if (suppressNextActivation) {
      suppressNextActivation = false;
      event.preventDefault();
      event.stopPropagation();
      return;
    }

    if (onActivate && !effectiveHref) {
      onActivate(card);
      return;
    }

    toggleSurfaceSelection();
  }

  function handleSurfaceKeydown(event: KeyboardEvent) {
    if (!interactive) return;
    if (event.key !== "Enter" && event.key !== " ") return;
    if (onActivate && !effectiveHref) {
      event.preventDefault();
      onActivate(card);
      return;
    }

    if (!selectable || (!inSelectMode && href)) return;
    event.preventDefault();
    toggleSurfaceSelection();
  }

  function stopSelectionActivation(event: Event) {
    event.stopPropagation();
  }

  function markImageLoaded() {
    imageLoaded = true;
  }

  function formatRating(value: number): string {
    if (value <= 0) return "";
    return String(Math.round(value));
  }


  // touchmove must be non-passive so preventDefault can cancel scrolling while scrubbing; Svelte's
  // inline handlers can register as passive, so attach the listeners explicitly.
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

  onDestroy(() => {
    clearHoverIntentTimer();
  });
</script>

<svelte:element
  this={effectiveHref ? "a" : "article"}
  href={effectiveHref || undefined}
  target={effectiveHref ? linkTarget : undefined}
  rel={effectiveHref && linkTarget === "_blank" ? "noopener noreferrer" : undefined}
  role={selectionRole}
  tabindex={selectionTabIndex}
  class="entity-thumbnail"
  class:is-hovering={pointerRatio !== null}
  class:is-image-only={imageOnly}
  class:is-list={layout === "list"}
  class:is-selected={selected}
  class:is-select-mode={inSelectMode}
  class:is-static={!interactive}
  style:aspect-ratio={layout === "list" || !imageOnly ? undefined : aspectRatio}
  aria-label={card.entity.title}
  aria-checked={interactive && !onActivate && (inSelectMode || (!href && selectable)) ? selected : undefined}
  onblur={clearHover}
  onclick={handleSurfaceClick}
  onfocus={handleFocus}
  onkeydown={handleSurfaceKeydown}
>
  <div
    bind:this={mediaEl}
    class="media"
    class:has-placeholder={showPlaceholder}
    class:has-logo-art={isLogoLikeArtwork && !showPlaceholder}
    class:is-image-loading={showImageLoading}
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
      <img
        src={activeSequenceAsset.src}
        alt={activeSequenceAsset.alt}
        decoding="async"
        loading={imageLoading}
        fetchpriority={imageFetchPriority}
        style:object-fit={imageFit}
        onload={markImageLoaded}
        onerror={() => {
          imageFailed = true;
          hoverBroken = true;
          clearHover();
        }}
      />
    {:else if sequenceRestCover}
      <img
        src={sequenceRestCover.src}
        alt={sequenceRestCover.alt}
        decoding="async"
        loading={imageLoading}
        fetchpriority={imageFetchPriority}
        style:object-fit={imageFit}
        onload={markImageLoaded}
        onerror={() => { imageFailed = true; }}
      />
    {:else if isSpriteHover && card.cover}
      <img
        src={card.cover.src}
        srcset={card.cover.thumbSrc ? `${card.cover.thumbSrc} 480w, ${card.cover.src} 1600w` : undefined}
        sizes={card.cover.thumbSrc ? "auto" : undefined}
        alt={card.cover.alt}
        decoding="async"
        loading={imageLoading}
        fetchpriority={imageFetchPriority}
        style:object-fit={imageFit}
        class:sprite-active={activeSpriteFrame !== null}
        onload={markImageLoaded}
        onerror={() => { imageFailed = true; }}
      />
    {:else if asset && !showPlaceholder}
      <img
        src={asset.src}
        srcset={asset.thumbSrc ? `${asset.thumbSrc} 480w, ${asset.src} 1600w` : undefined}
        sizes={asset.thumbSrc ? "auto" : undefined}
        alt={asset.alt}
        decoding="async"
        loading={imageLoading}
        fetchpriority={imageFetchPriority}
        style:object-fit={imageFit}
        onload={markImageLoaded}
        onerror={() => {
          imageFailed = true;
          if (pointerRatio !== null) {
            hoverBroken = true;
            pointerRatio = null;
          }
        }}
      />
    {:else}
      <div class="placeholder-glow" aria-hidden="true"></div>
      <div class="placeholder" aria-hidden="true">
        {@render PlaceholderIcon({ icon: placeholderIcon })}
      </div>
    {/if}

    {#if showImageLoading}
      <div class="image-loading-skeleton" aria-hidden="true"></div>
    {/if}

    {#if activeSpriteFrame && card.hover.kind === THUMBNAIL_HOVER_KIND.sprite && spriteDims.width > 0}
      <div class="sprite-overlay" aria-hidden="true"
        style:background-image="url({card.hover.spriteUrl ?? activeSpriteFrame.url})"
        style:background-size="{(spriteDims.width / activeSpriteFrame.width) * 100}% {(spriteDims.height / activeSpriteFrame.height) * 100}%"
        style:background-position="{spriteDims.width <= activeSpriteFrame.width ? 0 : (activeSpriteFrame.x / (spriteDims.width - activeSpriteFrame.width)) * 100}% {spriteDims.height <= activeSpriteFrame.height ? 0 : (activeSpriteFrame.y / (spriteDims.height - activeSpriteFrame.height)) * 100}%"
        style:background-repeat="no-repeat"
      ></div>
    {/if}

    {#if isImageSequenceHover && sequenceAssets.length > 1 && !hoverBroken}
      <div class="sequence-rail" aria-hidden="true">
        {#each sequenceAssets as sequenceAsset, sequenceIndex (sequenceAsset.src)}
          <span class:is-active={activeSequenceIndex === sequenceIndex}></span>
        {/each}
      </div>
    {/if}


    {#if selectable}
      <input
        class="selection"
        class:is-selected={selected}
        type="checkbox"
        checked={selected}
        title={`Select ${card.entity.title}`}
        aria-label={`Select ${card.entity.title}`}
        onclick={stopSelectionActivation}
        onpointerdown={stopSelectionActivation}
        onchange={handleSelectionChange}
      />
    {/if}

    {#if (wanted && showWantedBadge) || nsfw}
      <div class="badges top-badges">
        {#if wanted && showWantedBadge}
          {@const WantedIcon = wantedDisplay.icon}
          <span class={`badge wanted-badge wb-${wantedDisplay.tone}`} title={`Wanted — ${wantedDisplay.label}`} aria-label={`Wanted — ${wantedDisplay.label}`}>
            <WantedIcon size={11} />
            <span class="wanted-label">{wantedDisplay.label}</span>
          </span>
        {/if}
        {#if nsfw}
          <span class="badge danger icon-only" title="NSFW" aria-label="NSFW">
            <Flame size={13} />
          </span>
        {/if}
      </div>
    {/if}

    {#if bottomLeft}
      <div class="badges bottom-left-badges">
        <span class="badge position-badge" title={bottomLeft.title ?? bottomLeft.label}>
          {bottomLeft.label}
        </span>
      </div>
    {/if}

    {#if rating > 0}
      <div class="badges bottom-right-badges">
        <span class="badge rating-badge" title={`Rating ${formatRating(rating)}`} aria-label={`Rating ${formatRating(rating)}`}>
          {formatRating(rating)}
          <Star size={11} />
        </span>
      </div>
    {/if}

    {#if progressPercent != null}
      <div class="progress-meter" aria-hidden="true">
        <span class="progress-meter-fill" style:width={`${progressPercent}%`}></span>
      </div>
    {/if}

  </div>

  {#if !imageOnly}
    <div class="glass-info" class:has-subtitle={Boolean(card.subtitle || subtitleContent)}>
      <div class="copy">
        <h3 class={`title-align-${titleAlign} title-size-${titleSize}`} title={card.entity.title} aria-label={card.entity.title}>
          {card.entity.title}
        </h3>
        {#if subtitleContent}
          <div class={`custom-subtitle title-align-${titleAlign}`}>
            {@render subtitleContent(card)}
          </div>
        {/if}
        {#if card.subtitle && !subtitleContent}
          <div class={`subtitle title-align-${titleAlign}`} title={card.subtitle}>
            <OverflowTicker text={card.subtitle} align={titleAlign} />
          </div>
        {/if}
      </div>

      {#if card.meta?.length}
        <div class="chips">
          {#if card.meta?.length}
            {#each card.meta as item (item.icon + item.label)}
              <span class="chip">
                {@render MetaIcon({ icon: item.icon })}
                {item.label}
              </span>
            {/each}
          {/if}
        </div>
      {/if}
    </div>
  {/if}
</svelte:element>

{#snippet PlaceholderIcon({ icon }: { icon: EntityThumbnailMetaIcon })}
  {#if icon === "video"}
    <div class="placeholder-frame">
      <Film class="placeholder-icon-framed" />
    </div>
  {:else if icon === "audio"}
    <div class="placeholder-audio">
      <Disc3 class="placeholder-disc" />
      <Music class="placeholder-note" />
    </div>
  {:else if icon === "person"}
    <Users class="placeholder-icon" />
  {:else if icon === "book"}
    <BookOpen class="placeholder-icon" />
  {:else if icon === "gallery"}
    <Layers class="placeholder-icon" />
  {:else if icon === "image"}
    <Image class="placeholder-icon" />
  {:else if icon === "studio"}
    <Building2 class="placeholder-icon" />
  {:else if icon === "tag"}
    <Tag class="placeholder-icon" />
  {:else if icon === "collection"}
    <FolderOpen class="placeholder-icon" />
  {:else}
    <Hash class="placeholder-icon" />
  {/if}
{/snippet}

{#snippet MetaIcon({ icon }: { icon: EntityThumbnailMetaIcon })}
  {#if icon === "audio"}
    <Music size={12} />
  {:else if icon === "book"}
    <BookOpen size={12} />
  {:else if icon === "calendar"}
    <Calendar size={12} />
  {:else if icon === "chapter"}
    <Album size={12} />
  {:else if icon === "collection"}
    <Layers size={12} />
  {:else if icon === "duration"}
    <Clock3 size={12} />
  {:else if icon === "gallery"}
    <Images size={12} />
  {:else if icon === "image"}
    <Images size={12} />
  {:else if icon === "person"}
    <Users size={12} />
  {:else if icon === "studio"}
    <Building2 size={12} />
  {:else if icon === "tag"}
    <Tag size={12} />
  {:else if icon === "video"}
    <Film size={12} />
  {:else}
    <Hash size={12} />
  {/if}
{/snippet}

<style>
  .entity-thumbnail {
    position: relative;
    display: flex;
    flex-direction: column;
    container-type: inline-size;
    color: var(--color-text, #f4efe6);
    text-decoration: none;
    min-width: 0;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: 6px;
    box-shadow: var(--shadow-card);
    transition:
      transform 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      border-color 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow 200ms var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .entity-thumbnail:is(:hover, :focus-visible) {
    transform: translateY(-1px);
    border-color: var(--color-border-accent, rgb(242 194 106 / 0.32));
    box-shadow:
      var(--shadow-card-hover),
      0 0 12px rgb(242 194 106 / 0.07),
      0 0 4px rgb(242 194 106 / 0.10);
  }

  .entity-thumbnail.is-selected {
    border-color: var(--color-border-accent-strong, rgb(242 194 106 / 0.6));
    box-shadow: var(--shadow-focus-accent), var(--shadow-glow-accent-strong);
  }

  .entity-thumbnail.is-static {
    pointer-events: none;
  }

  .entity-thumbnail.is-static:is(:hover, :focus-visible) {
    transform: none;
    border-color: var(--color-border-subtle, rgb(255 255 255 / 0.08));
    box-shadow: var(--shadow-card);
  }

  @media (prefers-reduced-motion: reduce) {
    .entity-thumbnail {
      transition: none;
    }

    .entity-thumbnail:is(:hover, :focus-visible) {
      transform: none;
    }
  }

  .entity-thumbnail.is-list {
    flex-direction: row;
    inline-size: 100%;
    min-block-size: 5.25rem;
    border: 1px solid rgb(255 255 255 / 0.08);
    background: rgb(12 12 13 / 0.92);
    box-shadow:
      inset 0 0 0 1px rgb(0 0 0 / 0.5),
      0 2px 6px rgb(0 0 0 / 0.32);
  }

  .entity-thumbnail.is-list .media {
    flex: 0 0 auto;
    width: clamp(5.5rem, 30%, 7.5rem);
    border-radius: 5px 0 0 5px;
    box-shadow: none;
    border-right: 1px solid rgb(255 255 255 / 0.1);
  }

  .media {
    position: relative;
    z-index: 2;
    width: 100%;
    min-height: 0;
    overflow: hidden;
    /* Allow the browser to pan in both axes so horizontal thumbnail rows scroll on touch.
       Trickplay scrubbing is a pointer-hover (desktop) interaction and is unaffected by this. */
    touch-action: pan-x pan-y;
    border-radius: 5px 5px 0 0;
    background:
      radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.08), transparent 34%),
      linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92)),
      #111;
  }

  /* Playback/reading progress meter pinned to the bottom edge of the artwork. */
  .progress-meter {
    position: absolute;
    inset: auto 0 0 0;
    z-index: 4;
    height: 3px;
    background: rgb(0 0 0 / 0.45);
  }

  .progress-meter-fill {
    display: block;
    height: 100%;
    background: linear-gradient(135deg, #7a5e20 0%, #d59a2a 58%, #f2c26a 100%);
    box-shadow: 0 0 6px rgb(242 194 106 / 0.55);
  }

  .media.is-image-loading {
    background:
      linear-gradient(110deg, rgb(255 255 255 / 0.04) 8%, rgb(242 194 106 / 0.11) 18%, rgb(255 255 255 / 0.04) 33%),
      radial-gradient(circle at 50% 45%, rgb(255 255 255 / 0.08), transparent 34%),
      linear-gradient(135deg, rgb(15 16 18 / 0.96), rgb(28 25 20 / 0.92)),
      #111;
    background-size:
      220% 100%,
      auto,
      auto,
      auto;
  }

  .media.has-logo-art {
    background:
      radial-gradient(circle at 34% 24%, rgb(255 255 255 / 0.32), transparent 34%),
      linear-gradient(135deg, rgb(232 221 190 / 0.92) 0%, rgb(150 134 96 / 0.72) 45%, rgb(22 25 29 / 0.94) 100%),
      #b7aa86;
  }

  .entity-thumbnail.is-image-only .media {
    border-radius: 5px;
  }

  .entity-thumbnail.is-list .media img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
  }

  .media img,
  .placeholder {
    width: 100%;
    height: 100%;
  }

  .media img {
    position: relative;
    z-index: 1;
    display: block;
    object-fit: cover;
    object-position: center;
    transition:
      filter 160ms ease;
  }

  .media.has-logo-art img {
    padding: clamp(0.85rem, 12%, 1.5rem);
    object-fit: contain !important;
    filter: drop-shadow(0 1px 2px rgb(0 0 0 / 0.42));
  }

  .image-loading-skeleton {
    position: absolute;
    inset: 0;
    z-index: 2;
    pointer-events: none;
    background:
      linear-gradient(110deg, transparent 0%, rgb(242 194 106 / 0.12) 42%, transparent 68%),
      linear-gradient(180deg, rgb(255 255 255 / 0.05), rgb(0 0 0 / 0.08));
    background-size: 220% 100%, auto;
    animation: thumbnail-skeleton-shimmer 1.2s ease-in-out infinite;
  }

  .entity-thumbnail:is(:hover, :focus-visible) .media img,
  .entity-thumbnail.is-hovering .media img {
    filter: saturate(1.06) contrast(1.04);
  }

  .media img.sprite-active,
  .media img:global(.sprite-active) {
    opacity: 0;
  }

  .sequence-rail {
    position: absolute;
    z-index: 3;
    left: 0.55rem;
    right: 0.55rem;
    bottom: 0.45rem;
    display: flex;
    gap: 0.18rem;
    pointer-events: none;
  }

  .sequence-rail span {
    min-width: 0;
    height: 0.16rem;
    flex: 1 1 0;
    background: rgb(255 255 255 / 0.24);
    box-shadow: 0 0 8px rgb(0 0 0 / 0.38);
    transition:
      background 120ms ease,
      box-shadow 120ms ease,
      transform 120ms ease;
  }

  .sequence-rail span.is-active {
    background: rgb(242 194 106 / 0.95);
    box-shadow: 0 0 10px rgb(242 194 106 / 0.55);
    transform: scaleY(1.35);
  }

  .sprite-overlay {
    position: absolute;
    inset: 0;
    z-index: 1;
  }

  .placeholder-glow {
    position: absolute;
    inset: 0;
    background:
      radial-gradient(circle at top, rgb(245 239 213 / 0.16), transparent 38%),
      linear-gradient(180deg, rgb(7 8 11 / 0.06) 0%, rgb(7 8 11 / 0.55) 100%);
    pointer-events: none;
  }

  .placeholder {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
  }

  .placeholder-frame {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 3.5rem;
    height: 3.5rem;
    border: 1px solid rgb(242 194 106 / 0.25);
    background: linear-gradient(160deg, rgb(18 20 24 / 0.96), rgb(7 8 11 / 0.98));
    box-shadow:
      inset 0 1px 0 rgb(255 255 255 / 0.08),
      0 0 24px rgb(0 0 0 / 0.35);
  }

  .placeholder :global(.placeholder-icon-framed) {
    width: 1.75rem;
    height: 1.75rem;
    color: rgb(231 211 175 / 0.85);
    filter: drop-shadow(0 0 14px rgb(242 194 106 / 0.24));
  }

  .placeholder :global(.placeholder-icon) {
    width: 2rem;
    height: 2rem;
    color: rgb(255 255 255 / 0.25);
  }

  .placeholder-audio {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .placeholder :global(.placeholder-disc) {
    width: 3.5rem;
    height: 3.5rem;
    color: rgb(255 255 255 / 0.15);
  }

  .placeholder :global(.placeholder-note) {
    position: absolute;
    width: 1.5rem;
    height: 1.5rem;
    color: rgb(255 255 255 / 0.4);
  }

  @keyframes thumbnail-skeleton-shimmer {
    from { background-position: 180% 0, 0 0; }
    to { background-position: -80% 0, 0 0; }
  }

  @media (prefers-reduced-motion: reduce) {
    .image-loading-skeleton {
      animation: none;
    }
  }

  .glass-info {
    position: relative;
    z-index: 1;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 0.25rem;
    min-width: 0;
    padding: 0.5rem 0.6rem;
    border-top: 1px solid rgb(255 255 255 / 0.05);
    background:
      linear-gradient(
        180deg,
        rgb(20 22 26 / 0.95) 0%,
        rgb(13 14 17) 100%
      );
    overflow: hidden;
    pointer-events: none;
  }

  .glass-info.has-subtitle {
    gap: 0.15rem;
  }

  .badges {
    position: absolute;
    z-index: 3;
    right: 0.45rem;
    left: 2.45rem;
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
    align-items: center;
    justify-content: flex-end;
    pointer-events: none;
  }

  .top-badges {
    top: 0.45rem;
  }

  /* Position / episode chip: pinned to the TOP-left corner so it balances the wanted/NSFW badges on
     the top-right and reads evenly across a grid. (The selection checkbox shares this corner but only
     appears on hover / in select mode, when it takes over.) */
  .bottom-left-badges {
    top: 0.45rem;
    left: 0.45rem;
    right: auto;
    justify-content: flex-start;
  }

  .bottom-right-badges {
    bottom: 0.45rem;
    right: 0.45rem;
    left: auto;
    justify-content: flex-end;
  }

  .badge {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    border: 1px solid rgb(255 255 255 / 0.12);
    border-radius: var(--radius-xs, 4px);
    background: rgb(11 11 12 / 0.72);
    color: rgb(244 239 230 / 0.88);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    line-height: 1;
    letter-spacing: 0;
    min-height: 1.35rem;
    padding: 0.25rem 0.38rem;
  }

  .badge :global(svg) {
    flex: 0 0 auto;
  }

  .danger {
    color: #ff806f;
    border-color: rgb(255 92 67 / 0.42);
    background: rgb(40 13 10 / 0.76);
    box-shadow: 0 0 14px rgb(255 92 67 / 0.12);
  }

  .position-badge {
    color: rgb(244 239 230 / 0.9);
    border-color: rgb(242 194 106 / 0.34);
    background: rgb(9 10 11 / 0.78);
    box-shadow: 0 0 14px rgb(0 0 0 / 0.18);
  }

  .rating-badge {
    gap: 0.18rem;
    color: rgb(242 194 106 / 0.96);
    border-color: rgb(242 194 106 / 0.38);
    background: rgb(39 29 12 / 0.76);
    box-shadow: 0 0 16px rgb(242 194 106 / 0.18);
  }

  /* Wanted placeholder: icon + short status, tone-keyed to what the acquisition is doing. */
  .wanted-badge {
    gap: 0.28rem;
    color: rgb(242 194 106 / 0.96);
    border-color: rgb(242 194 106 / 0.42);
    background: rgb(39 29 12 / 0.82);
    box-shadow: 0 0 14px rgb(0 0 0 / 0.3);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-size: 0.58rem;
    font-weight: 600;
  }
  .wanted-badge :global(svg) { flex: 0 0 auto; }
  .wb-downloading { color: #f2c26a; border-color: rgb(242 194 106 / 0.5); background: rgb(52 38 14 / 0.85); box-shadow: 0 0 14px rgb(242 194 106 / 0.18); }
  .wb-searching { color: #e7d3af; border-color: rgb(211 176 106 / 0.4); background: rgb(40 33 18 / 0.85); }
  .wb-attention { color: #f2c26a; border-color: rgb(242 194 106 / 0.48); background: rgb(52 36 12 / 0.85); }
  .wb-queued { color: rgb(224 228 236 / 0.9); border-color: rgb(255 255 255 / 0.22); background: rgb(18 20 24 / 0.85); }
  .wb-failed { color: #ff9a86; border-color: rgb(255 122 92 / 0.46); background: rgb(44 16 12 / 0.85); box-shadow: 0 0 14px rgb(255 92 67 / 0.14); }
  .wb-wanted { color: rgb(242 194 106 / 0.96); border-color: rgb(242 194 106 / 0.42); background: rgb(39 29 12 / 0.82); }

  .rating-badge :global(svg) {
    fill: currentColor;
    filter: drop-shadow(0 0 4px rgb(242 194 106 / 0.35));
  }

  .icon-only {
    justify-content: center;
    inline-size: 1.35rem;
    padding-inline: 0;
  }

  .selection {
    position: absolute;
    top: 0.45rem;
    left: 0.45rem;
    z-index: 6;
    display: grid;
    inline-size: 1.55rem;
    block-size: 1.55rem;
    border: 1px solid rgb(255 255 255 / 0.12);
    border-radius: var(--radius-xs, 4px);
    background: rgb(11 11 12 / 0.72);
    appearance: none;
    cursor: pointer;
    opacity: 0;
    pointer-events: none;
    transition:
      opacity 120ms ease,
      border-color 120ms ease,
      box-shadow 120ms ease;
  }

  .entity-thumbnail:is(:hover, :focus-within) .selection,
  .entity-thumbnail.is-select-mode .selection,
  .entity-thumbnail.is-selected .selection,
  .selection:focus {
    opacity: 1;
    pointer-events: auto;
  }

  .selection::before {
    position: absolute;
    inset: 0.38rem;
    border: 1px solid rgb(244 239 230 / 0.7);
    background: rgb(0 0 0 / 0.16);
    content: "";
    pointer-events: none;
  }

  .selection::after {
    position: absolute;
    top: 0.58rem;
    left: 0.54rem;
    inline-size: 0.45rem;
    block-size: 0.24rem;
    border-bottom: 2px solid #0b0b0c;
    border-left: 2px solid #0b0b0c;
    content: "";
    opacity: 0;
    transform: rotate(-45deg);
  }

  .selection:checked,
  .selection.is-selected {
    border-color: rgb(242 194 106 / 0.74);
    box-shadow: 0 0 16px rgb(242 194 106 / 0.22);
  }

  .selection:checked::before,
  .selection.is-selected::before {
    border-color: rgb(242 194 106 / 0.95);
    background: linear-gradient(135deg, #f2c26a, #b8862e);
  }

  .selection:checked::after,
  .selection.is-selected::after {
    opacity: 1;
  }

  .copy {
    display: flex;
    flex-direction: column;
    min-width: 0;
    max-width: 100%;
  }

  .subtitle {
    overflow: hidden;
    margin: 0.1rem 0 0;
    color: rgb(196 201 212 / 0.82);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
    text-shadow: 0 1px 3px rgb(0 0 0 / 0.6);
  }

  .custom-subtitle {
    display: flex;
    min-width: 0;
    margin-top: 0.22rem;
  }

  .custom-subtitle.title-align-left {
    justify-content: flex-start;
  }

  .custom-subtitle.title-align-center {
    justify-content: center;
  }

  .custom-subtitle.title-align-right {
    justify-content: flex-end;
  }

  .entity-thumbnail.is-list .glass-info {
    flex: 1 1 0;
    height: auto;
    min-width: 0;
    min-height: auto;
    justify-content: center;
    min-block-size: 5.25rem;
    margin-top: 0;
    padding: 0.72rem 0.9rem;
    border-radius: 0;
    background:
      linear-gradient(180deg, rgb(10 12 15 / 0.94), rgb(9 10 12 / 0.98)),
      #0a0b0d;
    border: none;
    box-shadow: none;
  }

  .entity-thumbnail.is-list .selection {
    opacity: 1;
    pointer-events: auto;
  }

  .entity-thumbnail.is-list .badges {
    right: 0.38rem;
    left: 2.2rem;
  }

  h3 {
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    margin: 0;
    min-width: 0;
    overflow: hidden;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.82rem;
    font-weight: 600;
    line-height: 1.3;
    letter-spacing: -0.01em;
    white-space: normal;
    text-overflow: ellipsis;
    color: rgb(244 239 230 / 0.95);
  }

  .title-size-compact {
    font-size: 0.72rem;
    font-weight: 600;
    line-height: 1.2;
  }

  .title-align-left {
    text-align: left;
  }

  .title-align-center {
    text-align: center;
  }

  .title-align-right {
    text-align: right;
  }

  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
    margin-top: 0.1rem;
    max-block-size: 1.4rem;
    overflow: hidden;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.2rem;
    min-width: 0;
    max-width: 100%;
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-xs, 4px);
    background: rgb(255 255 255 / 0.06);
    color: rgb(244 239 230 / 0.72);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    line-height: 1;
    min-height: 1.1rem;
    padding: 0.12rem 0.28rem;
    text-shadow: 0 1px 2px rgb(0 0 0 / 0.5);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .chip :global(svg) {
    flex: 0 0 auto;
    color: rgb(242 194 106 / 0.82);
  }

  .chip-accent {
    border-color: rgb(242 194 106 / 0.38);
    background: rgb(13 13 14 / 0.78);
    color: rgb(244 239 230 / 0.92);
    box-shadow: 0 0 8px rgb(242 194 106 / 0.08);
  }

  .chip-rating {
    border-color: rgb(242 193 95 / 0.5);
    background: linear-gradient(135deg, rgb(50 38 14 / 0.85), rgb(32 25 13 / 0.85));
    color: #f2c15f;
    box-shadow: 0 0 10px rgb(242 193 95 / 0.18), inset 0 1px 0 rgb(255 255 255 / 0.06);
    font-weight: 600;
  }

  .chip-rating :global(svg) {
    color: #f2c15f;
    filter: drop-shadow(0 0 3px rgb(242 193 95 / 0.4));
  }

  @container (max-width: 220px) {
    .glass-info {
      padding: 0.35rem 0.45rem;
      gap: 0.15rem;
    }

    h3 {
      font-size: 0.72rem;
    }

    .chips {
      gap: 0.15rem;
      max-block-size: 1.15rem;
    }

    .chip {
      font-size: 0.52rem;
      min-height: 0.9rem;
      padding: 0.08rem 0.2rem;
    }

    .subtitle {
      display: none;
    }
  }

  @container (max-width: 140px) {
    .glass-info {
      padding: 0.25rem 0.35rem;
      gap: 0.1rem;
    }

    h3 {
      -webkit-line-clamp: 1;
      line-clamp: 1;
      font-size: 0.62rem;
    }

    .subtitle,
    .chips {
      display: none;
    }
  }

  @media (max-width: 640px) {
    .badge {
      font-size: 0.61rem;
    }
  }

  /* Small thumbnails (list rows, dense rails): overlays are absolutely sized, so scale the badges,
     placeholder icons, and progress meter down with the artwork and pull the badge cluster back to the
     edge — at these widths there is no selection checkbox occupying the top-left inset. Kept last so it
     wins over the mobile @media badge sizing above for genuinely small thumbnails. */
  @container (max-width: 112px) {
    .badges {
      gap: 0.18rem;
    }

    .top-badges {
      top: 0.3rem;
      left: 0.3rem;
      right: 0.3rem;
    }

    .bottom-left-badges {
      top: 0.3rem;
      left: 0.3rem;
    }

    .bottom-right-badges {
      bottom: 0.3rem;
      right: 0.3rem;
    }

    .badge {
      font-size: 0.5rem;
      min-height: 0;
      line-height: 1;
      gap: 0.15rem;
      padding: 0.15rem 0.26rem;
    }

    .wanted-badge {
      gap: 0;
      padding: 0.2rem;
    }

    /* Icon-only on tiny artwork — the status still reads by icon + colour without crowding the poster. */
    .wanted-label {
      display: none;
    }

    .rating-badge {
      display: none;
    }

    .icon-only {
      inline-size: 1.05rem;
    }

    .placeholder :global(.placeholder-icon) {
      width: 1.35rem;
      height: 1.35rem;
    }

    .placeholder-frame {
      width: 2rem;
      height: 2rem;
    }

    .placeholder :global(.placeholder-icon-framed) {
      width: 1.05rem;
      height: 1.05rem;
    }

    .placeholder :global(.placeholder-disc) {
      width: 2.1rem;
      height: 2.1rem;
    }

    .placeholder :global(.placeholder-note) {
      width: 0.95rem;
      height: 0.95rem;
    }

    .progress-meter {
      height: 2px;
    }
  }
</style>
