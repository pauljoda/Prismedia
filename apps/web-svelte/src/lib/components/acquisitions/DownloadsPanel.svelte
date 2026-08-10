<script lang="ts">
  import { onMount } from "svelte";
  import { SvelteMap } from "svelte/reactivity";
  import { ExternalLink, HardDriveDownload, Trash2 } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import type { DownloadQueueItemView, EntityThumbnail } from "$lib/api/generated/model";
  import { deleteAcquisition, fetchDownloadQueue, reSearchAcquisition } from "$lib/api/acquisitions";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import EntityThumbnailView from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import DownloadManagerTable from "$lib/components/acquisitions/DownloadManagerTable.svelte";
  import { downloadToListItem } from "$lib/requests/acquisition-list-item";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import { createSerializedRefresh } from "$lib/async/serialized-refresh";
  import type { DownloadManagerEntry } from "./download-tree";

  /**
   * The global Downloads workbench. Active acquisitions are grouped through their real Entity ancestry
   * in a tree table; the selected transfer owns a persistent inspector beneath the queue.
   */
  let rows = $state.raw<DownloadQueueItemView[]>([]);
  let thumbnails = $state.raw<Map<string, EntityThumbnail>>(new Map());
  let loading = $state(true);
  let error = $state<string | null>(null);
  let acting = $state(false);
  let selectedId = $state<string | null>(null);
  let thumbnailEntityKey = "";

  const ACTIVE_POLL_INTERVAL_MS = 4_000;
  const IDLE_POLL_INTERVAL_MS = 15_000;
  const MAX_ENTITY_TREE_DEPTH = 8;

  let pendingRemoveIds = $state<string[]>([]);
  let confirmOpen = $state(false);

  const callbacks = {
    onReSearch: (row: DownloadQueueItemView) => void reSearchOne(row.acquisitionId),
    onRemove: (row: DownloadQueueItemView) => requestRemove([row.acquisitionId]),
  };

  const entries = $derived<DownloadManagerEntry[]>(rows.map((row) => ({
    row,
    item: downloadToListItem(
      row,
      row.entityId ? thumbnails.get(row.entityId) ?? null : null,
      callbacks,
      acting,
    ),
  })));
  const selectedEntry = $derived(entries.find((entry) => entry.item.id === selectedId) ?? null);

  /** Fetches the queue's Entities, then walks parent ids until every available ancestor is resolved. */
  async function fetchThumbnailHierarchy(entityIds: string[]): Promise<Map<string, EntityThumbnail>> {
    const resolved = new SvelteMap<string, EntityThumbnail>();
    let pending = [...new Set(entityIds)];
    for (let depth = 0; depth < MAX_ENTITY_TREE_DEPTH && pending.length > 0; depth += 1) {
      const fetched = await fetchEntityThumbnails(pending);
      fetched.forEach((thumbnail) => resolved.set(thumbnail.id, thumbnail));
      pending = [...new Set(fetched
        .map((thumbnail) => thumbnail.parentEntityId)
        .filter((id): id is string => !!id && !resolved.has(id)))];
    }
    return resolved;
  }

  async function loadOnce() {
    try {
      const nextRows = await fetchDownloadQueue();
      rows = nextRows;
      if (selectedId && !nextRows.some((row) => row.acquisitionId === selectedId)) {
        selectedId = null;
      }
      error = null;
      loading = false;

      const ids = [...new Set(nextRows.map((row) => row.entityId).filter((id): id is string => !!id))].sort();
      const nextThumbnailEntityKey = ids.join("\u0000");
      if (nextThumbnailEntityKey !== thumbnailEntityKey) {
        try {
          thumbnails = await fetchThumbnailHierarchy(ids);
          thumbnailEntityKey = nextThumbnailEntityKey;
        } catch {
          // The transfer list remains usable without artwork/hierarchy; the next queue poll retries it.
        }
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load downloads";
    } finally {
      loading = false;
    }
  }

  // Focus, visibility, queue polling, and detail mutations share one serialized refresh lane.
  const load = createSerializedRefresh(loadOnce);

  async function reSearchOne(id: string) {
    acting = true;
    try {
      await reSearchAcquisition(id);
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to re-search";
    } finally {
      acting = false;
    }
  }

  function requestRemove(ids: string[]) {
    pendingRemoveIds = ids;
    confirmOpen = true;
  }

  async function removeSelected() {
    acting = true;
    const failures: string[] = [];
    try {
      // Each removal may touch a remote download client. Keep the established sequential safety bound.
      for (const id of pendingRemoveIds) {
        try {
          await deleteAcquisition(id);
        } catch (reason) {
          const message = reason instanceof Error ? reason.message : "Failed to remove download";
          failures.push(`${id}: ${message}`);
        }
      }
      await load();
      if (failures.length > 0) {
        error = `Removed ${pendingRemoveIds.length - failures.length} of ${pendingRemoveIds.length} downloads. ${failures.join("; ")}`;
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove downloads";
    } finally {
      acting = false;
      pendingRemoveIds = [];
    }
  }

  const pollIntervalMs = $derived(
    rows.some((row) => acquisitionStatusShouldPoll(row.status))
      ? ACTIVE_POLL_INTERVAL_MS
      : IDLE_POLL_INTERVAL_MS,
  );

  $effect(() => {
    const timer = setInterval(load, pollIntervalMs);
    return () => clearInterval(timer);
  });

  onMount(load);

  function refreshWhenVisible() {
    if (document.visibilityState === "visible") void load();
  }

  function openSelectedEntity() {
    if (selectedEntry?.item.href) window.location.assign(selectedEntry.item.href);
  }

  const removeCount = $derived(pendingRemoveIds.length);
</script>

<svelte:window onfocus={() => void load()} />
<svelte:document onvisibilitychange={refreshWhenVisible} />

<div class="downloads-workbench">
  <DownloadManagerTable
    {entries}
    {thumbnails}
    {loading}
    {error}
    {acting}
    {selectedId}
    onSelect={(id) => (selectedId = id)}
    onRemove={requestRemove}
  />

  <section class="download-inspector" aria-label="Selected download details">
    {#if selectedEntry}
      <header class="inspector-header">
        <div class="inspector-identity">
          <span class="inspector-artwork">
            <EntityThumbnailView
              card={{ ...selectedEntry.item.thumbnail, aspectRatio: "square" }}
              mediaOnly
              interactive={false}
              hoverPreviewsEnabled={false}
              showWantedBadge={false}
              artworkReactive
              imageFetchPriority="high"
            />
          </span>
          <span class="inspector-copy">
            <span class="inspector-kicker">Selected transfer</span>
            <strong>{selectedEntry.item.title}</strong>
            {#if selectedEntry.item.subtitle}<span>{selectedEntry.item.subtitle}</span>{/if}
          </span>
          <Badge variant={selectedEntry.item.tone === "failed" ? "error" : selectedEntry.item.tone === "attention" ? "warning" : "accent"}>
            {selectedEntry.item.statusLabel}
          </Badge>
        </div>
        <div class="inspector-actions">
          {#if selectedEntry.item.href}
            <Button variant="secondary" size="sm" onclick={openSelectedEntity}>
              <ExternalLink class="h-3.5 w-3.5" /> Open entity
            </Button>
          {/if}
          {#if selectedEntry.item.selectable !== false}
            <Button variant="danger" size="sm" disabled={acting} onclick={() => requestRemove([selectedEntry.item.id])}>
              <Trash2 class="h-3.5 w-3.5" /> Remove
            </Button>
          {/if}
        </div>
      </header>

      <div class="inspector-detail">
        {#key selectedEntry.item.id}
          <AcquisitionPanel
            acquisitionId={selectedEntry.item.id}
            onCancelled={() => load()}
            onImported={() => load()}
            onReset={() => load()}
          />
        {/key}
      </div>
    {:else if loading}
      <div class="inspector-empty">
        <HardDriveDownload class="h-6 w-6" />
        <strong>Loading transfer details…</strong>
      </div>
    {:else}
      <div class="inspector-empty">
        <HardDriveDownload class="h-6 w-6" />
        <strong>No transfer selected</strong>
        <span>Select a download row to inspect its controls, files, peers, and history.</span>
      </div>
    {/if}
  </section>
</div>

<ConfirmDialog
  open={confirmOpen}
  title={`Remove ${removeCount} download${removeCount === 1 ? "" : "s"}?`}
  message={`This removes the selected ${removeCount === 1 ? "download" : "downloads"}, deletes associated transfer data when reachable, and clears any interrupted import state and partial files. Monitored items stay Wanted and start again with a clean search; use Unmonitor or Remove wanted when you mean to stop tracking them.`}
  confirmLabel="Remove"
  danger
  onConfirm={removeSelected}
  onClose={() => {
    confirmOpen = false;
    pendingRemoveIds = [];
  }}
/>

<style>
  .downloads-workbench {
    display: grid;
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    grid-template-rows: minmax(13rem, 3fr) minmax(12rem, 2fr);
  }
  .download-inspector {
    display: flex;
    min-height: 0;
    flex-direction: column;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-top-color: var(--color-border-default);
    border-radius: 0 0 var(--radius-md, 8px) var(--radius-md, 8px);
    background:
      radial-gradient(circle at 0 0, rgb(255 255 255 / 0.025), transparent 26rem),
      rgb(8 8 9 / 0.94);
    box-shadow: 0 18px 40px rgb(0 0 0 / 0.22);
  }
  .inspector-header { display: flex; min-height: 4.8rem; align-items: center; justify-content: space-between; gap: 1rem; padding: 0.7rem 0.9rem; border-bottom: 1px solid var(--color-border-subtle); background: rgb(255 255 255 / 0.012); }
  .inspector-identity { display: flex; min-width: 0; align-items: center; gap: 0.7rem; }
  .inspector-artwork { flex: 0 0 3.2rem; width: 3.2rem; overflow: hidden; border-radius: var(--radius-xs, 4px); box-shadow: 0 0 0 1px var(--color-border-default), 0 7px 18px rgb(0 0 0 / 0.32); }
  .inspector-artwork :global(.media) { border: 0; border-radius: var(--radius-xs, 4px); box-shadow: none; }
  .inspector-copy { display: flex; min-width: 0; flex-direction: column; gap: 0.08rem; }
  .inspector-copy strong { overflow: hidden; color: var(--color-text-primary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 1rem; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
  .inspector-copy > span:last-child { overflow: hidden; color: var(--color-text-muted); font-size: 0.7rem; text-overflow: ellipsis; white-space: nowrap; }
  .inspector-kicker { color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.58rem !important; letter-spacing: 0.1em; text-transform: uppercase; }
  .inspector-actions { display: flex; flex: 0 0 auto; align-items: center; gap: 0.45rem; }
  .inspector-detail { min-width: 0; min-height: 0; flex: 1 1 auto; overflow: auto; padding: 1rem; }
  .inspector-empty { display: flex; min-height: 0; flex: 1 1 auto; align-items: center; justify-content: center; flex-direction: column; gap: 0.4rem; color: var(--color-text-muted); text-align: center; }
  .inspector-empty strong { color: var(--color-text-secondary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 0.9rem; }
  .inspector-empty span { max-width: 28rem; font-size: 0.72rem; }

  @media (max-width: 640px) {
    .download-inspector { border-radius: 0 0 var(--radius-sm, 6px) var(--radius-sm, 6px); }
    .inspector-header { align-items: flex-start; flex-direction: column; }
    .inspector-actions { width: 100%; }
    .inspector-detail { padding: 0.75rem; }
  }
</style>
