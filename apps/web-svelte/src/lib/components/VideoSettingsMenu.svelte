<script lang="ts">
  import {
    Settings2,
    Captions,
    ChevronDown,
    ChevronLeft,
    Gauge,
    RotateCw,
    Sliders,
    Volume2,
  } from "@lucide/svelte";
  import { Button, Popover, Separator, Slider, buttonVariants, cn } from "@prismedia/ui-svelte";
  import {
    subtitleDisplayStyles,
    type SubtitleAppearance,
    type VideoSubtitleTrack,
  } from "$lib/player/subtitle-types";
  import {
    languageLabel,
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
    open?: boolean;
    /** Native fullscreen containers are a separate browser top layer. */
    portalTarget?: HTMLElement | null;
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
    open = false,
    portalTarget,
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

<Popover.Root {open} onOpenChange={next => { if (next) onOpenView("root"); else onClose(); }}>
  <Popover.Trigger aria-label="Player settings" class={buttonVariants({ variant: "ghost", size: "icon" })}>
    <Settings2 />
  </Popover.Trigger>
  <Popover.Content side="top" align="end" aria-label="Player settings menu" class="w-[28rem] max-h-[min(32rem,var(--bits-popover-content-available-height))] overflow-y-auto p-1"
    portalProps={{ to: portalTarget ?? undefined }}>

  {#if view !== "root"}
    <Button variant="ghost"
      type="button"
      class="grid h-auto min-h-10 w-full grid-cols-[auto_1fr] justify-start gap-3 border-b px-3 py-2 text-left"
      onclick={() => onViewChange("root")}
    >
      <ChevronLeft class="h-4 w-4" />
      <span>{viewTitle}</span>
    </Button>
  {/if}

  {#if view === "root"}
    <Button variant="ghost" type="button" class="grid h-auto min-h-10 w-full grid-cols-[auto_minmax(5rem,1fr)_minmax(0,52%)_auto] gap-3 px-3 py-2 text-left" onclick={() => onOpenView("quality")}>
      <Gauge class="h-4 w-4" />
      <span>Quality</span>
      <span class="min-w-0 truncate text-right text-xs text-muted-foreground">{selectedQualityLabel ?? "Auto"}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </Button>
    <Button variant="ghost" type="button" class="grid h-auto min-h-10 w-full grid-cols-[auto_minmax(5rem,1fr)_minmax(0,52%)_auto] gap-3 px-3 py-2 text-left" onclick={() => onOpenView("speed")}>
      <RotateCw class="h-4 w-4" />
      <span>Speed</span>
      <span class="min-w-0 truncate text-right text-xs text-muted-foreground">{playbackRate === 1 ? "Normal" : `${playbackRate}x`}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </Button>
    <Button variant="ghost" type="button" class="grid h-auto min-h-10 w-full grid-cols-[auto_minmax(5rem,1fr)_minmax(0,52%)_auto] gap-3 px-3 py-2 text-left" onclick={() => onOpenView("audio")}>
      <Volume2 class="h-4 w-4" />
      <span>Audio</span>
      <span class="min-w-0 truncate text-right text-xs text-muted-foreground">{displayedAudioTrackLabel}</span>
      <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
    </Button>
    {#if subtitleTracks.length > 0}
      <Button variant="ghost" type="button" class="grid h-auto min-h-10 w-full grid-cols-[auto_minmax(5rem,1fr)_minmax(0,52%)_auto] gap-3 px-3 py-2 text-left" onclick={() => onOpenView("captions")}>
        <Captions class="h-4 w-4" />
        <span>Captions</span>
        <span class="min-w-0 truncate text-right text-xs text-muted-foreground">{activeSubtitleLabel}</span>
        <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
      </Button>
      <Button variant="ghost" type="button" class="grid h-auto min-h-10 w-full grid-cols-[auto_minmax(5rem,1fr)_minmax(0,52%)_auto] gap-3 px-3 py-2 text-left" onclick={() => onOpenView("subtitle-style")}>
        <Sliders class="h-4 w-4" />
        <span>Subtitle style</span>
        <span class="min-w-0 truncate text-right text-xs text-muted-foreground">Custom</span>
        <ChevronDown class="h-3.5 w-3.5 -rotate-90" />
      </Button>
    {/if}
  {:else if view === "quality"}
    {#each qualityOptions as option (String(option.value))}
      <Button variant="ghost"
        type="button"
        onclick={() => onQualityChange(option.value)}
        class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", qualityMode === option.value && "bg-accent text-foreground")}
      >
        <span>{option.label}</span>
        {#if option.value === "auto" && qualityMode === "auto" && activeQualityLabel}
          <span>{activeQualityLabel}</span>
        {:else if qualityMode === option.value}
          <span>On</span>
        {/if}
      </Button>
    {/each}
  {:else if view === "speed"}
    {#each PLAYBACK_RATES as rate (rate)}
      <Button variant="ghost"
        type="button"
        onclick={() => onPlaybackRateChange(rate)}
        class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", playbackRate === rate && "bg-accent text-foreground")}
      >
        <span>{rate === 1 ? "Normal" : `${rate}x`}</span>
        {#if playbackRate === rate}<span>On</span>{/if}
      </Button>
    {/each}
  {:else if view === "audio"}
    {#each displayedAudioTracks as track (track.id)}
      <Button variant="ghost"
        type="button"
        onclick={() => onSelectAudioTrack(track)}
        class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", track.selected && "bg-accent text-foreground")}
      >
        <span class="min-w-0 truncate">{track.label}</span>
        {#if track.selected}<span>On</span>{/if}
      </Button>
    {/each}
  {:else if view === "captions"}
    <Button variant="ghost"
      type="button"
      onclick={() => onSelectSubtitle(null)}
      class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", !activeSubtitleId && "bg-accent text-foreground")}
    >
      <span>Off</span>
      {#if !activeSubtitleId}<span>On</span>{/if}
    </Button>
    {#each subtitleTracks as track (track.id)}
      {@const isActive = activeSubtitleId === track.id}
      {@const lang = languageLabel(track.language)}
      {@const displayName = track.label ? `${lang} - ${track.label}` : lang}
      <Button variant="ghost"
        type="button"
        onclick={() => onSelectSubtitle(track.id)}
        class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", isActive && "bg-accent text-foreground")}
      >
        <span class="min-w-0 flex-1 break-words">{displayName}</span>
        <span>{isActive ? "On" : track.source}</span>
      </Button>
    {/each}
  {:else if view === "subtitle-style"}
    {#each subtitleDisplayStyles as style (style)}
      <Button variant="ghost"
        type="button"
        onclick={() => onAppearanceChange({ ...appearance, style })}
        class={cn("grid h-auto min-h-10 w-full grid-cols-[minmax(0,1fr)_auto] gap-3 whitespace-normal px-3 py-2 text-left", appearance.style === style && "bg-accent text-foreground")}
      >
        <span class="capitalize">{style}</span>
        {#if appearance.style === style}<span>On</span>{/if}
      </Button>
    {/each}

    <Separator />

    <div class="grid grid-cols-[1fr_auto] gap-2 px-3 py-3 text-sm">
      <span>Text size</span>
      <span class="font-mono text-xs text-muted-foreground">{appearance.fontScale.toFixed(2)}x</span>
      <Slider type="single" min={0.5} max={3} step={0.05} value={appearance.fontScale}
        thumbLabel="Subtitle text size" class="col-span-full"
        onValueChange={value => onAppearanceChange({ ...appearance, fontScale: value })} />
    </div>

    <div class="grid grid-cols-[1fr_auto] gap-2 px-3 py-3 text-sm">
      <span>Position</span>
      <span class="font-mono text-xs text-muted-foreground">{Math.round(appearance.positionPercent)}%</span>
      <Slider type="single" min={10} max={98} step={1} value={appearance.positionPercent}
        thumbLabel="Subtitle position" class="col-span-full"
        onValueChange={value => onAppearanceChange({ ...appearance, positionPercent: value })} />
    </div>

    <div class="grid grid-cols-[1fr_auto] gap-2 px-3 py-3 text-sm">
      <span>Opacity</span>
      <span class="font-mono text-xs text-muted-foreground">{Math.round(appearance.opacity * 100)}%</span>
      <Slider type="single" min={0.2} max={1} step={0.05} value={appearance.opacity}
        thumbLabel="Subtitle opacity" class="col-span-full"
        onValueChange={value => onAppearanceChange({ ...appearance, opacity: value })} />
    </div>

    <Button variant="ghost"
      type="button"
      onclick={onAppearanceReset}
      disabled={localAppearance == null}
      class="w-full justify-start"
    >
      <span>Reset to library defaults</span>
    </Button>
  {/if}
</Popover.Content>
</Popover.Root>
