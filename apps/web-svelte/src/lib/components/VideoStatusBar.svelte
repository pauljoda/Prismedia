<script lang="ts">
  import { Cpu, Gauge, MonitorPlay, Radio, Volume2 } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { StreamMethod } from "$lib/player/media-badges";

  interface Props {
    /** Delivery method, drives the leading pill's icon and tone. */
    playbackMethod: StreamMethod;
    /** Friendly method name: "Direct Play" / "Direct Stream" / "Transcoding". */
    methodLabel: string;
    /** One-line explanation, shown as the pill's tooltip. */
    methodHint?: string | null;
    /** Short output descriptor for transcoding (e.g. "1080p · H.264 · SDR"). */
    methodDetail?: string | null;
    /** Source resolution tier ("4K", "1080p", …). */
    resolutionLabel?: string | null;
    /** Exact dimensions and codec, shown as the resolution pill's tooltip. */
    videoDetail?: string | null;
    /** Friendly HDR format ("Dolby Vision", "HDR10", …); absent for SDR. */
    dynamicRangeLabel?: string | null;
    /** Active audio format ("Dolby Atmos 7.1", "Dolby Digital+ 5.1", …). */
    audioFormatLabel?: string | null;
    /** Seconds of media buffered ahead of the playhead; null hides the readout. */
    bufferSeconds?: number | null;
    /** Transient player message (e.g. a recovery notice). */
    playerNotice?: string | null;
    showControls: boolean;
  }

  let {
    playbackMethod,
    methodLabel,
    methodHint = null,
    methodDetail = null,
    resolutionLabel = null,
    videoDetail = null,
    dynamicRangeLabel = null,
    audioFormatLabel = null,
    bufferSeconds = null,
    playerNotice = null,
    showControls,
  }: Props = $props();

  const MethodIcon = $derived(
    playbackMethod === "transcode" ? Cpu : playbackMethod === "remux" ? Radio : MonitorPlay,
  );
  // Premium dynamic range earns the brass accent the design system reserves for "special" state.
  const isPremiumRange = $derived(
    dynamicRangeLabel === "Dolby Vision" ||
      dynamicRangeLabel === "HDR10+" ||
      dynamicRangeLabel === "HDR10",
  );
  const isPremiumAudio = $derived(
    Boolean(audioFormatLabel) &&
      /atmos|dts:x|dts-hd|truehd/i.test(audioFormatLabel ?? ""),
  );
</script>

<div
  class={cn(
    "player-top-bar pointer-events-none absolute inset-x-0 top-0 z-20 flex items-start justify-between gap-2 px-3 sm:px-4 pb-8 sm:pb-12 pt-3 sm:pt-4 transition-opacity duration-normal",
    showControls ? "opacity-100" : "opacity-0",
  )}
