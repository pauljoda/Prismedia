<script lang="ts">
  import { Gauge, Settings2, Wifi } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    activePlaybackDetailLabel?: string | null;
    activePlaybackLabel: string;
    bandwidthLabel: string;
    bufferAhead: number;
    droppedFrames?: number | null;
    mode: "direct" | "hls";
    playerNotice?: string | null;
    qualityDetailLabel?: string | null;
    qualityLabel: string;
    showControls: boolean;
  }

  let {
    activePlaybackDetailLabel = null,
    activePlaybackLabel,
    bandwidthLabel,
    bufferAhead,
    droppedFrames = null,
    mode,
    playerNotice = null,
    qualityDetailLabel = null,
    qualityLabel,
    showControls,
  }: Props = $props();
</script>

<div
  class={cn(
    "player-top-bar pointer-events-none absolute inset-x-0 top-0 z-20 flex items-start justify-between gap-2 px-3 sm:px-4 pb-8 sm:pb-12 pt-3 sm:pt-4 transition-opacity duration-normal",
    showControls ? "opacity-100" : "opacity-0",
  )}
>
  <div class="flex flex-wrap gap-1.5 sm:gap-2">
    <span class="pointer-events-auto player-mode-chip flex max-w-[14rem] flex-col px-2 py-0.5 text-[0.55rem] font-semibold uppercase tracking-[0.18em] text-accent-100 sm:px-2.5 sm:py-1 sm:text-[0.62rem]">
      <span class="truncate">{activePlaybackLabel}</span>
      {#if activePlaybackDetailLabel}
        <span class="truncate text-[0.5rem] tracking-[0.12em] text-white/50 sm:text-[0.56rem]">
          {activePlaybackDetailLabel}
        </span>
      {/if}
    </span>
    {#if mode !== "direct"}
      <span
        data-testid="playback-quality-chip"
        title={qualityDetailLabel ?? qualityLabel}
        class="pointer-events-auto player-chip flex max-w-[14rem] flex-col border-white/10 px-2 py-0.5 text-[0.6rem] text-white/80 sm:px-2.5 sm:py-1 sm:text-[0.7rem]"
      >
        <span class="truncate">{qualityLabel}</span>
        {#if qualityDetailLabel}
          <span class="truncate text-[0.52rem] uppercase tracking-[0.12em] text-white/45 sm:text-[0.58rem]">
            {qualityDetailLabel}
          </span>
        {/if}
      </span>
    {/if}
    {#if playerNotice}
      <span class="player-chip border-warning/20 px-2 sm:px-2.5 py-0.5 sm:py-1 text-[0.6rem] sm:text-[0.7rem] text-white/80">
        {playerNotice}
      </span>
    {/if}
  </div>

  <div
    class={cn(
      "hidden sm:grid gap-2 text-right text-[0.68rem] text-white/70",
      mode === "direct" ? "min-w-[120px] grid-cols-2" : "min-w-[184px] grid-cols-3",
    )}
  >
    {#if mode !== "direct"}
      <div class="player-instrument-chip px-2 py-1.5">
        <div class="mb-0.5 flex items-center justify-end gap-1 text-white/40">
          <Wifi class="h-3.5 w-3.5" />
          <span class="text-[0.58rem] uppercase tracking-[0.16em]">ABR</span>
        </div>
        <div class="truncate text-mono-tabular text-glow-phosphor text-[0.72rem] font-medium">
          {bandwidthLabel}
        </div>
      </div>
    {/if}
    <div class="player-instrument-chip px-2 py-1.5">
      <div class="mb-0.5 flex items-center justify-end gap-1 text-white/40">
        <Gauge class="h-3.5 w-3.5" />
        <span class="text-[0.58rem] uppercase tracking-[0.16em]">Buffer</span>
      </div>
      <div class="truncate text-mono-tabular text-glow-phosphor text-[0.72rem] font-medium">
        {bufferAhead.toFixed(1)}s
      </div>
    </div>
    <div class="player-instrument-chip px-2 py-1.5">
      <div class="mb-0.5 flex items-center justify-end gap-1 text-white/40">
        <Settings2 class="h-3.5 w-3.5" />
        <span class="text-[0.58rem] uppercase tracking-[0.16em]">Drop</span>
      </div>
      <div class="truncate text-mono-tabular text-glow-phosphor text-[0.72rem] font-medium">
        {droppedFrames == null ? "-" : String(droppedFrames)}
      </div>
    </div>
  </div>
</div>

<style>
  .player-top-bar {
    background:
      linear-gradient(
        to bottom,
        rgba(7, 8, 11, 0.88) 0%,
        rgba(7, 8, 11, 0.50) 50%,
        transparent 100%
      );
  }

  .player-mode-chip {
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    background: rgba(36, 30, 22, 0.75);
    border: 1px solid rgba(196, 154, 90, 0.30);
    border-radius: var(--radius-base);
    box-shadow:
      inset 0 1px 0 rgba(196, 154, 90, 0.10),
      0 0 12px rgba(196, 154, 90, 0.12),
      0 2px 8px rgba(0, 0, 0, 0.30);
  }

  .player-instrument-chip {
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    background: rgba(12, 15, 21, 0.70);
    border: 1px solid rgba(255, 255, 255, 0.10);
    border-radius: var(--radius-base);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.06),
      0 2px 8px rgba(0, 0, 0, 0.35);
  }
</style>
