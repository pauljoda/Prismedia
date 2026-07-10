<script lang="ts">
  import { onMount } from "svelte";
  import { ChevronLeft, ChevronRight } from "@lucide/svelte";
  import { elementSize } from "$lib/hooks/element-size.svelte";

  interface Props {
    peaks: number[];
    duration: number;
    audioEl: HTMLAudioElement | null;
    onSeek: (time: number) => void;
  }

  const STRIP_HEIGHT = 52;
  const DESKTOP_WHEEL_SCRUB_MQ = "(pointer: fine) and (hover: hover)";
  const BAR_COLOR = "rgba(255, 255, 255, 0.18)";
  const BAR_ACCENT = "rgba(196, 154, 90, 0.35)";

  let { peaks, duration, audioEl, onSeek }: Props = $props();

  let containerEl: HTMLDivElement | null = $state(null);
  let trackEl: HTMLDivElement | null = $state(null);
  let canvasEl: HTMLCanvasElement | null = $state(null);
  const containerSize = elementSize();
  let containerWidth = $derived(containerSize.width);
  let stripDragging = $state(false);

  let dragging = false;
  let dragStartX = 0;
  let dragStartTime = 0;
  let animationFrame = 0;
  let wheelIdleTimer: number | null = null;

  const safeDuration = $derived(duration > 0 ? duration : 0.001);
  const pairCount = $derived(Math.floor(peaks.length / 2));
  const naturalWidth = $derived(Math.max(1, pairCount * 2));
  const trackWidth = $derived(
    containerWidth > 0
      ? Math.max(naturalWidth, containerWidth * 6, safeDuration * 10)
      : 0,
  );

  function drawWaveformStrip(
    canvas: HTMLCanvasElement,
    waveform: number[],
    width: number,
    height: number,
  ) {
    const count = Math.floor(waveform.length / 2);
    if (count <= 0 || width <= 0) return;

    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.floor(width * dpr);
    canvas.height = Math.floor(height * dpr);
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    const barWidth = Math.max(1, width / count);
    const centerY = height / 2;
    const maxAmplitude = waveform.reduce((max, value) => Math.max(max, Math.abs(value)), 1);

    for (let i = 0; i < count; i += 1) {
      const min = waveform[i * 2]! / maxAmplitude;
      const max = waveform[i * 2 + 1]! / maxAmplitude;
      const x = (i / count) * width;
      const barTop = centerY - max * (height / 2) * 0.88;
      const barBottom = centerY - min * (height / 2) * 0.88;
      const barHeight = Math.max(1, barBottom - barTop);
      const intensity = (Math.abs(min) + Math.abs(max)) / 2;
      ctx.fillStyle = intensity > 0.35 ? BAR_ACCENT : BAR_COLOR;
      ctx.fillRect(x, barTop, Math.max(1, barWidth - 0.5), barHeight);
    }
  }

  function endPointer() {
    dragging = false;
    stripDragging = false;
  }

  function clearWheelIdleTimer() {
    if (wheelIdleTimer != null) {
      window.clearTimeout(wheelIdleTimer);
      wheelIdleTimer = null;
    }
  }

  function clampTime(time: number) {
    return Math.max(0, Math.min(safeDuration, time));
  }

  function applyPosition(time: number) {
    if (!containerEl || !trackEl || trackWidth <= 0) return;
    const clampedTime = clampTime(time);
    const trackPosition = (clampedTime / safeDuration) * trackWidth;
    const translateX = containerEl.clientWidth / 2 - trackPosition;
    trackEl.style.transform = `translateX(${translateX}px)`;
  }

  function jump(direction: -1 | 1) {
    const step = Math.max(0.5, safeDuration / 48);
    const audioTime = audioEl?.currentTime ?? 0;
    const nextTime = clampTime(audioTime + direction * step);
    applyPosition(nextTime);
    onSeek(nextTime);
  }

  onMount(() => {
    const tick = () => {
      // While the user is scrubbing — by pointer drag (`dragging`) or by wheel/trackpad
      // (`stripDragging`, set without `dragging`) — leave the playhead where the scrub put it.
      // Otherwise this loop would overwrite it each frame with the audio element's still-catching-up
      // currentTime, making the strip fight the scrub position and flicker.
      if (!dragging && !stripDragging && audioEl) {
        applyPosition(audioEl.currentTime);
      }
      animationFrame = window.requestAnimationFrame(tick);
    };
    animationFrame = window.requestAnimationFrame(tick);

    const mq = window.matchMedia(DESKTOP_WHEEL_SCRUB_MQ);
    const handleWheel = (event: WheelEvent) => {
      if (!mq.matches) return;
      if (Math.abs(event.deltaX) <= Math.abs(event.deltaY)) return;
      const rawDelta = event.deltaX;
      if (rawDelta === 0 || trackWidth <= 0) return;
      event.preventDefault();
      event.stopPropagation();
      const pixelsPerSecond = trackWidth / safeDuration;
      const timeDelta = rawDelta / pixelsPerSecond;
      const nextTime = clampTime((audioEl?.currentTime ?? 0) + timeDelta);
      applyPosition(nextTime);
      onSeek(nextTime);
      clearWheelIdleTimer();
      stripDragging = true;
      wheelIdleTimer = window.setTimeout(() => {
        wheelIdleTimer = null;
        stripDragging = false;
      }, 180);
    };

    const syncWheelListener = () => {
      containerEl?.removeEventListener("wheel", handleWheel);
      if (mq.matches) {
        containerEl?.addEventListener("wheel", handleWheel, { passive: false });
      }
    };

    syncWheelListener();
    mq.addEventListener("change", syncWheelListener);

    return () => {
      if (animationFrame) window.cancelAnimationFrame(animationFrame);
      mq.removeEventListener("change", syncWheelListener);
      containerEl?.removeEventListener("wheel", handleWheel);
      clearWheelIdleTimer();
    };
  });

  $effect(() => {
    if (!canvasEl || trackWidth <= 0 || pairCount <= 0) return;
    drawWaveformStrip(canvasEl, peaks, trackWidth, STRIP_HEIGHT);
    applyPosition(audioEl?.currentTime ?? 0);
  });
