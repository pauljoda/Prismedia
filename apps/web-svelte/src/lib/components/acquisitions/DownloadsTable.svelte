<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import { HardDriveDownload, Loader2, Search, X } from "@lucide/svelte";
  import { Badge, Button, Checkbox } from "@prismedia/ui-svelte";
  import type { DownloadQueueItemView } from "$lib/api/generated/model";
  import type { AcquisitionStatusCode } from "$lib/api/generated/codes";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import { deleteAcquisition, fetchDownloadQueue, reSearchAcquisition } from "$lib/api/acquisitions";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { labelForEntityKind, resolveEntityHref } from "$lib/entities/entity-codes";
  import { ACTIVE_ACQUISITION_STATUSES, acquisitionStatusLabel } from "$lib/requests/review-cards";
  import { formatBytes, formatEta, formatSpeed } from "$lib/utils/format";

  /**
   * The global Downloads view: every active acquisition across all kinds in one table, with live
   * client telemetry (progress, speed, ETA, client) on rows whose transfer is in flight. Rows link to
   * the entity's own page, where the acquisition card carries the full management surface (release
   * picker, piece map, cancel) — this table is the fleet overview, not a second manager.
   */

  let items = $state<DownloadQueueItemView[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let acting = $state(false);
  let selected = $state<Set<string>>(new Set());
  let pendingRemoveIds = $state<string[]>([]);
  let confirmOpen = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  const allSelected = $derived(items.length > 0 && items.every((item) => selected.has(item.acquisitionId)));
  const someSelected = $derived(selected.size > 0 && !allSelected);
  const removeCount = $derived(pendingRemoveIds.length);

  async function load() {
    try {
      const next = await fetchDownloadQueue();
      // Drop selections for rows that disappeared (imported or removed elsewhere).
      const ids = new Set(next.map((item) => item.acquisitionId));
      if ([...selected].some((id) => !ids.has(id))) {
        selected = new Set([...selected].filter((id) => ids.has(id)));
      }
      items = next;
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load downloads";
    } finally {
      loading = false;
    }
  }

  // Poll while anything is mid-flight so progress/speed stay live.
  $effect(() => {
    const active = items.some((item) => ACTIVE_ACQUISITION_STATUSES.includes(item.status as AcquisitionStatusCode));
    if (active && !pollTimer) {
      pollTimer = setInterval(load, 4000);
    } else if (!active && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });

  onMount(load);
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });

  function toggleRow(id: string) {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    selected = next;
  }

  function toggleAll() {
    selected = allSelected ? new Set() : new Set(items.map((item) => item.acquisitionId));
  }

  function entityHref(item: DownloadQueueItemView): string | undefined {
    return item.entityId ? resolveEntityHref(item.kind, item.entityId) : undefined;
  }

  /** Percent progress for the bar; null hides the bar (no transfer and no persisted progress). */
  function percentOf(item: DownloadQueueItemView): number | null {
    const progress = Number(item.progress);
    return Number.isFinite(progress) && item.progress != null ? Math.round(progress * 100) : null;
  }

  /** Re-search makes sense only while still seeking a release. */
  function canReSearch(item: DownloadQueueItemView): boolean {
    return (
      item.status === ACQUISITION_STATUS.awaitingSelection || item.status === ACQUISITION_STATUS.failed
    );
  }

  async function reSearchOne(item: DownloadQueueItemView) {
    acting = true;
    error = null;
    try {
      await reSearchAcquisition(item.acquisitionId);
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to re-search";
    } finally {
      acting = false;
    }
  }

  async function removeSelected() {
    acting = true;
    error = null;
    try {
      // Sequentially: each removal best-effort deletes the client torrent, so don't flood the client.
      for (const id of pendingRemoveIds) {
        await deleteAcquisition(id);
      }
      selected = new Set();
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove downloads";
    } finally {
      acting = false;
      pendingRemoveIds = [];
    }
  }
</script>

