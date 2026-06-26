<script lang="ts">
  import { onMount, onDestroy, type Component } from "svelte";
  import { AlertTriangle, CheckCircle2, Inbox, ListChecks, Loader2, LoaderCircle, Trash2, X } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { deleteAcquisition, fetchAcquisitions } from "$lib/api/acquisitions";
  import { fetchRequestHistory } from "$lib/api/requests";
  import { REQUEST_MEDIA_KIND, type RequestMediaKindCode } from "$lib/api/generated/codes";
  import { REQUEST_KIND_LABELS_PLURAL } from "$lib/requests/request-helpers";
  import {
    ACTIVE_ACQUISITION_STATUSES,
    REVIEW_GROUP_LABELS,
    REVIEW_GROUP_ORDER,
    acquisitionToReviewItem,
    requestHistoryToReviewItem,
    type ReviewGroup,
    type ReviewItem,
  } from "$lib/requests/review-cards";
  import type { AcquisitionSummary } from "$lib/api/generated/model";

  let acquisitions = $state<AcquisitionSummary[]>([]);
  let items = $state<ReviewItem[]>([]);
  let warnings = $state<string[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  let selectMode = $state(false);
  let selectedIds = $state<string[]>([]);
  let confirmOpen = $state(false);
  let selectedKind = $state<RequestMediaKindCode | "all">("all");

  const GROUP_ICONS: Record<ReviewGroup, Component<{ class?: string }>> = {
    action: AlertTriangle,
    progress: LoaderCircle,
    done: CheckCircle2,
  };

  const KIND_ORDER: RequestMediaKindCode[] = [
    REQUEST_MEDIA_KIND.book,
    REQUEST_MEDIA_KIND.movie,
    REQUEST_MEDIA_KIND.series,
    REQUEST_MEDIA_KIND.artist,
    REQUEST_MEDIA_KIND.album,
    REQUEST_MEDIA_KIND.plugin,
  ];

  const removableIds = $derived(new Set(items.filter((item) => item.removable).map((item) => item.id)));
  const hasRemovable = $derived(removableIds.size > 0);

  const availableKinds = $derived(
    [...new Set(items.map((item) => item.kind))].sort((a, b) => KIND_ORDER.indexOf(a) - KIND_ORDER.indexOf(b)),
  );
  const filteredItems = $derived(
    selectedKind === "all" ? items : items.filter((item) => item.kind === selectedKind),
  );
  // Action group floats to top, then in-progress, then done. Date-added descending within each.
  const groups = $derived(
    REVIEW_GROUP_ORDER
      .map((group) => ({
        group,
        items: filteredItems
          .filter((item) => item.group === group)
          .sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
      }))
      .filter((section) => section.items.length > 0),
  );
  const selectedCount = $derived(selectedIds.length);

  function isSelected(id: string): boolean {
    return selectedIds.includes(id);
  }
  function setSelected(id: string, on: boolean) {
    selectedIds = on ? [...new Set([...selectedIds, id])] : selectedIds.filter((value) => value !== id);
  }
  function clearSelection() {
    selectedIds = [];
  }
  function toggleSelectMode() {
    selectMode = !selectMode;
    if (!selectMode) clearSelection();
  }

  async function load() {
    try {
      const [acq, history] = await Promise.all([fetchAcquisitions(), fetchRequestHistory()]);
      acquisitions = acq;
      warnings = history.providerErrors.map((item) => `${item.displayName}: ${item.message}`);
      items = [...acq.map(acquisitionToReviewItem), ...history.entries.map(requestHistoryToReviewItem)];
      // Drop any selected ids that no longer exist.
      const present = new Set(items.map((item) => item.id));
      selectedIds = selectedIds.filter((id) => present.has(id));
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load requests";
    } finally {
      loading = false;
    }
  }

  async function removeSelected() {
    const ids = selectedIds.filter((id) => removableIds.has(id));
    await Promise.all(ids.map((id) => deleteAcquisition(id)));
    clearSelection();
    selectMode = false;
    await load();
  }

  // Poll while any acquisition is mid-flight so statuses and grouping update live.
  $effect(() => {
    const active = acquisitions.some((item) => ACTIVE_ACQUISITION_STATUSES.includes(item.status));
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
</script>

{#if error}
  <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
{/if}
{#each warnings as warning (warning)}
  <div class="surface-panel border-l-2 border-warning px-4 py-2.5 text-sm text-warning-text">{warning}</div>
{/each}

{#if loading && items.length === 0}
  <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
    <Loader2 class="h-4 w-4 animate-spin" />
    <span class="text-sm">Loading requests…</span>
  </div>
{:else if items.length === 0}
  <div class="empty-rack-slot grid place-items-center gap-2 p-10 text-center">
    <Inbox class="h-7 w-7 text-text-disabled" />
    <p class="text-sm font-medium text-text-primary">No requests yet</p>
    <p class="text-[0.8rem] text-text-muted">Request a book or other media from the Discover tab to see it here.</p>
  </div>
{:else}
  <!-- ── Controls: kind filter + selection toggle ── -->
  <div class="flex flex-wrap items-center justify-between gap-2">
    <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by kind">
      {#if availableKinds.length > 1}
        {#each [{ value: "all", label: "All" }, ...availableKinds.map((kind) => ({ value: kind, label: REQUEST_KIND_LABELS_PLURAL[kind] ?? kind }))] as option (option.value)}
          <button
            type="button"
            onclick={() => (selectedKind = option.value as RequestMediaKindCode | "all")}
            class={cn(
              "rounded-xs border px-2.5 py-1 text-[0.72rem] font-medium transition-all duration-fast",
              selectedKind === option.value
                ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                : "bg-surface-1 border-border-subtle text-text-muted hover:border-border-default hover:text-text-primary",
            )}
          >
            {option.label}
          </button>
        {/each}
      {/if}
    </div>

    {#if hasRemovable}
      <Button type="button" variant={selectMode ? "primary" : "secondary"} size="sm" onclick={toggleSelectMode} class="gap-1.5">
        {#if selectMode}<X class="h-3.5 w-3.5" />Done{:else}<ListChecks class="h-3.5 w-3.5" />Select{/if}
      </Button>
    {/if}
  </div>

  <!-- ── Grouped, collapsible review sections ── -->
  <div class="mt-4 space-y-6 {selectMode && selectedCount > 0 ? 'pb-20' : ''}">
    {#each groups as section (section.group)}
      <EntityGridSection
        title={REVIEW_GROUP_LABELS[section.group]}
        icon={GROUP_ICONS[section.group]}
        count={section.items.length}
        prefsKey={`request-review-${section.group}`}
      >
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
          {#each section.items as item (item.id)}
            <EntityThumbnail
              card={item.card}
              selectable={item.removable}
              selectMode={selectMode && item.removable}
              selected={isSelected(item.id)}
              onSelectedChange={(value) => setSelected(item.id, value)}
            />
          {/each}
        </div>
      </EntityGridSection>
    {/each}
  </div>
{/if}

<!-- ── Floating bulk action bar ── -->
{#if selectMode && selectedCount > 0}
  <div class="selection-bar">
    <span class="font-mono text-[0.78rem] text-text-secondary">{selectedCount} selected</span>
    <div class="flex items-center gap-2">
      <Button type="button" variant="ghost" size="sm" onclick={clearSelection}>Clear</Button>
      <Button type="button" variant="danger" size="sm" onclick={() => (confirmOpen = true)} class="gap-1.5">
        <Trash2 class="h-3.5 w-3.5" />
        Remove
      </Button>
    </div>
  </div>
{/if}

<ConfirmDialog
  open={confirmOpen}
  title={`Remove ${selectedCount} request${selectedCount === 1 ? "" : "s"}?`}
  message={`This removes the selected ${selectedCount === 1 ? "request" : "requests"} and deletes ${selectedCount === 1 ? "its" : "their"} download and any downloaded data from the download client. This can't be undone.`}
  confirmLabel="Remove"
  danger
  onConfirm={removeSelected}
  onClose={() => (confirmOpen = false)}
/>

<style>
  .selection-bar {
    position: fixed;
    bottom: 1.25rem;
    left: 50%;
    z-index: 40;
    display: flex;
    align-items: center;
    gap: 1rem;
    transform: translateX(-50%);
    padding: 0.55rem 0.6rem 0.55rem 1rem;
    border: 1px solid var(--color-border-accent, rgb(242 194 106 / 0.32));
    border-radius: var(--radius-full, 999px);
    background: color-mix(in srgb, var(--color-surface-1, #0c0f15) 88%, transparent);
    box-shadow: var(--shadow-panel), var(--shadow-glow-accent);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
  }
</style>