</script>

{#if pairCount > 0}
  <div class="relative flex items-center bg-black" style={`height: ${STRIP_HEIGHT}px`}>
    <button
      type="button"
      onclick={() => jump(-1)}
      class="relative z-30 flex h-full w-8 shrink-0 items-center justify-center bg-black/80 text-white/50 transition-colors hover:text-white"
      aria-label="Scrub back"
    >
      <ChevronLeft class="h-4 w-4" />
    </button>

    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      bind:this={containerEl}
      use:containerSize.attach
      class="relative flex-1 overflow-hidden select-none touch-pan-y"
      style={`height: ${STRIP_HEIGHT}px`}
      onpointerdown={(event) => {
        if (safeDuration <= 0 || trackWidth <= 0) return;
        clearWheelIdleTimer();
        dragging = true;
        stripDragging = true;
        dragStartX = event.clientX;
        dragStartTime = audioEl?.currentTime ?? 0;
        (event.currentTarget as HTMLDivElement).setPointerCapture(event.pointerId);
        applyPosition(dragStartTime);
      }}
      onpointermove={(event) => {
        if (!dragging || safeDuration <= 0 || trackWidth <= 0) return;
        const deltaX = event.clientX - dragStartX;
        const pixelsPerSecond = trackWidth / safeDuration;
        const nextTime = clampTime(dragStartTime - deltaX / pixelsPerSecond);
        applyPosition(nextTime);
        onSeek(nextTime);
      }}
      onpointerup={(event) => {
        (event.currentTarget as HTMLDivElement).releasePointerCapture(event.pointerId);
        endPointer();
      }}
      onpointercancel={endPointer}
    >
      <div class="pointer-events-none absolute inset-y-0 left-0 z-10 w-12 bg-gradient-to-r from-black/70 to-transparent"></div>
      <div class="pointer-events-none absolute inset-y-0 right-0 z-10 w-12 bg-gradient-to-l from-black/70 to-transparent"></div>

      <div class="pointer-events-none absolute inset-y-0 left-1/2 z-20 -translate-x-1/2">
        <div class="absolute -top-px left-1/2 -translate-x-1/2 border-x-[5px] border-t-[5px] border-x-transparent border-t-accent-500"></div>
        <div class="h-full w-[2px] bg-accent-500 shadow-[0_0_8px_rgba(196,154,90,0.55)]"></div>
        <div class="absolute -bottom-px left-1/2 -translate-x-1/2 border-x-[5px] border-b-[5px] border-x-transparent border-b-accent-500"></div>
      </div>

      {#if trackWidth > 0}
        <div
          bind:this={trackEl}
          class="absolute inset-y-0 left-0 will-change-transform"
          style={`width: ${trackWidth}px; cursor: ${stripDragging ? "grabbing" : "grab"};`}
        >
          <canvas bind:this={canvasEl} class="block h-full"></canvas>
        </div>
      {/if}
    </div>

    <button
      type="button"
      onclick={() => jump(1)}
      class="relative z-30 flex h-full w-8 shrink-0 items-center justify-center bg-black/80 text-white/50 transition-colors hover:text-white"
      aria-label="Scrub forward"
    >
      <ChevronRight class="h-4 w-4" />
    </button>
  </div>
{/if}
