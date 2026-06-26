<script lang="ts">
  import { onMount, onDestroy, type Component } from "svelte";
  import { AlertTriangle, CheckCircle2, Inbox, Loader2, LoaderCircle, XCircle } from "@lucide/svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { deleteAcquisition, fetchAcquisitions } from "$lib/api/acquisitions";
  import { deleteRequestHistoryEntry, fetchRequestHistory } from "$lib/api/requests";
  import type { EntityGridBulkAction } from "$lib/entities/entity-grid";
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

  let pendingRemoveIds = $state<string[]>([]);
  let confirmOpen = $state(false);

  const GROUP_ICONS: Record<ReviewGroup, Component<{ class?: string }>> = {
    action: AlertTriangle,
    progress: LoaderCircle,
    completed: CheckCircle2,
    cancelled: XCircle,
  };

  const byId = $derived(new Map(items.map((item) => [item.id, item])));

  // Action group floats to the top, then in progress, completed, cancelled. Newest first within each
  // group (EntityGrid's "added" sort preserves the order cards arrive in).
  const groups = $derived(
    REVIEW_GROUP_ORDER
      .map((group) => ({
        group,
        cards: items
          .filter((item) => item.group === group)
          .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
          .map((item) => item.card),
      }))
      .filter((section) => section.cards.length > 0),
  );

  const removeCount = $derived(pendingRemoveIds.length);

  // Shared bulk action wired into each section's EntityGrid; selection + the action bar are the grid's own.
  const bulkActions: EntityGridBulkAction[] = [
    {
      id: "remove",
      label: "Remove",
      tone: "danger",
      onRun: (ids) => {
        pendingRemoveIds = ids;
        confirmOpen = true;
      },
    },
  ];

  async function load() {
    try {
      const [acq, history] = await Promise.all([fetchAcquisitions(), fetchRequestHistory()]);
      acquisitions = acq;
      warnings = history.providerErrors.map((item) => `${item.displayName}: ${item.message}`);
      items = [...acq.map(acquisitionToReviewItem), ...history.entries.map(requestHistoryToReviewItem)];
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load requests";
    } finally {
      loading = false;
    }
  }

  async function removeSelected() {
    await Promise.all(
      pendingRemoveIds.map((id) =>
        byId.get(id)?.type === "history" ? deleteRequestHistoryEntry(id) : deleteAcquisition(id),
      ),
    );
    pendingRemoveIds = [];
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
  <div class="space-y-6">
    {#each groups as section (section.group)}
      <EntityGridSection
        title={REVIEW_GROUP_LABELS[section.group]}
        icon={GROUP_ICONS[section.group]}
        count={section.cards.length}
        prefsKey={`request-review-${section.group}`}
      >
        <EntityGrid
          cards={section.cards}
          entityKind="request"
          prefsKey={`request-review-grid-${section.group}`}
          selectable
          {bulkActions}
          bulkLibraryActions={false}
          dockControls={false}
          initialSortBy="added"
          initialSortDir="desc"
          emptyTitle="Nothing here"
          emptyMessage="No requests in this group."
        />
      </EntityGridSection>
    {/each}
  </div>
{/if}

<ConfirmDialog
  open={confirmOpen}
  title={`Remove ${removeCount} request${removeCount === 1 ? "" : "s"}?`}
  message={`This removes the selected ${removeCount === 1 ? "request" : "requests"} and deletes any associated download and downloaded data from the download client. This can't be undone.`}
  confirmLabel="Remove"
  danger
  onConfirm={removeSelected}
  onClose={() => {
    confirmOpen = false;
    pendingRemoveIds = [];
  }}
/>
