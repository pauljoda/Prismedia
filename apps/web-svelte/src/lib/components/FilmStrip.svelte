<script lang="ts">
  import { onMount } from "svelte";
  import { ChevronLeft, ChevronRight } from "@lucide/svelte";
  import {
    loadTrickplayFrames,
    findFrameAtTime,
    timeToTrackPosition,
    type TrickplayFrame,
  } from "@prismedia/ui-svelte";

  interface FilmStripMarker {
    id: string;
    time: number;
    title: string;
    tag?: string;
  }

  interface Props {
    playlistUrl: string;
    videoEl: HTMLVideoElement | null | undefined;
    currentTime?: number;
    duration: number;
    onSeek: (time: number) => void;
    markers?: FilmStripMarker[];
    onStripInteractionChange?: (active: boolean) => void;
  }

  let {
    playlistUrl,
    videoEl,
    currentTime,
    duration,
    onSeek,
    markers = [],
    onStripInteractionChange,
  }: Props = $props();

  const STRIP_HEIGHT = 52;
  const DESKTOP_WHEEL_SCRUB_MQ = "(pointer: fine) and (hover: hover)";

  let containerEl: HTMLDivElement | undefined = $state();
  let trackEl: HTMLDivElement | undefined = $state();
  let markersEl: HTMLDivElement | undefined = $state();
  let frames = $state<TrickplayFrame[] | null>(null);
  let error = $state(false);
  let dragging = $state(false);
  let previewTime = $state<number | null>(null);
  let dragStartX = 0;
  let dragStartTime = 0;
  let dragTargetTime = 0;
  let rafId = 0;
  let wheelIdleTimer: number | null = null;

  const frameWidth = $derived(
    frames && frames.length > 0
      ? Math.round((frames[0].width / frames[0].height) * STRIP_HEIGHT)
      : Math.round((16 / 9) * STRIP_HEIGHT),
  );
  const spriteWidth = $derived(
    frames ? frames.reduce((m, f) => Math.max(m, f.x + f.width), 0) : 0,
  );
  const spriteHeight = $derived(
    frames ? frames.reduce((m, f) => Math.max(m, f.y + f.height), 0) : 0,
  );
  const trackWidth = $derived(frames ? frames.length * frameWidth : 0);

  function clearWheelIdleTimer() {
    if (wheelIdleTimer != null) {
      window.clearTimeout(wheelIdleTimer);
      wheelIdleTimer = null;
    }
  }

  function scheduleWheelScrubCommit(time: number) {
    onStripInteractionChange?.(true);
    clearWheelIdleTimer();
    previewTime = time;
    wheelIdleTimer = window.setTimeout(() => {
      wheelIdleTimer = null;
      const commitTime = previewTime;
      previewTime = null;
      if (commitTime !== null) {
        onSeek(commitTime);
      }
      onStripInteractionChange?.(false);
    }, 320);
  }

  function applyPosition(time: number) {
    if (!containerEl || !trackEl || !frames) return;
    const containerWidth = containerEl.clientWidth;
    const trackPosition = timeToTrackPosition(frames, time, frameWidth);
    const tx = containerWidth / 2 - trackPosition;
    const transform = `translateX(${tx}px)`;
    trackEl.style.transform = transform;
    if (markersEl) markersEl.style.transform = transform;
  }

  $effect(() => {
    loadTrickplayFrames(playlistUrl)
      .then((f) => (frames = f))
      .catch(() => (error = true));
  });

  $effect(() => {
    if (!frames || frames.length === 0) return;
    const tick = () => {
      if (!containerEl || !trackEl || dragging) {
        rafId = requestAnimationFrame(tick);
        return;
      }
      applyPosition(previewTime ?? currentTime ?? videoEl?.currentTime ?? 0);
      rafId = requestAnimationFrame(tick);
    };
    rafId = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafId);
  });

  $effect(() => {
    if (!frames || frames.length === 0 || duration <= 0 || trackWidth <= 0) return;
    const el = containerEl;
    if (!el) return;

    const mq = window.matchMedia(DESKTOP_WHEEL_SCRUB_MQ);

    const onWheel = (e: WheelEvent) => {
      if (!mq.matches) return;
      if (Math.abs(e.deltaX) <= Math.abs(e.deltaY)) return;
      const raw = e.deltaX;
      e.preventDefault();
      e.stopPropagation();
      const pixelsPerSecond = trackWidth / duration;
      const timeDelta = raw / pixelsPerSecond;
      const current = previewTime ?? currentTime ?? videoEl?.currentTime ?? 0;
      const newTime = Math.max(0, Math.min(duration, current + timeDelta));
      applyPosition(newTime);
      scheduleWheelScrubCommit(newTime);
    };

    const syncListener = () => {
      el.removeEventListener("wheel", onWheel);
      if (mq.matches) el.addEventListener("wheel", onWheel, { passive: false });
    };

    syncListener();
    mq.addEventListener("change", syncListener);
    return () => {
      mq.removeEventListener("change", syncListener);
      el.removeEventListener("wheel", onWheel);
    };
  });

  onMount(() => {
    return () => {
      clearWheelIdleTimer();
      onStripInteractionChange?.(false);
    };
  });

  function handlePointerDown(e: PointerEvent) {
    if (!frames || duration <= 0 || trackWidth <= 0) return;
    clearWheelIdleTimer();
    onStripInteractionChange?.(true);
    dragging = true;
    dragStartX = e.clientX;
    dragStartTime = currentTime ?? videoEl?.currentTime ?? 0;
    dragTargetTime = dragStartTime;
    previewTime = dragStartTime;
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
  }

  function handlePointerMove(e: PointerEvent) {
    if (!dragging || !frames || duration <= 0 || trackWidth <= 0) return;
    const dx = e.clientX - dragStartX;
    const pixelsPerSecond = trackWidth / duration;
    const timeDelta = -dx / pixelsPerSecond;
    const newTime = Math.max(0, Math.min(duration, dragStartTime + timeDelta));
    dragTargetTime = newTime;
    previewTime = newTime;
    applyPosition(newTime);
  }

  function endPointer(commit = true) {
    if (dragging && commit) {
      onSeek(dragTargetTime);
    }
    previewTime = null;
    dragging = false;
    clearWheelIdleTimer();
    onStripInteractionChange?.(false);
  }

  function jumpFrame(direction: -1 | 1) {
    if (!frames || frames.length === 0) return;
    const time = currentTime ?? videoEl?.currentTime ?? 0;
    const currentIndex = findFrameAtTime(frames, time);
    const nextIndex = Math.max(0, Math.min(frames.length - 1, currentIndex + direction));
    onSeek(frames[nextIndex].start);
  }

  function handleMarkerPointerDown(e: PointerEvent) {
    e.stopPropagation();
  }

  function handleMarkerClick(e: MouseEvent, marker: FilmStripMarker) {
    e.stopPropagation();
    onSeek(marker.time);
  }
