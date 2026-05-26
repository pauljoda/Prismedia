<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { AudioTrackListItemDto } from "@prismedia/contracts";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import TrackListRow from "./TrackListRow.svelte";

  interface Props {
    tracks: AudioTrackListItemDto[];
    activeTrackId: string | null;
    isPlaying: boolean;
    onPlay: (trackId: string) => void;
    onRatingChange?: (trackId: string, value: number | null) => void;
    onDelete?: (track: AudioTrackListItemDto) => void;
    class?: string;
  }

  let {
    tracks,
    activeTrackId,
    isPlaying,
    onPlay,
    onRatingChange,
    onDelete,
    class: className = "",
  }: Props = $props();

  function formatTotalDuration(seconds: number): string {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    if (h > 0) return `${h} hr ${m} min`;
    return `${m} min`;
  }

  const totalDuration = $derived(
    tracks.reduce((sum, t) => sum + (t.duration ?? 0), 0),
  );
</script>

<div class={cn("surface-panel border border-border-subtle overflow-hidden", className)}>
  <div
    class="grid grid-cols-[2rem_minmax(0,1fr)_auto_3rem_1.75rem] items-center gap-3 border-b border-border-subtle px-3 py-2 sm:px-4"
  >
    <span class="text-center font-mono text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">#</span>
    <span class="text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Title</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Rating</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Time</span>
    <span></span>
  </div>

  {#each tracks as track, index (track.id)}
    <TrackListRow
      {track}
      {index}
      isActive={track.id === activeTrackId}
      isPlaying={track.id === activeTrackId && isPlaying}
      {onPlay}
      {onRatingChange}
      {onDelete}
      trackHref={resolveEntityHref("audio-track", track.id)}
    />
  {/each}

  {#if tracks.length > 0}
    <div class="flex items-center justify-between border-t border-border-subtle px-3 py-2 sm:px-4">
      <span class="font-mono text-[0.72rem] tabular-nums text-text-disabled">
        {tracks.length} {tracks.length === 1 ? "track" : "tracks"}
      </span>
      {#if totalDuration > 0}
        <span class="font-mono text-[0.72rem] tabular-nums text-text-disabled">
          {formatTotalDuration(totalDuration)}
        </span>
      {/if}
    </div>
  {/if}
</div>
