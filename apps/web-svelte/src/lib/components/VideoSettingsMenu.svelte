<script lang="ts">
  import {
    Captions,
    ChevronDown,
    ChevronLeft,
    Gauge,
    RotateCw,
    Sliders,
    Volume2,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import {
    subtitleDisplayStyles,
    type SubtitleAppearance,
    type VideoSubtitleTrack,
  } from "$lib/player/subtitle-types";
  import {
    languageLabel,
    rangeProgress,
  } from "./video-player-format";
  import {
    PLAYBACK_RATES,
    type AudioTrackOption,
    type QualityMode,
    type QualityOption,
    type SettingsView,
  } from "./video-player-types";

  interface Props {
    activeQualityLabel?: string | null;
    activeSubtitleId?: string | null;
    activeSubtitleLabel: string;
    appearance: SubtitleAppearance;
    closing?: boolean;
    displayedAudioTrackLabel: string;
    displayedAudioTracks: AudioTrackOption[];
    localAppearance: Partial<SubtitleAppearance> | null;
    onAppearanceChange: (appearance: SubtitleAppearance) => void;
    onAppearanceReset: () => void;
    onClose: () => void;
    onOpenView: (view: SettingsView) => void;
    onPlaybackRateChange: (rate: number) => void;
    onQualityChange: (mode: QualityMode) => void;
    onSelectAudioTrack: (track: AudioTrackOption) => void;
    onSelectSubtitle: (id: string | null) => void;
    onViewChange: (view: SettingsView) => void;
    playbackRate: number;
    qualityMode: QualityMode;
    qualityOptions: QualityOption[];
    selectedQualityLabel?: string | null;
    subtitleTracks: VideoSubtitleTrack[];
    view: SettingsView;
  }

  let {
    activeQualityLabel = null,
    activeSubtitleId = null,
    activeSubtitleLabel,
    appearance,
    closing = false,
    displayedAudioTrackLabel,
    displayedAudioTracks,
    localAppearance,
    onAppearanceChange,
    onAppearanceReset,
    onClose,
    onOpenView,
    onPlaybackRateChange,
    onQualityChange,
    onSelectAudioTrack,
    onSelectSubtitle,
    onViewChange,
    playbackRate,
    qualityMode,
    qualityOptions,
    selectedQualityLabel = null,
    subtitleTracks,
    view,
  }: Props = $props();

  const viewTitle = $derived(
    view === "quality"
      ? "Quality"
      : view === "speed"
        ? "Speed"
        : view === "audio"
          ? "Audio"
          : view === "captions"
            ? "Captions"
            : "Subtitle style",
  );
</script>

<button
  type="button"
  class={cn("player-settings-backdrop", closing && "is-closing")}
  aria-label="Close player settings"
  onclick={(event) => {
    event.stopPropagation();
    onClose();
  }}
></button>
<div
  class={cn("player-settings-menu player-dropdown", closing && "is-closing")}
  role="menu"
  aria-label="Player settings menu"
>
  {#if view !== "root"}
    <button
      type="button"
      class="player-settings-back"
      onclick={() => onViewChange("root")}
    >
      <ChevronLeft class="h-4 w-4" />
      <span>{viewTitle}</span>
    </button>
  {/if}

  {#if view === "root"}
    <button type="button" class="player-settings-row" onclick={() => onOpenView("quality")}>
      <Gauge class="h-4 w-4" />
      <span>Quality</span>
      <span class="player-settings-value">{selectedQualityLabel ?? "Auto"}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </button>
    <button type="button" class="player-settings-row" onclick={() => onOpenView("speed")}>
      <RotateCw class="h-4 w-4" />
      <span>Speed</span>
      <span class="player-settings-value">{playbackRate === 1 ? "Normal" : `${playbackRate}x`}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </button>
    <button type="button" class="player-settings-row" onclick={() => onOpenView("audio")}>
      <Volume2 class="h-4 w-4" />
      <span>Audio</span>
      <span class="player-settings-value">{displayedAudioTrackLabel}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </button>
    {#if subtitleTracks.length > 0}
      <button type="button" class="player-settings-row" onclick={() => onOpenView("captions")}>
        <Captions class="h-4 w-4" />
        <span>Captions</span>
        <span class="player-settings-value">{activeSubtitleLabel}</span>
        <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
      </button>
      <button type="button" class="player-settings-row" onclick={() => onOpenView("subtitle-style")}>
        <Sliders class="h-4 w-4" />
        <span>Subtitle style</span>
        <span class="player-settings-value">Custom</span>
        <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
      </button>
    {/if}
  {:else if view === "quality"}
    {#each qualityOptions as option (String(option.value))}
      <button
        type="button"
        onclick={() => onQualityChange(option.value)}
        class={cn("player-settings-option", qualityMode === option.value && "is-active")}
      >
        <span>{option.label}</span>
        {#if option.value === "auto" && qualityMode === "auto" && activeQualityLabel}
          <span>{activeQualityLabel}</span>
        {:else if qualityMode === option.value}
          <span>On</span>
        {/if}
      </button>
    {/each}
  {:else if view === "speed"}
    {#each PLAYBACK_RATES as rate (rate)}
      <button
        type="button"
        onclick={() => onPlaybackRateChange(rate)}
        class={cn("player-settings-option", playbackRate === rate && "is-active")}
      >
        <span>{rate === 1 ? "Normal" : `${rate}x`}</span>
        {#if playbackRate === rate}<span>On</span>{/if}
      </button>
    {/each}
  {:else if view === "audio"}
    {#each displayedAudioTracks as track (track.id)}
      <button
        type="button"
        onclick={() => onSelectAudioTrack(track)}
        class={cn("player-settings-option", track.selected && "is-active")}
      >
        <span class="min-w-0 truncate">{track.label}</span>
        {#if track.selected}<span>On</span>{/if}
      </button>
    {/each}
  {:else if view === "captions"}
    <button
      type="button"
      onclick={() => onSelectSubtitle(null)}
      class={cn("player-settings-option", !activeSubtitleId && "is-active")}
    >
      <span>Off</span>
      {#if !activeSubtitleId}<span>On</span>{/if}
    </button>
    {#each subtitleTracks as track (track.id)}
      {@const isActive = activeSubtitleId === track.id}
      {@const lang = languageLabel(track.language)}
      {@const displayName = track.label ? `${lang} - ${track.label}` : lang}
      <button
        type="button"
        onclick={() => onSelectSubtitle(track.id)}
        class={cn("player-settings-option", isActive && "is-active")}
      >
        <span class="player-settings-option-label min-w-0 flex-1">{displayName}</span>
        <span>{isActive ? "On" : track.source}</span>
      </button>
    {/each}
  {:else if view === "subtitle-style"}
    {#each subtitleDisplayStyles as style (style)}
      <button
        type="button"
        onclick={() => onAppearanceChange({ ...appearance, style })}
        class={cn("player-settings-option", appearance.style === style && "is-active")}
      >
        <span class="capitalize">{style}</span>
        {#if appearance.style === style}<span>On</span>{/if}
      </button>
    {/each}

    <div class="player-settings-separator"></div>

    <label class="player-settings-control">
      <span>Text size</span>
      <span class="player-settings-control-value">{appearance.fontScale.toFixed(2)}x</span>
      <input
        type="range"
        min="0.5"
        max="3"
        step="0.05"
        value={appearance.fontScale}
        style={`--range-progress: ${rangeProgress(appearance.fontScale, 0.5, 3)}`}
        oninput={(event) =>
          onAppearanceChange({ ...appearance, fontScale: Number(event.currentTarget.value) })}
      />
    </label>

    <label class="player-settings-control">
      <span>Position</span>
      <span class="player-settings-control-value">{Math.round(appearance.positionPercent)}%</span>
      <input
        type="range"
        min="10"
        max="98"
        step="1"
        value={appearance.positionPercent}
        style={`--range-progress: ${rangeProgress(appearance.positionPercent, 10, 98)}`}
        oninput={(event) =>
          onAppearanceChange({
            ...appearance,
            positionPercent: Number(event.currentTarget.value),
          })}
      />
    </label>

    <label class="player-settings-control">
      <span>Opacity</span>
      <span class="player-settings-control-value">{Math.round(appearance.opacity * 100)}%</span>
      <input
        type="range"
        min="0.2"
        max="1"
        step="0.05"
        value={appearance.opacity}
        style={`--range-progress: ${rangeProgress(appearance.opacity, 0.2, 1)}`}
        oninput={(event) =>
          onAppearanceChange({ ...appearance, opacity: Number(event.currentTarget.value) })}
      />
    </label>

    <button
      type="button"
      onclick={onAppearanceReset}
      disabled={localAppearance == null}
      class={cn("player-settings-option player-settings-reset", localAppearance == null && "is-disabled")}
    >
      <span>Reset to library defaults</span>
    </button>
  {/if}
</div>

<style>
  .player-settings-menu {
    animation: player-settings-sheet-in var(--duration-moderate) var(--ease-enter);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
    background: rgba(21, 26, 40, 0.92);
    border: 1px solid var(--color-border-default, rgba(148, 158, 178, 0.13));
    border-radius: var(--radius-lg);
    bottom: max(0.75rem, env(safe-area-inset-bottom, 0px));
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.05),
      0 8px 40px rgba(0, 0, 0, 0.60);
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    height: min(34dvh, 18rem);
    left: max(0.75rem, env(safe-area-inset-left, 0px));
    max-height: calc(100dvh - 1.5rem);
    min-width: 0;
    overflow-y: auto;
    overscroll-behavior: contain;
    padding: 0.35rem;
    position: fixed;
    right: max(0.75rem, env(safe-area-inset-right, 0px));
    transform-origin: bottom right;
    top: auto;
    width: auto;
    z-index: 1005;
  }

  .player-settings-menu.is-closing {
    animation: player-settings-sheet-out var(--duration-normal) var(--ease-exit) forwards;
  }

  .player-settings-backdrop {
    background: rgba(0, 0, 0, 0.38);
    border: 0;
    bottom: 0;
    left: 0;
    position: fixed;
    right: 0;
    top: 0;
    z-index: 1000;
  }

  .player-settings-backdrop.is-closing {
    animation: player-settings-backdrop-out var(--duration-normal) var(--ease-exit) forwards;
  }

  .player-settings-row,
  .player-settings-option,
  .player-settings-back {
    align-items: center;
    border: 1px solid transparent;
    border-radius: var(--radius-base);
    color: rgba(255, 255, 255, 0.82);
    display: grid;
    gap: 0.75rem;
    min-height: 2.5rem;
    padding: 8px 12px;
    text-align: left;
    transition:
      background-color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
    width: 100%;
  }

  .player-settings-row {
    grid-template-columns: auto minmax(5rem, 1fr) minmax(0, 52%) auto;
    overflow: hidden;
  }

  .player-settings-option {
    grid-template-columns: minmax(0, 1fr) auto;
  }

  .player-settings-option-label {
    line-height: 1.18;
    overflow-wrap: anywhere;
  }

  .player-settings-back {
    border-bottom-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    font-size: 0.75rem;
    font-weight: 600;
    grid-template-columns: auto minmax(0, 1fr);
    letter-spacing: 0.14em;
    text-transform: uppercase;
  }

  .player-settings-row:hover,
  .player-settings-option:hover,
  .player-settings-back:hover {
    background: rgba(255, 255, 255, 0.08);
    border-color: var(--color-border-default, rgba(148, 158, 178, 0.13));
    color: #fff;
  }

  .player-settings-option.is-active {
    background: var(--color-accent-overlay-subtle);
    border-color: var(--color-border-accent-strong, rgba(196, 154, 90, 0.50));
    box-shadow: 0 0 0 1px rgba(196, 154, 90, 0.20), 0 0 8px rgba(196, 154, 90, 0.10);
    color: var(--color-accent-100);
  }

  .player-settings-value {
    color: rgba(255, 255, 255, 0.58);
    font-size: 0.7rem;
    min-width: 0;
    overflow: hidden;
    text-align: right;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .player-settings-separator {
    border-top: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    margin: 0.2rem 0;
  }

  .player-settings-control {
    border: 1px solid transparent;
    color: rgba(255, 255, 255, 0.82);
    display: grid;
    font-size: 0.74rem;
    gap: 0.55rem;
    grid-template-columns: minmax(0, 1fr) auto;
    padding: 0.65rem 0.7rem 0.75rem;
  }

  .player-settings-control-value {
    color: var(--color-accent-300);
    font-family: var(--font-mono);
    font-size: 0.68rem;
    letter-spacing: 0.04em;
  }

  .player-settings-control input[type="range"] {
    appearance: none;
    background:
      linear-gradient(
        to right,
        var(--color-accent-400) 0 var(--range-progress),
        rgba(255, 255, 255, 0.18) var(--range-progress) 100%
      );
    border: 1px solid var(--color-border-default, rgba(148, 158, 178, 0.13));
    border-radius: var(--radius-xs);
    grid-column: 1 / -1;
    height: 0.45rem;
    width: 100%;
  }

  .player-settings-control input[type="range"]::-webkit-slider-thumb {
    appearance: none;
    background: var(--color-accent-400);
    border: 1px solid rgba(0, 0, 0, 0.45);
    border-radius: 50%;
    box-shadow:
      0 0 0 1px rgba(196, 154, 90, 0.30),
      0 0 14px rgba(196, 154, 90, 0.40);
    height: 0.85rem;
    width: 0.85rem;
  }

  .player-settings-control input[type="range"]::-moz-range-thumb {
    background: var(--color-accent-400);
    border: 1px solid rgba(0, 0, 0, 0.45);
    border-radius: 50%;
    box-shadow:
      0 0 0 1px rgba(196, 154, 90, 0.30),
      0 0 14px rgba(196, 154, 90, 0.40);
    height: 0.85rem;
    width: 0.85rem;
  }

  .player-settings-reset:hover {
    background: rgba(255, 255, 255, 0.08);
  }

  .player-settings-reset.is-disabled {
    border-color: rgba(255, 255, 255, 0.06);
    color: rgba(255, 255, 255, 0.3);
    cursor: not-allowed;
  }

  @keyframes player-settings-sheet-in {
    from {
      opacity: 0;
      transform: translateY(calc(100% + 1.25rem));
    }

    to {
      opacity: 1;
      transform: translateY(0);
    }
  }

  @keyframes player-settings-sheet-out {
    from {
      opacity: 1;
      transform: translateY(0);
    }

    to {
      opacity: 0;
      transform: translateY(calc(100% + 1.25rem));
    }
  }

  @keyframes player-settings-backdrop-out {
    from {
      opacity: 1;
    }

    to {
      opacity: 0;
    }
  }

  @keyframes player-settings-flyout-in {
    from {
      opacity: 0;
      transform: translateX(1.25rem);
    }

    to {
      opacity: 1;
      transform: translateX(0);
    }
  }

  @keyframes player-settings-flyout-out {
    from {
      opacity: 1;
      transform: translateX(0);
    }

    to {
      opacity: 0;
      transform: translateX(1.25rem);
    }
  }

  @media (min-width: 640px) {
    .player-settings-menu {
      animation: player-settings-flyout-in var(--duration-moderate) var(--ease-enter);
      backdrop-filter: blur(var(--glass-blur-md));
      -webkit-backdrop-filter: blur(var(--glass-blur-md));
      background: rgba(16, 20, 32, 0.82);
      border: 1px solid var(--color-border-default, rgba(148, 158, 178, 0.13));
      border-radius: var(--radius-lg);
      bottom: 7.25rem;
      box-shadow:
        inset 0 1px 0 rgba(255, 255, 255, 0.05),
        0 8px 40px rgba(0, 0, 0, 0.60);
      height: auto;
      left: auto;
      max-height: none;
      min-width: min(19rem, calc(100vw - 2rem));
      transform-origin: bottom right;
      position: absolute;
      right: 1rem;
      top: 4.75rem;
      width: min(28rem, calc(100% - 2rem));
      z-index: 60;
    }

    .player-settings-menu.is-closing {
      animation: player-settings-flyout-out var(--duration-normal) var(--ease-exit) forwards;
    }

    .player-settings-backdrop {
      background: transparent;
      position: absolute;
      z-index: 55;
    }
  }
</style>