</script>

{#if frames && frames.length > 0 && !error}
  <div class="relative flex items-center" style:height="{STRIP_HEIGHT}px">
    <button
      type="button"
      onclick={() => jumpFrame(-1)}
      class="relative z-30 flex h-full w-8 flex-shrink-0 items-center justify-center bg-black/80 text-white/50 transition-colors hover:text-white"
      aria-label="Previous frame"
    >
      <ChevronLeft class="h-4 w-4" />
    </button>

    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      bind:this={containerEl}
      class="relative flex-1 overflow-hidden select-none touch-pan-y"
      style:height="{STRIP_HEIGHT}px"
      onpointerdown={handlePointerDown}
      onpointermove={handlePointerMove}
      onpointerup={() => endPointer(true)}
      onpointercancel={() => endPointer(false)}
    >
      <div class="pointer-events-none absolute inset-y-0 left-0 z-10 w-16 bg-gradient-to-r from-black/60 to-transparent"></div>
      <div class="pointer-events-none absolute inset-y-0 right-0 z-10 w-16 bg-gradient-to-l from-black/60 to-transparent"></div>

      <div class="pointer-events-none absolute inset-y-0 left-1/2 z-20 -translate-x-1/2">
        <div class="absolute -top-px left-1/2 -translate-x-1/2 border-x-[5px] border-t-[5px] border-x-transparent border-t-accent-500"></div>
        <div class="h-full w-[2px] bg-accent-500 shadow-[0_0_8px_rgba(var(--accent-500-rgb,59,130,246),0.6)]"></div>
        <div class="absolute -bottom-px left-1/2 -translate-x-1/2 border-x-[5px] border-b-[5px] border-x-transparent border-b-accent-500"></div>
      </div>

      <div
        bind:this={trackEl}
        class="absolute inset-y-0 flex will-change-transform"
        style:width="{trackWidth}px"
        style:cursor={dragging ? "grabbing" : "grab"}
      >
        {#each frames as frame, i (i)}
          <div class="flex-shrink-0" style:width="{frameWidth}px" style:height="{STRIP_HEIGHT}px">
            <div
              class="h-full w-full"
              style:background-image="url({frame.url})"
              style:background-size="{(spriteWidth / frame.width) * frameWidth}px {(spriteHeight / frame.height) * STRIP_HEIGHT}px"
              style:background-position="-{(frame.x / frame.width) * frameWidth}px -{(frame.y / frame.height) * STRIP_HEIGHT}px"
              style:background-repeat="no-repeat"
            ></div>
          </div>
        {/each}
      </div>

      {#if duration > 0 && markers.length > 0}
        <div
          bind:this={markersEl}
          class="absolute inset-y-0 pointer-events-none will-change-transform"
          style:width="{trackWidth}px"
        >
          {#each markers as marker (marker.id)}
            {@const left = timeToTrackPosition(frames, marker.time, frameWidth)}
            <button
              type="button"
              class="absolute top-0 bottom-0 flex -translate-x-1/2 flex-col items-center pointer-events-auto group/marker"
              style:left="{left}px"
              aria-label={`Seek to ${marker.title}`}
              title={`Seek to ${marker.title}`}
              onpointerdown={handleMarkerPointerDown}
              onclick={(e) => handleMarkerClick(e, marker)}
            >
              <div class="w-px flex-1 bg-accent-500/50"></div>
              <div class="mb-0.5 whitespace-nowrap rounded-md px-1.5 py-px text-[0.5rem] font-medium tracking-wide uppercase leading-tight bg-black/90 text-accent-300 border border-accent-500/30 transition-colors group-hover/marker:border-accent-400/70 group-hover/marker:text-accent-100 group-focus-visible/marker:border-accent-400/70 group-focus-visible/marker:text-accent-100">
                {marker.title}
              </div>
            </button>
          {/each}
        </div>
      {/if}
    </div>

    <button
      type="button"
      onclick={() => jumpFrame(1)}
      class="relative z-30 flex h-full w-8 flex-shrink-0 items-center justify-center bg-black/80 text-white/50 transition-colors hover:text-white"
      aria-label="Next frame"
    >
      <ChevronRight class="h-4 w-4" />
    </button>
  </div>
{/if}
