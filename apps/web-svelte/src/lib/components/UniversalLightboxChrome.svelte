<script lang="ts">
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
  import { Button, buttonVariants, cn } from "@prismedia/ui-svelte";

  interface Props {
    section: "top" | "bottom";
    title?: string | null;
    counterText: string;
    showRatingControls?: boolean;
    currentRating?: number | null;
    canOpenDetails?: boolean;
    infoOpen?: boolean;
    hasVideoPlayback?: boolean;
    videoMuted?: boolean;
    downloadHref?: string;
    downloadName?: string;
    onClose?: () => void;
    onRate?: (rating: number) => void;
    onToggleInfo?: () => void;
    onToggleVideoMute?: () => void;
    onPrevious?: () => void;
    onNext?: () => void;
    onZoomOut?: () => void;
    onZoomIn?: () => void;
    onResetZoom?: () => void;
  }

  let {
    section,
    title,
    counterText,
    showRatingControls = false,
    currentRating = null,
    canOpenDetails = false,
    infoOpen = false,
    hasVideoPlayback = false,
    videoMuted = true,
    downloadHref,
    downloadName = "media",
    onClose = () => {},
    onRate = () => {},
    onToggleInfo = () => {},
    onToggleVideoMute = () => {},
    onPrevious = () => {},
    onNext = () => {},
    onZoomOut = () => {},
    onZoomIn = () => {},
    onResetZoom = () => {},
  }: Props = $props();
</script>

{#if section === "top"}
  <div class="top-bar">
  <Button variant="ghost" size="icon" onclick={onClose} class="lightbox-button" aria-label="Close" title="Close (Esc)">
    <X class="h-5 w-5" />
  </Button>
  <div class="title-block">
    {#if title}
      <h2>{title}</h2>
    {/if}
    <div class="counter">{counterText}</div>
  </div>
  {#if showRatingControls}
    <div class="rating-buttons">
      {#each [1, 2, 3, 4, 5] as rating (rating)}
        <Button variant="ghost" size="icon" onclick={() => onRate(rating)} class="lightbox-button" aria-label={`Rate ${rating}`} title={`${rating} stars`}>
          <Star class={cn("h-4 w-4", (currentRating ?? 0) >= rating && "is-filled")} />
        </Button>
      {/each}
    </div>
  {/if}
  {#if canOpenDetails}
    <Button
      variant="ghost"
      size="icon"
      onclick={onToggleInfo}
      class={cn("lightbox-button", infoOpen && "is-active")}
      aria-label="Details"
      title="Details (I)"
    >
      <Info class="h-4 w-4" />
    </Button>
  {/if}
  {#if hasVideoPlayback}
    <Button
      variant="ghost"
      size="icon"
      onclick={onToggleVideoMute}
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
    <a href={downloadHref} download={downloadName} class={cn(buttonVariants({ variant: "ghost", size: "icon" }), "lightbox-button")} aria-label="Download" title="Download">
      <Download class="h-4 w-4" />
    </a>
  {/if}
  </div>
{:else}
  <div class="bottom-bar">
    <div class="bottom-controls">
      <Button variant="ghost" size="icon" onclick={onPrevious} class="lightbox-button" aria-label="Previous">
        <ChevronLeft class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={onNext} class="lightbox-button" aria-label="Next">
        <ChevronRight class="h-4 w-4" />
      </Button>
    </div>
    <div class="counter">{counterText}</div>
    <div class="bottom-controls">
      <Button variant="ghost" size="icon" onclick={onZoomOut} class="lightbox-button" aria-label="Zoom out">
        <ZoomOut class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={onZoomIn} class="lightbox-button" aria-label="Zoom in">
        <ZoomIn class="h-4 w-4" />
      </Button>
      <Button variant="ghost" size="icon" onclick={onResetZoom} class="lightbox-button" aria-label="Reset zoom">
        <RotateCcw class="h-4 w-4" />
      </Button>
    </div>
  </div>
{/if}

<style>
  .top-bar,
  .bottom-bar {
    position: relative;
    z-index: 20;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    border-color: var(--color-border-subtle, #1c2235);
    background: rgb(0 0 0 / 0.72);
    padding: 0.5rem 0.75rem;
    backdrop-filter: blur(var(--glass-blur-md));
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
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
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
    border-color: rgb(199 201 204 / 0.45);
    color: var(--color-text-accent, #c7c9cc);
    box-shadow: 0 0 18px rgb(199 201 204 / 0.22);
  }

  :global(.is-filled) {
    fill: currentColor;
    color: var(--color-text-accent, #c7c9cc);
  }

  @media (max-width: 640px) {
    .rating-buttons {
      display: none;
    }

    .bottom-bar {
      padding-bottom: calc(0.5rem + env(safe-area-inset-bottom, 0px));
    }
  }
</style>
