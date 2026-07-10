<script lang="ts">
  import { BellOff, ChevronLeft, ChevronRight, PackageSearch, RotateCw } from "@lucide/svelte";
  import { Button, Select } from "@prismedia/ui-svelte";
  import type { SelectOption } from "@prismedia/ui-svelte";
  import type { EntityThumbnail, WantedListItemView } from "$lib/api/generated/model";
  import type { WantedListParams } from "$lib/api/monitors";
  import { fetchCutoffUnmetWanted, fetchMissingWanted, stopMonitor } from "$lib/api/monitors";
  import { reSearchAcquisition } from "$lib/api/acquisitions";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import AcquisitionListShell from "$lib/components/acquisitions/AcquisitionListShell.svelte";
  import {
    wantedToListItem,
    type AcquisitionBulkAction,
    type AcquisitionListItem,
  } from "$lib/requests/acquisition-list-item";

  /**
   * One Wanted list surface (Missing or Cutoff Unmet), rendered through the shared acquisition card list
   * so it matches the Downloads view — poster artwork, status, quality gap, search cadence, and per-row
   * Search-now / Unmonitor, plus bulk actions. Server-side kind filtering and pagination stay in this
   * component (the data can be large) and are injected into the shell's filter/footer slots; the shell's
   * own text search refines the loaded page. Reused for both lists via `variant`.
   */
  let {
    variant,
    kindOptions,
  }: {
    /** Which Wanted list this instance renders. */
    variant: "missing" | "cutoffUnmet";
    /** Kind filter options ({ value: kindCode | "all", label }); the parent supplies the catalog. */
    kindOptions: SelectOption[];
  } = $props();

  const PAGE_SIZE = 50;

  let rows = $state<WantedListItemView[]>([]);
  let thumbs = $state<Map<string, EntityThumbnail>>(new Map());
  let total = $state(0);
  let page = $state(1);
  let kind = $state<string>("all");
  let loading = $state(true);
  let error = $state<string | null>(null);
  let acting = $state(false);

  const totalPages = $derived(Math.max(1, Math.ceil(total / PAGE_SIZE)));
  const fetcher = $derived(variant === "missing" ? fetchMissingWanted : fetchCutoffUnmetWanted);
  const byId = $derived(new Map(rows.map((row) => [row.monitorId, row])));

  const callbacks = {
    onSearchNow: (row: WantedListItemView) => void searchNow([row]),
    onUnmonitor: (row: WantedListItemView) => void unmonitor([row]),
  };

  const items = $derived<AcquisitionListItem[]>(
    rows.map((row) => wantedToListItem(row, variant, row.entityId ? thumbs.get(row.entityId) ?? null : null, callbacks, acting)),
  );

  const bulkActions: AcquisitionBulkAction[] = [
    {
      id: "search",
      label: "Search now",
      icon: RotateCw,
      run: (ids) => void searchNow(ids.map((id) => byId.get(id)).filter((row): row is WantedListItemView => !!row)),
    },
    {
      id: "unmonitor",
      label: "Unmonitor",
      icon: BellOff,
      tone: "danger",
      run: (ids) => void unmonitor(ids.map((id) => byId.get(id)).filter((row): row is WantedListItemView => !!row)),
    },
  ];

  async function load() {
    loading = true;
    error = null;
    try {
      const params: WantedListParams = { page, pageSize: PAGE_SIZE, ...(kind === "all" ? {} : { kind }) };
      const result = await fetcher(params);
      rows = result.items;
      total = Number(result.total);
      // Real entity thumbnails (proper cover + kind shape), same path as the library grid.
      const ids = rows.map((row) => row.entityId).filter((id): id is string => !!id);
      const fetched = await fetchEntityThumbnails(ids).catch(() => []);
      thumbs = new Map(fetched.map((thumbnail) => [thumbnail.id, thumbnail]));
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load";
    } finally {
      loading = false;
    }
  }

  // Reload on first render and whenever the page or kind filter changes.
  $effect(() => {
    void page;
    void kind;
    void variant;
    void load();
  });

  function setKind(next: string) {
    if (next === kind) return;
    kind = next;
    page = 1;
  }

  async function searchNow(targets: WantedListItemView[]) {
    const withAcquisition = targets.filter((row) => row.acquisitionId);
    if (withAcquisition.length === 0) return;
    acting = true;
    error = null;
    try {
      // Sequentially, so a large selection never floods the search queue (each re-search enqueues a job).
      for (const row of withAcquisition) {
        await reSearchAcquisition(row.acquisitionId!);
      }
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to re-search";
    } finally {
      acting = false;
    }
  }

  async function unmonitor(targets: WantedListItemView[]) {
    if (targets.length === 0) return;
    acting = true;
    error = null;
    const failures: string[] = [];
    try {
      for (const row of targets) {
        try {
          await stopMonitor(row.monitorId);
        } catch (reason) {
          failures.push(reason instanceof Error ? reason.message : `Failed to unmonitor ${row.title}`);
        }
      }
      await load();
      const loadFailure = error;
      error = [...new Set([...failures, ...(loadFailure ? [loadFailure] : [])])].join(" · ") || null;
    } finally {
      acting = false;
    }
  }
</script>

<AcquisitionListShell
  {items}
  {loading}
  {error}
  {bulkActions}
  countNoun={variant === "missing" ? "missing item" : "item"}
  emptyTitle={variant === "missing" ? "Nothing is missing" : "Nothing below cutoff"}
  emptyMessage={variant === "missing"
    ? "Monitored items not yet acquired will appear here."
    : "Upgradable items still chasing a better release appear here."}
>
  {#snippet emptyIcon()}
    <PackageSearch class="h-7 w-7 text-text-disabled" />
  {/snippet}

  {#snippet filters()}
    <label class="kind-filter">
      <span class="text-label text-text-muted">Kind</span>
      <Select size="sm" value={kind} options={kindOptions} onchange={setKind} />
    </label>
    <span class="total-count">{total} total</span>
  {/snippet}

  {#snippet footer()}
    {#if totalPages > 1}
      <div class="pager">
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={page <= 1 || loading}
          onclick={() => (page = Math.max(1, page - 1))}
          class="gap-1 px-2.5 py-1 text-xs"
        >
          <ChevronLeft class="h-3.5 w-3.5" />
          Prev
        </Button>
        <span class="pager-label">Page {page} of {totalPages}</span>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={page >= totalPages || loading}
          onclick={() => (page = Math.min(totalPages, page + 1))}
          class="gap-1 px-2.5 py-1 text-xs"
        >
          Next
          <ChevronRight class="h-3.5 w-3.5" />
        </Button>
      </div>
    {/if}
  {/snippet}
</AcquisitionListShell>

<style>
  .kind-filter {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
  }
  .total-count {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.6));
  }
  .pager {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    padding-top: 0.25rem;
  }
  .pager-label {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.7));
  }
</style>
