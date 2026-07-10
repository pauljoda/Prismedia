<script module lang="ts">
  export interface VideoTimelineHover {
    chapterTitle: string | null;
    markerTitles: string[];
    percent: number;
    time: number;
  }
</script>

<script lang="ts">
  import { cn, type TrickplayFrame } from "@prismedia/ui-svelte";
  import { formatTime } from "./video-player-format";

  interface Props {
    bufferedProgressPercent: number;
    fullChrome: boolean;
    markersCount: number;
    onHover: (clientX: number, rect: DOMRect) => void;
    onHoverEnd: () => void;
    playbackProgressPercent: number;
    showControls: boolean;
    timelineHover: VideoTimelineHover | null;
    timelinePreviewFrame: TrickplayFrame | null;
    timelinePreviewSpriteDims: { width: number; height: number };
  }

  let {
    bufferedProgressPercent,
    fullChrome,
    markersCount,
    onHover,
    onHoverEnd,
    playbackProgressPercent,
    showControls,
    timelineHover,
    timelinePreviewFrame,
    timelinePreviewSpriteDims,
  }: Props = $props();
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<media-time-slider
  class={cn(
    "video-time-slider mobile-video-progress group/track",
    !fullChrome && "is-minimal-progress",
    !fullChrome || showControls ? "opacity-100" : "opacity-0",
    markersCount === 0 && "no-markers",
  )}
  style:--prismedia-slider-fill={`${playbackProgressPercent}%`}
  style:--prismedia-buffer-progress={`${bufferedProgressPercent}%`}
  data-testid="video-progress-track"
  aria-label="Seek"
  onpointerdown={(event) => event.stopPropagation()}
  onpointermove={(event) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onHover(event.clientX, rect);
  }}
  onpointerleave={onHoverEnd}
