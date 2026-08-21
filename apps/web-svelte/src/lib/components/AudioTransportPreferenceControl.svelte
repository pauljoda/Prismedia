<script lang="ts">
  import { Volume1, Volume2, VolumeX } from "@lucide/svelte";
  import {
    formatAudioPlaybackRate,
    nextAudioPlaybackRate,
  } from "$lib/player/audio-playback-rate";

  interface Props {
    accentColor: string;
    muted: boolean;
    playbackRate: number;
    supportsPlaybackRate: boolean;
    volume: number;
    onMute: () => void;
    onPlaybackRate: (rate: number) => void;
    onVolume: (volume: number) => void;
  }

  let {
    accentColor,
    muted,
    playbackRate,
    supportsPlaybackRate,
    volume,
    onMute,
    onPlaybackRate,
    onVolume,
  }: Props = $props();

  const VolumeIcon = $derived(muted || volume === 0 ? VolumeX : volume < 0.5 ? Volume1 : Volume2);
</script>

<div class="group/vol flex min-w-0 items-center gap-1">
  {#if supportsPlaybackRate}
    <button
      type="button"
      onclick={() => onPlaybackRate(nextAudioPlaybackRate(playbackRate))}
      title={`Playback speed: ${formatAudioPlaybackRate(playbackRate)}`}
      aria-label={`Playback speed: ${formatAudioPlaybackRate(playbackRate)}`}
      class="player-icon-control min-w-9 px-1 py-1 font-mono text-[0.65rem] tabular-nums transition-colors"
    >
      {formatAudioPlaybackRate(playbackRate)}
    </button>
  {:else}
    <button type="button" onclick={onMute} class="player-icon-control p-1 transition-colors">
      <VolumeIcon class="h-3 w-3" />
    </button>
    <div class="w-0 overflow-hidden transition-all duration-200 group-hover/vol:w-16">
      <input
        type="range"
        min="0"
        max="1"
        step="0.01"
        value={muted ? 0 : volume}
        oninput={(event) => onVolume(Number(event.currentTarget.value))}
        class="h-1 w-full cursor-pointer"
        style:accent-color={accentColor}
      />
    </div>
  {/if}
</div>

<style>
  .player-icon-control {
    color: var(--color-text-disabled);
  }

  .player-icon-control:hover:not(:disabled) {
    color: var(--color-text-muted);
  }
</style>
