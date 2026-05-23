<script lang="ts">
  import { Captions } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import {
    defaultSubtitleAppearance,
    subtitleDisplayStyles,
    type SubtitleAppearance,
    type SubtitleDisplayStyle,
  } from "$lib/settings/library-settings";
  import type { LibrarySettings as LibrarySettings } from "$lib/api/prismedia";
  import SubtitleCaptionOverlay from "$lib/components/SubtitleCaptionOverlay.svelte";
  import ToggleCard from "./ToggleCard.svelte";

  interface Props {
    settings: LibrarySettings;
    onToggleAutoEnable: (checked: boolean) => void;
    onLanguagesCommit: (value: string) => void;
    onAppearanceCommit: (next: SubtitleAppearance) => void;
  }

  let { settings, onToggleAutoEnable, onLanguagesCommit, onAppearanceCommit }: Props = $props();

  const STYLE_LABELS: Record<SubtitleDisplayStyle, string> = {
    stylized: "Stylized",
    classic: "Classic",
    outline: "Outline",
  };

  const STYLE_DESCRIPTIONS: Record<SubtitleDisplayStyle, string> = {
    stylized: "Dark Room brass-edged plate",
    classic: "Flat black box, plain white text",
    outline: "White text with black stroke, no box",
  };

  const appearance = $derived<SubtitleAppearance>({
    style: (settings.subtitleStyle ?? defaultSubtitleAppearance.style) as SubtitleDisplayStyle,
    fontScale: settings.subtitleFontScale ?? defaultSubtitleAppearance.fontScale,
    positionPercent:
      settings.subtitlePositionPercent ?? defaultSubtitleAppearance.positionPercent,
    opacity: settings.subtitleOpacity ?? defaultSubtitleAppearance.opacity,
  });

  let langDraft = $state("en,eng");

  $effect(() => {
    langDraft = settings.subtitlesPreferredLanguages ?? "en,eng";
  });

  function commitLang(el: HTMLInputElement) {
    const next = langDraft.trim();
    if (next !== (settings.subtitlesPreferredLanguages ?? "")) {
      onLanguagesCommit(next);
    }
  }

  function rangeProgress(value: number, min: number, max: number): string {
    if (max <= min) return "0%";
    const pct = ((value - min) / (max - min)) * 100;
    return `${Math.max(0, Math.min(100, pct))}%`;
  }
</script>

