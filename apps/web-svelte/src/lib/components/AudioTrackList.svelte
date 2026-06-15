<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import TrackListRow from "./TrackListRow.svelte";

  interface Props {
    tracks: AudioTrackListItemDto[];
    activeTrackId: string | null;
    isPlaying: boolean;
    onPlay: (trackId: string) => void;
    onRatingChange?: (trackId: string, value: number | null) => void;
    onRename?: (track: AudioTrackListItemDto, title: string) => void | Promise<void>;
    onDelete?: (track: AudioTrackListItemDto) => void;
    class?: string;
  }

  let {
    tracks,
    activeTrackId,
    isPlaying,
    onPlay,
    onRatingChange,
    onRename,
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

  // Group consecutive tracks by their section (disc) label. Multi-disc albums render a small
  // heading per section with track numbering that restarts inside each one; single-section
  // albums collapse to one unlabelled group and read exactly as before. `globalIndex` keeps a
  // stable overall position for each row.
  const sections = $derived.by(() => {
    const groups: {
      label: string | null;
      rows: { track: AudioTrackListItemDto; globalIndex: number; numberInSection: number }[];
    }[] = [];
    tracks.forEach((track, globalIndex) => {
      const label = track.sectionLabel ?? null;
      let group = groups[groups.length - 1];
      if (!group || group.label !== label) {
        group = { label, rows: [] };
        groups.push(group);
      }
      group.rows.push({ track, globalIndex, numberInSection: group.rows.length + 1 });
    });
    return groups;
  });

  const hasSections = $derived(sections.some((group) => group.label !== null));
</script>

<div class={cn("surface-panel border border-border-subtle overflow-hidden", className)}>
  <div
    class="hidden grid-cols-[2rem_minmax(0,1fr)_auto_3rem_2rem] items-center gap-3 border-b border-border-subtle px-4 py-2 sm:grid"
  >
    <span class="text-center font-mono text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">#</span>
    <span class="text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Title</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Rating</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Time</span>
    <span></span>
  </div>

  {#each sections as section (section.label ?? "__main__")}
    {#if hasSections && section.label}
      <div
        class="border-b border-border-subtle bg-surface-raised/40 px-4 py-1.5 font-mono text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled"
      >
        {section.label}
      </div>
    {/if}
    {#each section.rows as row (row.track.id)}
      <TrackListRow
        track={row.track}
        index={row.globalIndex}
        displayNumber={hasSections ? row.numberInSection : undefined}
        isActive={row.track.id === activeTrackId}
        isPlaying={row.track.id === activeTrackId && isPlaying}
        {onPlay}
        {onRatingChange}
        {onRename}
        {onDelete}
        trackHref={resolveEntityHref("audio-track", row.track.id)}
      />
    {/each}
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