>
  {#if fullChrome && timelineHover}
    <div
      class="pointer-events-none absolute bottom-[calc(100%+0.6rem)] z-20 w-[min(11rem,54vw)] border border-border-default bg-surface-2/95 p-1.5 text-center shadow-[0_2px_12px_rgba(0,0,0,0.35)] rounded-[10px] overflow-hidden"
      style:left="clamp(0%, {timelineHover.percent}%, 100%)"
      style:transform="translateX(clamp(-95%, calc(-1 * {timelineHover.percent}%), -5%))"
    >
      {#if timelinePreviewFrame && timelinePreviewSpriteDims.width > 0 && timelinePreviewSpriteDims.height > 0}
        <div
          class="timeline-trickplay-preview"
          data-testid="timeline-trickplay-preview"
          style:aspect-ratio="{timelinePreviewFrame.width} / {timelinePreviewFrame.height}"
          style:background-image="url({timelinePreviewFrame.url})"
          style:background-size="{(timelinePreviewSpriteDims.width / timelinePreviewFrame.width) * 100}% {(timelinePreviewSpriteDims.height / timelinePreviewFrame.height) * 100}%"
          style:background-position="{timelinePreviewSpriteDims.width <= timelinePreviewFrame.width
            ? 0
            : (timelinePreviewFrame.x / (timelinePreviewSpriteDims.width - timelinePreviewFrame.width)) * 100}% {timelinePreviewSpriteDims.height <= timelinePreviewFrame.height
            ? 0
            : (timelinePreviewFrame.y / (timelinePreviewSpriteDims.height - timelinePreviewFrame.height)) * 100}%"
          style:background-repeat="no-repeat"
        ></div>
      {/if}
      <div class="text-mono-tabular text-[0.65rem] text-white/82">
        {formatTime(timelineHover.time)}
      </div>
      {#if timelineHover.chapterTitle}
        <div class="mt-1 max-w-48 text-[0.65rem] font-medium leading-snug text-accent-100">
          {timelineHover.chapterTitle}
        </div>
      {:else if timelineHover.markerTitles.length > 0}
        <div class="mt-1 max-w-48 text-[0.65rem] font-medium leading-snug text-accent-100">
          {timelineHover.markerTitles.join(" • ")}
        </div>
      {/if}
    </div>
  {/if}
  {#if fullChrome}
    <div class="video-slider-native-progress is-buffered"></div>
  {/if}
  <div class="video-slider-native-progress is-played"></div>
  {#if fullChrome}
    <media-slider-chapters>
      <template>
        <div class="video-slider-chapter">
          <div class="video-slider-track"></div>
          <div class="video-slider-track-progress"></div>
          <div class="video-slider-track-fill"></div>
        </div>
      </template>
    </media-slider-chapters>
    <media-slider-preview class="video-slider-preview">
      <span data-part="chapter-title" class="video-slider-chapter-title"></span>
      <media-slider-value type="pointer" class="video-slider-time"></media-slider-value>
    </media-slider-preview>
    <div class="video-slider-thumb"></div>
  {/if}
</media-time-slider>

<style>
  .mobile-video-progress {
    height: 3px;
  }

  .mobile-video-progress:hover {
    height: 6px;
  }

  .video-time-slider {
    /* Authoritative played gradient for OUR overlay divs (`is-played`, chapter fill). */
    --prismedia-fill-gradient: linear-gradient(90deg, var(--color-accent-500), var(--color-accent-300));
    --media-slider-track-bg: rgba(255, 255, 255, 0.18);
    /*
     * VidStack's own track-fill is positioned from the media element's raw duration. For a still-
     * growing on-demand HLS playlist (e.g. the SDR-direct adaptive path) that duration is only the
     * produced-so-far length, so VidStack paints its fill to ~100% and the brass overruns the real
     * playhead. We drive the visible fill ourselves from `playbackProgressPercent` (computed against
     * the authoritative max(video.duration, propDuration)), so keep VidStack's duration-driven fill
     * transparent and let `.is-played` be the single source of truth.
     */
    --media-slider-track-fill-bg: transparent;
    --media-slider-track-progress-bg: transparent;
    --media-slider-chapter-hover-transform: scaleY(2.2);
    bottom: 0.75rem;
    cursor: pointer;
    display: block;
    position: absolute;
    touch-action: none;
    transition: height var(--duration-fast) var(--ease-default);
    z-index: 45;
  }

  .video-time-slider:not(.is-minimal-progress) {
    left: 50%;
    right: auto;
    transform: translateX(-50%);
    width: calc(100cqw - var(--player-chrome-inline-padding) - var(--player-chrome-inline-padding));
  }

  .video-time-slider.is-minimal-progress {
    background: rgba(255, 255, 255, 0.18);
    bottom: 0;
    height: 4px;
    left: 0;
    overflow: hidden;
    right: 0;
    z-index: 55;
  }

  .video-time-slider.is-minimal-progress:hover {
    height: 6px;
  }

  .video-time-slider media-slider-chapters {
    align-items: center;
    display: flex;
    height: 100%;
    position: relative;
    width: 100%;
  }

  .video-slider-chapter {
    height: 100%;
    margin-right: 2px;
    min-width: 0.35rem;
    overflow: hidden;
    position: relative;
  }

  .video-slider-chapter:last-child {
    margin-right: 0;
  }

  .video-slider-track,
  .video-slider-track-progress,
  .video-slider-track-fill {
    height: 100%;
    left: 0;
    position: absolute;
    top: 0;
  }

  .video-slider-track {
    background: var(--media-slider-track-bg);
    width: 100%;
  }

  .video-slider-track-progress {
    background: var(--media-slider-track-progress-bg);
    width: var(--chapter-progress, 0%);
    z-index: 1;
  }

  .video-slider-track-fill {
    /*
     * Per-chapter fill is positioned by VidStack's `--chapter-fill`, which derives from the media
     * element's raw (possibly still-growing) duration and can overrun the real playhead. The visible
     * played fill is `.is-played` (driven by our authoritative duration), so this stays invisible —
     * the chapter elements remain only for hover/scrub geometry.
     */
    background: transparent;
    width: var(--chapter-fill, 0%);
    z-index: 2;
  }

  .video-slider-native-progress {
    height: 100%;
    left: 0;
    pointer-events: none;
    position: absolute;
    top: 0;
  }

  .video-slider-native-progress.is-buffered {
    background: rgba(255, 255, 255, 0.22);
    width: var(--prismedia-buffer-progress, 0%);
    z-index: 2;
  }

  .video-slider-native-progress.is-played {
    background: var(--prismedia-fill-gradient);
    box-shadow: 0 0 12px rgba(196, 154, 90, 0.40);
    width: var(--prismedia-slider-fill, 0%);
    z-index: 3;
  }

  .video-slider-thumb {
    background: var(--color-accent-400);
    border-radius: 50%;
    box-shadow:
      0 0 0 1px rgba(196, 154, 90, 0.35),
      0 0 14px rgba(196, 154, 90, 0.55);
    height: 0.75rem;
    left: var(--prismedia-slider-fill, var(--slider-fill, 0%));
    pointer-events: none;
    position: absolute;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 0.75rem;
    z-index: 4;
  }

  .video-slider-preview {
    display: none;
  }

  .video-slider-chapter-title,
  .video-slider-time {
    display: none;
  }

  .timeline-trickplay-preview {
    background-color: rgba(255, 255, 255, 0.06);
    border: 1px solid var(--color-border-default, rgba(148, 158, 178, 0.13));
    border-radius: var(--radius-sm);
    margin-bottom: 0.4rem;
    overflow: hidden;
    width: 100%;
  }

  @media (min-width: 640px) {
    .video-time-slider {
      bottom: 7rem;
    }

    .video-time-slider.no-markers {
      bottom: 5.25rem;
    }

    .video-time-slider.is-minimal-progress,
    .video-time-slider.is-minimal-progress.no-markers {
      bottom: 0;
    }

    .mobile-video-progress {
      height: 4px;
    }

    .mobile-video-progress:hover {
      height: 8px;
    }
  }
</style>
