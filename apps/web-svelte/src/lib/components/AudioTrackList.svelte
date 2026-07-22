<script lang="ts">
  import { Checkbox, cn } from "@prismedia/ui-svelte";
  import type { CollectionEntityType } from "$lib/collections/models";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityGridBulkAction } from "$lib/entities/entity-grid";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import BulkSelectionBar from "./entities/BulkSelectionBar.svelte";
  import TrackListRow from "./TrackListRow.svelte";

  interface Props {
    bulkActions?: EntityGridBulkAction[];
    tracks: AudioTrackListItemDto[];
    activeTrackId: string | null;
    isPlaying: boolean;
    onPlay: (trackId: string) => void;
    onRatingChange?: (trackId: string, value: number | null) => void;
    onRename?: (track: AudioTrackListItemDto, title: string) => void | Promise<void>;
    onDelete?: (track: AudioTrackListItemDto) => void;
    onSelectionChange?: (selectedIds: string[]) => void;
    artworkUrls?: Record<string, string | null | undefined>;
    selectable?: boolean;
    class?: string;
  }

  let {
    bulkActions = [],
    tracks,
    activeTrackId,
    isPlaying,
    onPlay,
    onRatingChange,
    onRename,
    onDelete,
    onSelectionChange,
    artworkUrls = {},
    selectable = true,
    class: className = "",
  }: Props = $props();

  let selectedIds = $state<string[]>([]);

  function formatTotalDuration(seconds: number): string {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    if (h > 0) return `${h} hr ${m} min`;
    return `${m} min`;
  }

  const totalDuration = $derived(
    tracks.reduce((sum, t) => sum + (t.duration ?? 0), 0),
  );
  const hasArtwork = $derived(
    tracks.some((track) => track.libraryId && artworkUrls[track.libraryId]),
  );
  const presentTracks = $derived(
    tracks.filter((track) => track.hasSourceMedia !== false && track.isWanted !== true),
  );
  const missingTrackCount = $derived(tracks.length - presentTracks.length);
  const visibleTrackIds = $derived(new Set(presentTracks.map((track) => track.id)));
  const visibleSelectedIds = $derived(selectedIds.filter((id) => visibleTrackIds.has(id)));
  const selectedIdSet = $derived(new Set(visibleSelectedIds));
  const selectedCount = $derived(visibleSelectedIds.length);
  const allTracksSelected = $derived(
    presentTracks.length > 0 && presentTracks.every((track) => selectedIdSet.has(track.id)),
  );
  const someTracksSelected = $derived(selectedCount > 0 && !allTracksSelected);
  const selectedCollectionItems = $derived(
    visibleSelectedIds.map((id) => ({
      entityType: ENTITY_KIND.audioTrack as CollectionEntityType,
      entityId: id,
    })),
  );

  // Group consecutive tracks by their section (disc) label. Multi-disc albums render a small
  // heading per section with track numbering that restarts inside each one; single-section
  // albums collapse to one unlabelled group and read exactly as before. `globalIndex` keeps a
  // stable overall position for each row.
  const sections = $derived.by(() => {
    const groups: {
      key: string;
      sectionKey: string | null;
      label: string | null;
      rows: { track: AudioTrackListItemDto; globalIndex: number; numberInSection: number }[];
    }[] = [];
    tracks.forEach((track, globalIndex) => {
      const label = track.sectionLabel ?? null;
      const sectionKey = track.sectionKey ?? label;
      let group = groups[groups.length - 1];
      if (!group || group.sectionKey !== sectionKey) {
        group = { key: `${groups.length}:${sectionKey ?? "__main__"}`, sectionKey, label, rows: [] };
        groups.push(group);
      }
      group.rows.push({ track, globalIndex, numberInSection: group.rows.length + 1 });
    });
    return groups;
  });

  const hasSections = $derived(sections.some((group) => group.label !== null));

  function setSelectedIds(ids: string[]) {
    selectedIds = ids;
    onSelectionChange?.(selectedIds);
  }

  function updateSelection(id: string, selected: boolean) {
    setSelectedIds(
      selected
        ? Array.from(new Set([...selectedIds, id]))
        : selectedIds.filter((selectedId) => selectedId !== id),
    );
  }

  function selectAllTracks() {
    setSelectedIds(presentTracks.map((track) => track.id));
  }

  function clearSelection() {
    setSelectedIds([]);
  }

  function handleSelectAllChange(event: Event) {
    if ((event.currentTarget as HTMLInputElement).checked) {
      selectAllTracks();
      return;
    }

    clearSelection();
  }