<section class="space-y-3">
  <div class="flex items-center gap-2.5 px-1">
    <Captions class="h-4 w-4 text-text-accent" />
    <div>
      <h2
        class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase"
      >
        Subtitles
      </h2>
      <p class="text-[0.68rem] text-text-muted">
        Defaults applied to the video player when a video has subtitle tracks
      </p>
    </div>
  </div>

  <div class="grid gap-2 md:grid-cols-2">
    <ToggleCard
      label="Auto-enable on load"
      description="Turn on subtitles automatically when a video has a track matching your preferred languages."
      checked={settings.subtitlesAutoEnable ?? false}
      onChange={onToggleAutoEnable}
    />
    <div class="surface-card no-lift p-3.5 flex flex-col justify-between min-h-[100px]">
      <div>
        <label class="control-label" for="subtitle-lang-input">Preferred languages</label>
        <p class="text-[0.68rem] text-text-muted mt-1">
          Comma-separated priority list (e.g. <code class="language-code-token">en,eng,en-US</code>). First match
          wins.
        </p>
      </div>
      <input
        id="subtitle-lang-input"
        type="text"
        bind:value={langDraft}
        onblur={(e) => commitLang(e.currentTarget)}
        onkeydown={(e) => {
          if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur();
        }}
        class="allow-compact-input-text language-code-input mt-3 border border-border-default bg-surface-1 px-2.5 py-1.5 text-text-primary focus:border-border-accent focus:outline-none"
        placeholder="en,eng"
      />
    </div>
  </div>

  <div class="grid gap-2 md:grid-cols-2">
    <div class="surface-card no-lift p-3.5 space-y-3">
      <div>
        <div class="control-label">Display style</div>
        <p class="text-[0.68rem] text-text-muted mt-1">
          The preview on the right updates live as you change these.
        </p>
      </div>
      <div class="flex flex-col gap-1.5">
        {#each subtitleDisplayStyles as style (style)}
          {@const isActive = appearance.style === style}
          <button
            type="button"
            onclick={() => onAppearanceCommit({ ...appearance, style })}
            class={cn(
              "settings-menu-option",
              isActive
                ? "is-active"
                : "",
            )}
          >
            <div>
              <div class="settings-menu-option-title">{STYLE_LABELS[style]}</div>
              <div class="settings-menu-option-description">
                {STYLE_DESCRIPTIONS[style]}
              </div>
            </div>
            {#if isActive}
              <span class="settings-menu-status">On</span>
            {/if}
          </button>
        {/each}
      </div>

      <label class="settings-menu-control">
        <span>Text size</span>
        <span class="settings-menu-control-value">{appearance.fontScale.toFixed(2)}x</span>
        <input
          type="range"
          min="0.5"
          max="3"
          step="0.05"
          value={appearance.fontScale}
          oninput={(e) =>
            onAppearanceCommit({
              ...appearance,
              fontScale: Number((e.currentTarget as HTMLInputElement).value),
            })}
          style={`--range-progress: ${rangeProgress(appearance.fontScale, 0.5, 3)}`}
          aria-label="Subtitle text size"
        />
      </label>

      <label class="settings-menu-control">
        <span>Vertical position</span>
        <span class="settings-menu-control-value">{Math.round(appearance.positionPercent)}%</span>
        <input
          type="range"
          min="10"
          max="98"
          step="1"
          value={appearance.positionPercent}
          oninput={(e) =>
            onAppearanceCommit({
              ...appearance,
              positionPercent: Number((e.currentTarget as HTMLInputElement).value),
            })}
          style={`--range-progress: ${rangeProgress(appearance.positionPercent, 10, 98)}`}
          aria-label="Subtitle vertical position"
        />
      </label>

      <label class="settings-menu-control">
        <span>Transparency</span>
        <span class="settings-menu-control-value">{Math.round(appearance.opacity * 100)}%</span>
        <input
          type="range"
          min="0.2"
          max="1"
          step="0.05"
          value={appearance.opacity}
          oninput={(e) =>
            onAppearanceCommit({
              ...appearance,
              opacity: Number((e.currentTarget as HTMLInputElement).value),
            })}
          style={`--range-progress: ${rangeProgress(appearance.opacity, 0.2, 1)}`}
          aria-label="Subtitle transparency"
        />
      </label>
    </div>

    <div class="surface-card no-lift p-3.5 flex flex-col">
      <div>
        <div class="control-label">Preview</div>
        <p class="text-[0.68rem] text-text-muted mt-1">
          Shows how captions will render on top of a video.
        </p>
      </div>
      <div
        class="relative mt-3 aspect-video w-full overflow-hidden border border-border-subtle bg-black"
      >
        <div
          class="absolute inset-0 bg-[linear-gradient(135deg,#1a1f2b_0%,#0e1118_45%,#2a1f14_100%)]"
        ></div>
        <div
          class="absolute inset-0 opacity-[0.08]"
          style:background-image="repeating-linear-gradient(90deg, rgba(255,255,255,0.6) 0, rgba(255,255,255,0.6) 1px, transparent 1px, transparent 32px), repeating-linear-gradient(0deg, rgba(255,255,255,0.6) 0, rgba(255,255,255,0.6) 1px, transparent 1px, transparent 32px)"
        ></div>
        <div
          class="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/80 to-transparent"
        ></div>
        <SubtitleCaptionOverlay
          text="This is how your subtitles will look."
          {appearance}
          alwaysVisible
        />
      </div>
    </div>
  </div>
</section>

<style>
  .language-code-token,
  .language-code-input {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    letter-spacing: 0.02em;
  }

  .language-code-token {
    color: var(--color-accent-300, #d7b071);
    font-size: 0.68rem;
  }

  .language-code-input {
    font-size: max(16px, 1rem);
    line-height: 1.35;
  }

  @media (min-width: 48rem) {
    input.language-code-input {
      font-size: 0.74rem !important;
    }
  }

  .settings-menu-option {
    align-items: start;
    border: 1px solid var(--color-border-default, #273041);
    color: var(--color-text-secondary, #c4c9d4);
    display: grid;
    gap: 0.75rem;
    grid-template-columns: minmax(0, 1fr) auto;
    min-height: 2.75rem;
    padding: 0.62rem 0.7rem;
    text-align: left;
    transition:
      background-color 120ms ease,
      border-color 120ms ease,
      color 120ms ease;
    width: 100%;
  }

  .settings-menu-option:hover,
  .settings-menu-option:focus-visible {
    border-color: rgba(196, 154, 90, 0.46);
    color: var(--color-text-primary, #f2eed8);
    outline: none;
  }

  .settings-menu-option.is-active {
    background: rgba(196, 154, 90, 0.14);
    border-color: rgba(196, 154, 90, 0.42);
    color: var(--color-text-primary, #f2eed8);
  }

  .settings-menu-option-title {
    font-size: 0.74rem;
    font-weight: 600;
    line-height: 1.2;
  }

  .settings-menu-option-description {
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.64rem;
    line-height: 1.25;
    margin-top: 0.2rem;
  }

  .settings-menu-status {
    color: var(--color-text-accent, #c49a5a);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
  }

  .settings-menu-control {
    border: 1px solid transparent;
    color: var(--color-text-secondary, #c4c9d4);
    display: grid;
    font-size: 0.68rem;
    gap: 0.52rem;
    grid-template-columns: minmax(0, 1fr) auto;
    padding: 0.62rem 0.7rem 0.72rem;
  }

  .settings-menu-control-value {
    color: var(--color-text-accent, #c49a5a);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0.04em;
  }

  .settings-menu-control input[type="range"] {
    appearance: none;
    background:
      linear-gradient(
        to right,
        var(--color-accent-400, #d4a55f) 0 var(--range-progress),
        rgba(255, 255, 255, 0.22) var(--range-progress) 100%
      );
    border: 1px solid rgba(255, 255, 255, 0.32);
    border-radius: 0;
    grid-column: 1 / -1;
    height: 0.42rem;
    width: 100%;
  }

  .settings-menu-control input[type="range"]::-webkit-slider-thumb {
    appearance: none;
    background: var(--color-accent-400, #d4a55f);
    border: 1px solid rgba(0, 0, 0, 0.45);
    border-radius: 0;
    box-shadow: 0 0 12px rgba(196, 154, 90, 0.34);
    height: 0.95rem;
    width: 0.52rem;
  }

  .settings-menu-control input[type="range"]::-moz-range-thumb {
    background: var(--color-accent-400, #d4a55f);
    border: 1px solid rgba(0, 0, 0, 0.45);
    border-radius: 0;
    box-shadow: 0 0 12px rgba(196, 154, 90, 0.34);
    height: 0.95rem;
    width: 0.52rem;
  }
</style>