<div class="space-y-3">
  <!-- ── Toolbar ── -->
  <div class="flex flex-wrap items-center justify-between gap-2">
    <span class="flex items-center gap-2 text-label text-text-muted">
      <HardDriveDownload class="h-3.5 w-3.5" />
      {items.length} active
    </span>
    <div class="flex items-center gap-2">
      {#if selected.size > 0}
        <span class="font-mono text-[0.68rem] text-text-muted">{selected.size} selected</span>
      {/if}
      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={selected.size === 0 || acting}
        onclick={() => {
          pendingRemoveIds = [...selected];
          confirmOpen = true;
        }}
        class="gap-1.5 px-2.5 py-1 text-xs"
      >
        <X class="h-3.5 w-3.5" />
        Remove
      </Button>
    </div>
  </div>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
      {error}
    </div>
  {/if}

  {#if items.length > 0}
    <div class="overflow-hidden rounded-sm border border-border-subtle">
      <!-- Header row -->
      <div
        class="flex items-center gap-3 border-b border-border-subtle bg-surface-2/40 px-3 py-2 text-label text-text-muted"
      >
        <Checkbox
          checked={allSelected}
          indeterminate={someSelected}
          onchange={toggleAll}
          aria-label="Select all downloads"
        />
        <span class="flex-1">Item</span>
        <span class="hidden w-44 shrink-0 sm:block">Progress</span>
        <span class="hidden w-20 shrink-0 text-right md:block">Size</span>
        <span class="hidden w-24 shrink-0 lg:block">Client</span>
        <span class="w-14 shrink-0 text-right">Actions</span>
      </div>

      {#each items as item (item.acquisitionId)}
        {@const href = entityHref(item)}
        {@const percent = percentOf(item)}
        <div class="flex items-center gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0">
          <Checkbox
            checked={selected.has(item.acquisitionId)}
            onchange={() => toggleRow(item.acquisitionId)}
            aria-label={`Select ${item.title}`}
          />
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <div class="flex flex-wrap items-center gap-1.5">
              <Badge variant="default">{labelForEntityKind(item.kind)}</Badge>
              {#if href}
                <a
                  href={href}
                  class="truncate text-sm font-medium text-text-primary hover:text-text-accent"
                >
                  {item.title}
                </a>
              {:else}
                <span class="truncate text-sm font-medium text-text-primary">{item.title}</span>
              {/if}
            </div>
            <div class="flex flex-wrap items-center gap-1.5 text-[0.7rem] text-text-muted">
              <Badge variant="info">{acquisitionStatusLabel(item.status as AcquisitionStatusCode)}</Badge>
              {#if item.transferState}
                <span class="font-mono">{item.transferState}</span>
              {/if}
              {#if item.statusMessage && item.status === ACQUISITION_STATUS.failed}
                <span class="truncate text-error-text">{item.statusMessage}</span>
              {/if}
            </div>
          </div>

          <div class="hidden w-44 shrink-0 flex-col gap-1 sm:flex">
            {#if percent !== null}
              <div class="progress-track">
                <div class="progress-fill" style={`width: ${percent}%`}></div>
              </div>
              <div class="flex items-center justify-between font-mono text-[0.66rem] text-text-muted">
                <span>{percent}%</span>
                {#if item.downloadSpeedBytesPerSecond != null}
                  <span>{formatSpeed(Number(item.downloadSpeedBytesPerSecond))}</span>
                {/if}
                {#if item.etaSeconds != null}
                  <span>{formatEta(Number(item.etaSeconds))}</span>
                {/if}
              </div>
            {:else}
              <span class="font-mono text-[0.66rem] text-text-muted">—</span>
            {/if}
          </div>

          <span class="hidden w-20 shrink-0 text-right font-mono text-[0.7rem] text-text-muted md:block">
            {item.totalSizeBytes != null ? formatBytes(Number(item.totalSizeBytes)) : "—"}
          </span>

          <span class="hidden w-24 shrink-0 truncate font-mono text-[0.7rem] text-text-muted lg:block">
            {item.clientName ?? "—"}
          </span>

          <div class="flex w-14 shrink-0 items-center justify-end gap-1">
            {#if canReSearch(item)}
              <button
                type="button"
                onclick={() => void reSearchOne(item)}
                disabled={acting}
                class="rounded-xs p-1 text-text-muted transition-colors hover:text-text-accent disabled:opacity-40"
                title="Search again"
                aria-label={`Search again for ${item.title}`}
              >
                <Search class="h-3.5 w-3.5" />
              </button>
            {/if}
            <button
              type="button"
              onclick={() => {
                pendingRemoveIds = [item.acquisitionId];
                confirmOpen = true;
              }}
              disabled={acting}
              class="rounded-xs p-1 text-text-muted transition-colors hover:text-error-text disabled:opacity-40"
              title="Remove"
              aria-label={`Remove ${item.title}`}
            >
              <X class="h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      {/each}
    </div>
  {:else if loading}
    <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading downloads…</span>
    </div>
  {:else}
    <div class="empty-rack-slot grid place-items-center gap-2 p-10 text-center">
      <HardDriveDownload class="h-7 w-7 text-text-disabled" />
      <p class="text-sm font-medium text-text-primary">Nothing downloading</p>
      <p class="text-[0.8rem] text-text-muted">
        Request something from the Discover tab and its download will appear here.
      </p>
    </div>
  {/if}
</div>

<ConfirmDialog
  open={confirmOpen}
  title={`Remove ${removeCount} download${removeCount === 1 ? "" : "s"}?`}
  message={`This removes the selected ${removeCount === 1 ? "download" : "downloads"}, deletes any associated transfer and downloaded data from the download client, and removes the wanted placeholder. This can't be undone.`}
  confirmLabel="Remove"
  danger
  onConfirm={removeSelected}
  onClose={() => {
    confirmOpen = false;
    pendingRemoveIds = [];
  }}
/>

<style>
  .progress-track {
    height: 4px;
    border-radius: 2px;
    background: var(--color-surface-2);
    overflow: hidden;
  }

  .progress-fill {
    height: 100%;
    border-radius: 2px;
    background: linear-gradient(to right, var(--color-accent-overlay-strong), #f2c26a);
    box-shadow: 0 0 6px var(--color-accent-overlay-light);
    transition: width 600ms var(--ease-default, ease);
  }
</style>
