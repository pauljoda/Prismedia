<script lang="ts">
  import { onMount, onDestroy } from "svelte";
  import { Loader2 } from "@lucide/svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { fetchAcquisitions } from "$lib/api/acquisitions";
  import { fetchRequestHistory } from "$lib/api/requests";
  import {
    ACTIVE_ACQUISITION_STATUSES,
    acquisitionToThumbnailCard,
    requestHistoryToThumbnailCard,
  } from "$lib/requests/review-cards";
  import type { AcquisitionSummary } from "$lib/api/generated/model";

  let acquisitions = $state<AcquisitionSummary[]>([]);
  let cards = $state<EntityThumbnailCard[]>([]);
  let warnings = $state<string[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  async function load() {
    try {
      const [acq, history] = await Promise.all([fetchAcquisitions(), fetchRequestHistory()]);
      acquisitions = acq;
      warnings = history.providerErrors.map((item) => `${item.displayName}: ${item.message}`);
      // Acquisitions first (most actionable), then *arr request history — one unified review grid.
      cards = [
        ...acq.map(acquisitionToThumbnailCard),
        ...history.entries.map(requestHistoryToThumbnailCard),
      ];
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load requests";
    } finally {
      loading = false;
    }
  }

  // Poll while any acquisition is mid-flight so status overlays update live.
  $effect(() => {
    const active = acquisitions.some((a) => ACTIVE_ACQUISITION_STATUSES.includes(a.status));
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

{#if loading && cards.length === 0}
  <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
    <Loader2 class="h-4 w-4 animate-spin" />
    <span class="text-sm">Loading requests…</span>
  </div>
{:else}
  <EntityGrid
    cards={cards}
    entityKind="request"
    prefsKey="request-review"
    selectable={false}
    emptyTitle="No requests yet"
    emptyMessage="Request a book or other media from the Discover tab to see it here."
  />
{/if}
