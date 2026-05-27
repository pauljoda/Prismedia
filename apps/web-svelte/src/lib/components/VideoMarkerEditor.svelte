<script lang="ts">
  import { Plus, Pencil, Trash2 } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import {
    createEntityMarker,
    updateEntityMarker,
    deleteEntityMarker,
  } from "$lib/api/entity-mutations";
  import type { EntityDetailMarker } from "$lib/entities/entity-detail";
  import TimeMarkerForm, { formatSecondsInput } from "./TimeMarkerForm.svelte";

  interface Props {
    entityId: string;
    markers: EntityDetailMarker[];
    /** A reactive getter for the current playback time so we can sync now-buttons. */
    getCurrentTime: () => number;
    displayTime: number;
    onSeek?: (seconds: number) => void;
    onRefresh: () => void | Promise<void>;
  }

  let {
    entityId,
    markers,
    getCurrentTime,
    displayTime,
    onSeek,
    onRefresh,
  }: Props = $props();

  let editingMarker = $state<string | null>(null);
  let markerTitle = $state("");
  let markerSeconds = $state(0);
  let markerEndSeconds = $state<number | null>(null);
  let savingMarker = $state(false);

  function startNewMarker() {
    editingMarker = "new";
    markerTitle = "";
    markerSeconds = Math.floor(getCurrentTime());
    markerEndSeconds = null;
  }

  function startEditMarker(m: EntityDetailMarker) {
    editingMarker = m.id;
    markerTitle = m.title;
    markerSeconds = m.seconds;
    markerEndSeconds = m.endSeconds;
  }

  function cancelEdit() {
    editingMarker = null;
  }

  async function handleSave(payload: { seconds: number; endSeconds: number | null }) {
    if (!markerTitle.trim()) return;
    savingMarker = true;
    try {
      if (editingMarker === "new") {
        await createEntityMarker(entityId, {
          title: markerTitle.trim(),
          seconds: payload.seconds,
          endSeconds: payload.endSeconds,
        });
      } else if (editingMarker) {
        await updateEntityMarker(entityId, editingMarker, {
          title: markerTitle.trim(),
          seconds: payload.seconds,
          endSeconds: payload.endSeconds,
        });
      }
      editingMarker = null;
      await onRefresh();
    } catch {
      // silent
    } finally {
      savingMarker = false;
    }
  }

  async function handleDeleteMarker(markerId: string) {
    try {
      await deleteEntityMarker(entityId, markerId);
      await onRefresh();
    } catch {
      // silent
    }
  }
</script>

<div class="space-y-3">
  {#if editingMarker !== "new"}
    <Button variant="secondary" size="sm" onclick={startNewMarker}>
      <Plus class="h-3.5 w-3.5" />
      Add Marker at {formatSecondsInput(Math.floor(displayTime))}
    </Button>
  {/if}

  {#if editingMarker === "new"}
    <TimeMarkerForm
      title={markerTitle}
      seconds={markerSeconds}
      endSeconds={markerEndSeconds}
      saving={savingMarker}
      onTitleChange={(v) => (markerTitle = v)}
      onSecondsChange={(v) => (markerSeconds = v)}
      onEndSecondsChange={(v) => (markerEndSeconds = v)}
      onSetCurrentTime={() => (markerSeconds = Math.floor(getCurrentTime()))}
      onSetCurrentEndTime={() => (markerEndSeconds = Math.floor(getCurrentTime()))}
      onSave={handleSave}
      onCancel={cancelEdit}
    />
  {/if}

  {#if markers.length === 0 && editingMarker !== "new"}
    <div class="surface-well p-8 text-center">
      <p class="text-text-muted text-sm">No markers yet</p>
    </div>
  {/if}

  {#each markers as marker (marker.id)}
    {#if editingMarker === marker.id}
      <TimeMarkerForm
        title={markerTitle}
        seconds={markerSeconds}
        endSeconds={markerEndSeconds}
        saving={savingMarker}
        onTitleChange={(v) => (markerTitle = v)}
        onSecondsChange={(v) => (markerSeconds = v)}
        onEndSecondsChange={(v) => (markerEndSeconds = v)}
        onSetCurrentTime={() => (markerSeconds = Math.floor(getCurrentTime()))}
        onSetCurrentEndTime={() => (markerEndSeconds = Math.floor(getCurrentTime()))}
        onSave={handleSave}
        onCancel={cancelEdit}
      />
    {:else}
      {@const startMin = Math.floor(marker.seconds / 60)}
      {@const startSec = Math.floor(marker.seconds % 60)}
      {@const timeStr = `${startMin}:${String(startSec).padStart(2, "0")}`}
      {@const endMin = marker.endSeconds ? Math.floor(marker.endSeconds / 60) : 0}
      {@const endSec = marker.endSeconds ? Math.floor(marker.endSeconds % 60) : 0}
      {@const endStr = marker.endSeconds
        ? ` \u2192 ${endMin}:${String(endSec).padStart(2, "0")}`
        : ""}
      <div class="surface-card-sharp flex items-center gap-4 p-3 group">
        <span class="text-mono-tabular text-accent-400 w-24 flex-shrink-0">
          {timeStr}{#if endStr}<span class="text-text-disabled">{endStr}</span>{/if}
        </span>
        <button
          type="button"
          class="flex-1 min-w-0 text-left"
          onclick={() => onSeek?.(marker.seconds)}
          aria-label={`Seek to ${marker.title}`}
        >
          <span class="block text-sm font-medium truncate">{marker.title}</span>
        </button>
        <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0">
          <button
            type="button"
            onclick={() => startEditMarker(marker)}
            class="p-1 text-text-muted hover:text-text-accent transition-colors"
            aria-label="Edit marker"
          >
            <Pencil class="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onclick={() => void handleDeleteMarker(marker.id)}
            class="p-1 text-text-muted hover:text-error-text transition-colors"
            aria-label="Delete marker"
          >
            <Trash2 class="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    {/if}
  {/each}
</div>
