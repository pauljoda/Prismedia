<script lang="ts">
  import { onMount } from "svelte";
  import { ChevronLeft, ChevronRight } from "@lucide/svelte";
  import { normalizeWaveformSample, waveformDisplayScale } from "./audio-waveform";

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
  let canvasEl: HTMLCanvasElement | null = $state(null);
  let containerWidth = $state(0);
  let stripDragging = $state(false);
  let currentTime = $state(0);

  let dragging = false;
  let animationFrame = 0;

  const safeDuration = $derived(duration > 0 ? duration : 0.001);
  const pairCount = $derived(Math.floor(peaks.length / 2));
  const progress = $derived(Math.max(0, Math.min(1, currentTime / safeDuration)));

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

    const columns = Math.max(1, Math.floor(width));
    const barWidth = Math.max(1, width / columns);
    const centerY = height / 2;
    const displayScale = waveformDisplayScale(waveform);

    for (let column = 0; column < columns; column += 1) {
      const startPair = Math.floor((column / columns) * count);
      const endPair = Math.max(startPair + 1, Math.ceil(((column + 1) / columns) * count));
      let rawMin = 0;
      let rawMax = 0;

      for (let pair = startPair; pair < endPair && pair < count; pair += 1) {
        rawMin = Math.min(rawMin, waveform[pair * 2] ?? 0);
        rawMax = Math.max(rawMax, waveform[pair * 2 + 1] ?? 0);
      }

      const min = normalizeWaveformSample(rawMin, displayScale);
      const max = normalizeWaveformSample(rawMax, displayScale);
      const x = (column / columns) * width;
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

  function jump(direction: -1 | 1) {
    const step = Math.max(0.5, safeDuration / 48);
    const audioTime = audioEl?.currentTime ?? 0;
    const nextTime = Math.max(0, Math.min(safeDuration, audioTime + direction * step));
    onSeek(nextTime);
  }

  onMount(() => {
    if (containerEl) {
      const width = containerEl.getBoundingClientRect().width;
      if (width > 0) containerWidth = width;
    }

    const resizeObserver =
      containerEl && typeof ResizeObserver !== "undefined"
        ? new ResizeObserver((entries) => {
            const entry = entries[0];
            if (entry) containerWidth = entry.contentRect.width;
          })
        : null;

    if (containerEl && resizeObserver) {
      resizeObserver.observe(containerEl);
    }

    const tick = () => {
      if (!dragging && audioEl) {
        currentTime = audioEl.currentTime;
      }
      animationFrame = window.requestAnimationFrame(tick);
    };
    animationFrame = window.requestAnimationFrame(tick);

    const mq = window.matchMedia(DESKTOP_WHEEL_SCRUB_MQ);
    const handleWheel = (event: WheelEvent) => {
      if (!mq.matches) return;
      const rawDelta =
        Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
      if (rawDelta === 0) return;
      event.preventDefault();
      event.stopPropagation();
      const timeDelta =
        (rawDelta / Math.max(1, containerEl?.clientWidth ?? 1)) * safeDuration;
      const nextTime = Math.max(
        0,
        Math.min(safeDuration, (audioEl?.currentTime ?? 0) + timeDelta),
      );
      currentTime = nextTime;
      onSeek(nextTime);
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
      resizeObserver?.disconnect();
      mq.removeEventListener("change", syncWheelListener);
      containerEl?.removeEventListener("wheel", handleWheel);
    };
  });

  $effect(() => {
    if (!canvasEl || containerWidth <= 0 || pairCount <= 0) return;
    drawWaveformStrip(canvasEl, peaks, containerWidth, STRIP_HEIGHT);
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
      class="relative flex-1 overflow-hidden select-none touch-none"
      style={`height: ${STRIP_HEIGHT}px`}
      onpointerdown={(event) => {
        if (safeDuration <= 0) return;
        dragging = true;
        stripDragging = true;
        (event.currentTarget as HTMLDivElement).setPointerCapture(event.pointerId);
        const rect = (event.currentTarget as HTMLDivElement).getBoundingClientRect();
        const nextTime =
          Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)) * safeDuration;
        currentTime = nextTime;
        onSeek(nextTime);
      }}
      onpointermove={(event) => {
        if (!dragging || safeDuration <= 0) return;
        const rect = (event.currentTarget as HTMLDivElement).getBoundingClientRect();
        const nextTime =
          Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)) * safeDuration;
        currentTime = nextTime;
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

      {#if containerWidth > 0}
        <div
          class="absolute inset-y-0 left-0"
          style={`width: ${containerWidth}px; cursor: ${stripDragging ? "grabbing" : "grab"};`}
        >
          <canvas bind:this={canvasEl} class="block h-full"></canvas>
        </div>
      {/if}

      <div
        class="pointer-events-none absolute inset-y-0 left-0 z-10 bg-accent-500/12"
        style={`width: ${progress * 100}%`}
      ></div>

      <div
        class="pointer-events-none absolute inset-y-0 z-20 -translate-x-1/2"
        style={`left: ${progress * 100}%`}
      >
        <div class="absolute -top-px left-1/2 -translate-x-1/2 border-x-[5px] border-t-[5px] border-x-transparent border-t-accent-500"></div>
        <div class="h-full w-[2px] bg-accent-500 shadow-[0_0_8px_rgba(196,154,90,0.55)]"></div>
        <div class="absolute -bottom-px left-1/2 -translate-x-1/2 border-x-[5px] border-b-[5px] border-x-transparent border-b-accent-500"></div>
      </div>
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