>
  <div class="flex flex-wrap items-center gap-1.5 sm:gap-2">
    <span
      data-testid="playback-method-chip"
      title={methodHint ?? methodLabel}
      class={cn(
        "pointer-events-auto player-method-chip flex items-center gap-1.5 px-2 py-1 text-[0.6rem] font-semibold uppercase tracking-[0.14em] sm:px-2.5 sm:text-[0.66rem]",
        playbackMethod === "transcode" ? "is-working" : "is-direct",
      )}
    >
      <MethodIcon class="h-3.5 w-3.5 shrink-0" />
      <span class="truncate">{methodLabel}</span>
      {#if methodDetail}
        <span class="hidden text-[0.55rem] font-medium tracking-[0.08em] opacity-70 sm:inline">
          {methodDetail}
        </span>
      {/if}
    </span>

    {#if resolutionLabel}
      <span
        data-testid="resolution-chip"
        title={videoDetail ?? resolutionLabel}
        class="pointer-events-auto player-spec-chip px-2 py-1 text-[0.6rem] font-semibold uppercase tracking-[0.12em] text-white/85 sm:px-2.5 sm:text-[0.66rem]"
      >
        {resolutionLabel}
      </span>
    {/if}

    {#if dynamicRangeLabel}
      <span
        data-testid="dynamic-range-chip"
        class={cn(
          "pointer-events-auto player-spec-chip px-2 py-1 text-[0.6rem] font-semibold uppercase tracking-[0.12em] sm:px-2.5 sm:text-[0.66rem]",
          isPremiumRange ? "is-premium" : "text-white/85",
        )}
      >
        {dynamicRangeLabel}
      </span>
    {/if}

    {#if audioFormatLabel}
      <span
        data-testid="audio-format-chip"
        class={cn(
          "pointer-events-auto player-spec-chip flex items-center gap-1 px-2 py-1 text-[0.6rem] font-semibold uppercase tracking-[0.1em] sm:px-2.5 sm:text-[0.66rem]",
          isPremiumAudio ? "is-premium" : "text-white/85",
        )}
      >
        <Volume2 class="h-3.5 w-3.5 shrink-0 opacity-70" />
        <span class="truncate">{audioFormatLabel}</span>
      </span>
    {/if}

    {#if playerNotice}
      <span
        class="pointer-events-auto player-spec-chip is-notice px-2 py-1 text-[0.6rem] tracking-[0.08em] sm:px-2.5 sm:text-[0.66rem]"
      >
        {playerNotice}
      </span>
    {/if}
  </div>

  {#if bufferSeconds != null && Number.isFinite(bufferSeconds)}
    <div
      data-testid="buffer-chip"
      title="Seconds buffered ahead of the playhead"
      class="player-instrument-chip pointer-events-auto hidden shrink-0 px-2 py-1.5 text-right sm:block"
    >
      <div class="mb-0.5 flex items-center justify-end gap-1 text-white/40">
        <Gauge class="h-3.5 w-3.5" />
        <span class="text-[0.58rem] uppercase tracking-[0.16em]">Buffer</span>
      </div>
      <div class="text-mono-tabular text-glow-phosphor text-[0.72rem] font-medium text-white/80">
        {Math.max(0, bufferSeconds).toFixed(1)}s
      </div>
    </div>
  {/if}
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

  .player-method-chip {
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    border-radius: var(--radius-base);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.08),
      0 2px 8px rgba(0, 0, 0, 0.32);
  }

  /* Direct play / direct stream: calm, "everything is optimal" cool tint. */
  .player-method-chip.is-direct {
    background: rgba(16, 30, 28, 0.78);
    border: 1px solid rgba(120, 210, 190, 0.30);
    color: rgb(170, 232, 218);
    box-shadow:
      inset 0 1px 0 rgba(120, 210, 190, 0.10),
      0 0 12px rgba(120, 210, 190, 0.10),
      0 2px 8px rgba(0, 0, 0, 0.32);
  }

  /* Transcoding: the server is doing work — warm brass "active" tint. */
  .player-method-chip.is-working {
    background: rgba(36, 30, 22, 0.80);
    border: 1px solid rgba(196, 154, 90, 0.34);
    color: rgb(242, 194, 106);
    box-shadow:
      inset 0 1px 0 rgba(196, 154, 90, 0.12),
      0 0 14px rgba(196, 154, 90, 0.16),
      0 2px 8px rgba(0, 0, 0, 0.32);
  }

  .player-spec-chip {
    backdrop-filter: blur(var(--glass-blur-sm));
    -webkit-backdrop-filter: blur(var(--glass-blur-sm));
    background: rgba(12, 15, 21, 0.70);
    border: 1px solid rgba(255, 255, 255, 0.12);
    border-radius: var(--radius-base);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.06),
      0 2px 8px rgba(0, 0, 0, 0.32);
  }

  /* Premium video/audio (Dolby Vision, HDR10, Atmos, DTS:X): brass glow per the design language. */
  .player-spec-chip.is-premium {
    background: rgba(36, 30, 22, 0.72);
    border-color: rgba(196, 154, 90, 0.36);
    color: rgb(242, 206, 142);
    box-shadow:
      inset 0 1px 0 rgba(196, 154, 90, 0.10),
      0 0 12px rgba(196, 154, 90, 0.14),
      0 2px 8px rgba(0, 0, 0, 0.32);
  }

  .player-spec-chip.is-notice {
    border-color: rgba(224, 168, 96, 0.28);
    color: rgba(255, 255, 255, 0.82);
    text-transform: none;
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
