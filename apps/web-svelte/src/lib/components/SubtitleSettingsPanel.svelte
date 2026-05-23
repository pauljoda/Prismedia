<script lang="ts">
  import { X, RotateCcw } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import {
    subtitleDisplayStyles,
    type SubtitleAppearance,
    type SubtitleDisplayStyle,
  } from "$lib/settings/library-settings";
  import { portal } from "$lib/actions/portal";

  interface Props {
    appearance: SubtitleAppearance;
    onChange: (next: SubtitleAppearance) => void;
    onClose: () => void;
    onReset: () => void;
    hasLocalOverride: boolean;
  }

  let { appearance, onChange, onClose, onReset, hasLocalOverride }: Props = $props();

  const STYLE_LABELS: Record<SubtitleDisplayStyle, string> = {
    stylized: "Stylized",
    classic: "Classic",
    outline: "Outline",
  };

  const STYLE_DESCRIPTIONS: Record<SubtitleDisplayStyle, string> = {
    stylized: "Dark Room brass-edged plate",
    classic: "Flat black box, white text",
    outline: "White text with black stroke, no box",
  };
</script>

<div
  use:portal
  role="presentation"
  class="fixed inset-0 z-[220] flex items-stretch justify-center bg-black/58 p-3 backdrop-blur-sm sm:items-center sm:p-6"
  onclick={(e) => {
    if (e.target === e.currentTarget) onClose();
  }}
>
  <div
    role="dialog"
    aria-modal="true"
    aria-label="Subtitle style"
    tabindex="-1"
    class="player-dropdown flex h-full max-h-[calc(100dvh-1.5rem)] w-full max-w-[30rem] flex-col sm:h-auto sm:max-h-[min(42rem,calc(100dvh-3rem))]"
  >
    <div class="flex items-center justify-between border-b border-white/10 px-4 py-3">
      <span class="text-[0.7rem] font-semibold uppercase tracking-[0.16em] text-white/80">
        Subtitle style
      </span>
      <button
        type="button"
        onclick={onClose}
        class="text-white/60 hover:text-white transition-colors"
        aria-label="Close subtitle settings"
      >
        <X class="h-4 w-4" />
      </button>
    </div>

    <div class="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-4">
      <!-- Style pickers -->
      <div class="space-y-2">
        <span class="text-[0.62rem] uppercase tracking-[0.16em] text-white/50">Style</span>
        <div class="grid grid-cols-1 gap-1.5">
          {#each subtitleDisplayStyles as style (style)}
            {@const isActive = appearance.style === style}
            <button
              type="button"
              onclick={() => onChange({ ...appearance, style })}
              class={cn(
                "flex w-full items-start justify-between gap-2 border px-3 py-2.5 text-left transition-colors duration-fast",
                isActive
                  ? "border-accent-500/50 bg-accent-500/15 text-accent-100"
                  : "border-white/10 text-white/75 hover:border-white/25 hover:text-white",
              )}
            >
              <div>
                <div class="text-[0.82rem] font-medium">{STYLE_LABELS[style]}</div>
                <div class="text-[0.68rem] text-white/55">{STYLE_DESCRIPTIONS[style]}</div>
              </div>
              {#if isActive}
                <span class="text-[0.58rem] uppercase tracking-[0.16em]">On</span>
              {/if}
            </button>
          {/each}
        </div>
      </div>

      <!-- Font scale -->
      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <span class="text-[0.62rem] uppercase tracking-[0.16em] text-white/50">Text size</span>
          <span class="text-mono-tabular text-[0.7rem] text-white/80">
            {appearance.fontScale.toFixed(2)}x
          </span>
        </div>
        <input
          type="range"
          min="0.5"
          max="3"
          step="0.05"
          value={appearance.fontScale}
          oninput={(e) =>
            onChange({ ...appearance, fontScale: Number((e.currentTarget as HTMLInputElement).value) })}
          class="w-full accent-accent-500"
          aria-label="Subtitle text size"
        />
      </div>

      <!-- Vertical position -->
      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <span class="text-[0.62rem] uppercase tracking-[0.16em] text-white/50">
            Vertical position
          </span>
          <span class="text-mono-tabular text-[0.7rem] text-white/80">
            {Math.round(appearance.positionPercent)}%
          </span>
        </div>
        <input
          type="range"
          min="10"
          max="98"
          step="1"
          value={appearance.positionPercent}
          oninput={(e) =>
            onChange({
              ...appearance,
              positionPercent: Number((e.currentTarget as HTMLInputElement).value),
            })}
          class="w-full accent-accent-500"
          aria-label="Subtitle vertical position"
        />
      </div>

      <!-- Transparency -->
      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <span class="text-[0.62rem] uppercase tracking-[0.16em] text-white/50">Transparency</span>
          <span class="text-mono-tabular text-[0.7rem] text-white/80">
            {Math.round(appearance.opacity * 100)}%
          </span>
        </div>
        <input
          type="range"
          min="0.2"
          max="1"
          step="0.05"
          value={appearance.opacity}
          oninput={(e) =>
            onChange({
              ...appearance,
              opacity: Number((e.currentTarget as HTMLInputElement).value),
            })}
          class="w-full accent-accent-500"
          aria-label="Subtitle transparency"
        />
      </div>

      <button
        type="button"
        onclick={onReset}
        disabled={!hasLocalOverride}
        class={cn(
          "flex items-center justify-center gap-1.5 w-full border px-3 py-2 text-[0.72rem] transition-colors",
          hasLocalOverride
            ? "border-white/15 text-white/78 hover:border-white/30 hover:text-white"
            : "border-white/5 text-white/30 cursor-not-allowed",
        )}
      >
        <RotateCcw class="h-3 w-3" />
        Reset to library defaults
      </button>
    </div>
  </div>
</div>
