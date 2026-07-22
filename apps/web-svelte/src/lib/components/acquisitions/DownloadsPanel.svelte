<script lang="ts">
  import { onMount } from "svelte";
  import { HardDriveDownload } from "@lucide/svelte";
  import type { DownloadQueueItemView, EntityThumbnail } from "$lib/api/generated/model";
  import { deleteAcquisition, fetchDownloadQueue, reSearchAcquisition } from "$lib/api/acquisitions";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import AcquisitionListShell, {
    type AcquisitionStatusFilter,
  } from "$lib/components/acquisitions/AcquisitionListShell.svelte";
  import {
    downloadToListItem,
    Trash2,
    type AcquisitionBulkAction,
    type AcquisitionListItem,
  } from "$lib/requests/acquisition-list-item";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import { createSerializedRefresh } from "$lib/async/serialized-refresh";

  /**
   * The global Downloads view: every active acquisition across all kinds as a shared card list, with
   * live client telemetry, poster artwork, status/kind filters, and per-row + bulk actions. Rows link to
   * the item's own library page, where the acquisition card carries the full management surface.
   */

  let rows = $state.raw<DownloadQueueItemView[]>([]);
  let thumbs = $state.raw<Map<string, EntityThumbnail>>(new Map());
  let loading = $state(true);
  let error = $state<string | null>(null);
  let acting = $state(false);

  let thumbnailEntityKey = "";

  const ACTIVE_POLL_INTERVAL_MS = 4_000;
  const IDLE_POLL_INTERVAL_MS = 15_000;

  let pendingRemoveIds = $state<string[]>([]);
  let confirmOpen = $state(false);

  const callbacks = {
    onReSearch: (row: DownloadQueueItemView) => void reSearchOne(row.acquisitionId),
    onRemove: (row: DownloadQueueItemView) => {
      pendingRemoveIds = [row.acquisitionId];
      confirmOpen = true;
    },
  };

  const items = $derived<AcquisitionListItem[]>(
    rows.map((row) => downloadToListItem(row, row.entityId ? thumbs.get(row.entityId) ?? null : null, callbacks, acting)),
  );

  // Status pills over the presentation tone, so the labels stay meaningful without leaking status codes.
  const statusFilters: AcquisitionStatusFilter[] = [
    { value: "downloading", label: "Downloading", match: (item) => item.tone === "downloading" },
    { value: "searching", label: "Searching", match: (item) => item.tone === "searching" },
    { value: "cleanup", label: "Cleaning up", match: (item) => item.tone === "cleanup" },
    { value: "attention", label: "Needs attention", match: (item) => item.tone === "attention" },
  ];

  const bulkActions: AcquisitionBulkAction[] = [
    {
      id: "remove",
      label: "Remove",
      icon: Trash2,
      tone: "danger",
      run: (ids) => {
        pendingRemoveIds = ids;
        confirmOpen = true;
      },
    },
  ];

  async function loadOnce() {
    try {
      const nextRows = await fetchDownloadQueue();
      rows = nextRows;
      error = null;
      loading = false;

      // Artwork and entity hierarchy metadata do not change with transfer telemetry. Reuse the resolved
      // batch while its entity membership is stable so the four-second progress poll remains cheap.
      const ids = [...new Set(nextRows.map((row) => row.entityId).filter((id): id is string => !!id))].sort();
      const nextThumbnailEntityKey = ids.join("\u0000");
      if (nextThumbnailEntityKey !== thumbnailEntityKey) {
        try {
          const fetched = await fetchEntityThumbnails(ids);
          thumbs = new Map(fetched.map((thumbnail) => [thumbnail.id, thumbnail]));
          thumbnailEntityKey = nextThumbnailEntityKey;
        } catch {
          // Keep the rows and last-known artwork usable; the next poll retries this auxiliary batch.
        }
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load downloads";
    } finally {
      loading = false;
    }
  }

  // Polls, focus, and visibility can all request a refresh at once. Keep one network/database pass in
  // flight and collapse every overlapping request into one trailing refresh instead of letting slow
  // thumbnail projections accumulate indefinitely.
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

  async function removeSelected() {
    acting = true;
    const failures: string[] = [];
    try {
      // Sequentially: each removal best-effort deletes the client transfer, so don't flood the client.
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

  // Attention states can still resolve outside this panel when a scan reconciles a downloaded file
  // to its provider Entity. Keep a slower idle poll so those rows disappear without requiring a reload.
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

  const removeCount = $derived(pendingRemoveIds.length);
</script>

<svelte:window onfocus={() => void load()} />
<svelte:document onvisibilitychange={refreshWhenVisible} />

<AcquisitionListShell
  {items}
  {loading}
  {error}
  {statusFilters}
  {bulkActions}
  kindChips
  countNoun="download"
  emptyTitle="Nothing downloading"
  emptyMessage="Request something from the Discover tab and its download will appear here."
>
  {#snippet emptyIcon()}
    <HardDriveDownload class="h-7 w-7 text-text-disabled" />
  {/snippet}
</AcquisitionListShell>

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