</script>

<div class={cn("surface-panel border border-border-subtle overflow-hidden", className)}>
  <div
    class={cn(
      "track-header hidden items-center gap-3 border-b border-border-subtle px-4 py-2 sm:grid",
      selectable && "has-selection",
      hasArtwork && "has-artwork",
    )}
  >
    {#if selectable}
      <span></span>
    {/if}
    <span class="text-center font-mono text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">#</span>
    {#if hasArtwork}<span aria-hidden="true"></span>{/if}
    <span class="text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Title</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Rating</span>
    <span class="justify-self-end text-[0.65rem] font-semibold uppercase tracking-widest text-text-disabled">Time</span>
    <span></span>
  </div>

  {#if selectable && presentTracks.length > 0}
    <div class="track-select-row">
      <label class="track-select-all">
        <Checkbox
          checked={allTracksSelected}
          indeterminate={someTracksSelected}
          aria-label="Select all tracks"
          onchange={handleSelectAllChange}
        />
        <span>Select all</span>
      </label>
      <span class="track-select-count">
        {selectedCount > 0
          ? `${selectedCount} selected`
          : missingTrackCount > 0
            ? `${presentTracks.length} present · ${missingTrackCount} missing`
            : `${tracks.length} ${tracks.length === 1 ? "track" : "tracks"}`}
      </span>
    </div>

    {#if selectedCount > 0}
      <BulkSelectionBar
        bulkActions={bulkActions}
        collectionItems={selectedCollectionItems}
        onClearSelection={clearSelection}
        onSelectAllVisible={selectAllTracks}
        selectedCount={selectedCount}
        selectedIds={visibleSelectedIds}
        showNsfwAction={false}
        showSelectionToggle={false}
        variant="track-list"
      />
    {/if}
  {/if}

  {#each sections as section (section.key)}
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
        artworkUrl={row.track.libraryId ? artworkUrls[row.track.libraryId] : null}
        showArtwork={hasArtwork}
        index={row.globalIndex}
        displayNumber={hasSections ? row.numberInSection : undefined}
        isActive={row.track.id === activeTrackId}
        isPlaying={row.track.id === activeTrackId && isPlaying}
        {onPlay}
        {onRatingChange}
        {onRename}
        {onDelete}
        selectable={selectable}
        selected={selectedIdSet.has(row.track.id)}
        onSelectedChange={(selected) => updateSelection(row.track.id, selected)}
        trackHref={resolveEntityHref(ENTITY_KIND.audioTrack, row.track.id)}
      />
    {/each}
  {/each}

  {#if tracks.length > 0}
    <div class="flex items-center justify-between border-t border-border-subtle px-3 py-2 sm:px-4">
      <span class="font-mono text-[0.72rem] tabular-nums text-text-disabled">
        {#if missingTrackCount > 0}
          {presentTracks.length} present · {missingTrackCount} missing · {tracks.length} total
        {:else}
          {tracks.length} {tracks.length === 1 ? "track" : "tracks"}
        {/if}
      </span>
      {#if totalDuration > 0}
        <span class="font-mono text-[0.72rem] tabular-nums text-text-disabled">
          {formatTotalDuration(totalDuration)}
        </span>
      {/if}
    </div>
  {/if}
</div>

<style>
  .track-header {
    grid-template-columns: 2rem minmax(0, 1fr) auto 3rem 2rem;
  }

  .track-header.has-selection {
    grid-template-columns: 1.35rem 2rem minmax(0, 1fr) auto 3rem 2rem;
  }

  .track-header.has-artwork {
    grid-template-columns: 2rem 2.75rem minmax(0, 1fr) auto 3rem 2rem;
  }

  .track-header.has-selection.has-artwork {
    grid-template-columns: 1.35rem 2rem 2.75rem minmax(0, 1fr) auto 3rem 2rem;
  }

  .track-select-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    border-bottom: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgb(255 255 255 / 0.015);
    padding: 0.55rem 0.75rem;
  }

  .track-select-all {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .track-select-count {
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-variant-numeric: tabular-nums;
  }
</style>
