<script lang="ts">
  import { ChevronLeft, ChevronRight, Loader2, Search, X } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Select, cn } from "@prismedia/ui-svelte";
  import type { SelectOption } from "@prismedia/ui-svelte";
  import type { WantedListItemView } from "$lib/api/generated/model";
  import type { WantedListParams } from "$lib/api/monitors";
  import { fetchCutoffUnmetWanted, fetchMissingWanted, stopMonitor } from "$lib/api/monitors";
  import { reSearchAcquisition } from "$lib/api/acquisitions";
  import { labelForEntityKind, resolveEntityHref } from "$lib/entities/entity-codes";
  import { acquisitionStatusLabel } from "$lib/requests/review-cards";
  import { formatRelativeTime } from "$lib/utils/format";
  import type { AcquisitionStatusCode } from "$lib/api/generated/codes";

  /**
   * One Wanted list surface (Missing or Cutoff Unmet), rendered as a paged, multi-selectable table. Each
   * row shows the kind, title (linking to the entity when present), the monitor + acquisition status, the
   * search cadence and barren-search count, and — on the cutoff-unmet variant — the owned → cutoff quality.
   * Bulk "Search now" and "Unmonitor" drive the existing per-item re-search / stop-monitor endpoints
   * sequentially in small batches (no new backend bulk endpoint). Reused for both lists via `variant`.
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

  let items = $state<WantedListItemView[]>([]);
  let total = $state(0);
  let page = $state(1);
  let kind = $state<string>("all");
  let loading = $state(false);
  let error = $state<string | null>(null);
  let acting = $state(false);
  // Selected monitor ids (a Set for O(1) toggle); reset whenever the page/filter changes.
  let selected = $state<Set<string>>(new Set());

  const totalPages = $derived(Math.max(1, Math.ceil(total / PAGE_SIZE)));
  const allSelected = $derived(items.length > 0 && items.every((item) => selected.has(item.monitorId)));
  const someSelected = $derived(selected.size > 0 && !allSelected);
  const showQuality = $derived(variant === "cutoffUnmet");

  const fetcher = $derived(variant === "missing" ? fetchMissingWanted : fetchCutoffUnmetWanted);

  async function load() {
    loading = true;
    error = null;
    try {
      const params: WantedListParams = {
        page,
        pageSize: PAGE_SIZE,
        ...(kind === "all" ? {} : { kind }),
      };
      const result = await fetcher(params);
      items = result.items;
      total = Number(result.total);
      selected = new Set();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load";
    } finally {
      loading = false;
    }
  }

  // Reload on the first render and whenever the page or kind filter changes.
  $effect(() => {
    // Reference the reactive inputs so the effect re-runs when they change.
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

  function toggleRow(monitorId: string) {
    const next = new Set(selected);
    if (next.has(monitorId)) next.delete(monitorId);
    else next.add(monitorId);
    selected = next;
  }

  function toggleAll() {
    selected = allSelected ? new Set() : new Set(items.map((item) => item.monitorId));
  }

  /** The selected rows, in the current display order. */
  function selectedItems(): WantedListItemView[] {
    return items.filter((item) => selected.has(item.monitorId));
  }

  async function searchSelected() {
    const targets = selectedItems().filter((item) => item.acquisitionId);
    if (targets.length === 0) return;
    acting = true;
    error = null;
    try {
      // Sequentially, so a large selection never floods the search queue (each re-search enqueues a job).
      for (const item of targets) {
        await reSearchAcquisition(item.acquisitionId!);
      }
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to re-search";
    } finally {
      acting = false;
    }
  }

  async function unmonitorSelected() {
    const targets = selectedItems();
    if (targets.length === 0) return;
    acting = true;
    error = null;
    try {
      for (const item of targets) {
        await stopMonitor(item.monitorId);
      }
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to unmonitor";
    } finally {
      acting = false;
    }
  }

  async function searchOne(item: WantedListItemView) {
    if (!item.acquisitionId) return;
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

  async function unmonitorOne(item: WantedListItemView) {
    acting = true;
    error = null;
    try {
      await stopMonitor(item.monitorId);
      await load();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to unmonitor";
    } finally {
      acting = false;
    }
  }

  function entityHref(item: WantedListItemView): string | undefined {
    return item.entityId ? resolveEntityHref(item.kind, item.entityId) : undefined;
  }

  /** A compact future ETA ("in 3h", "in 2d", "due") for the next scheduled search. */
  function nextSearchLabel(value: string | null): string {
    if (!value) return "due";
    const diffMs = new Date(value).getTime() - Date.now();
    if (diffMs <= 0) return "due";
    const minutes = Math.floor(diffMs / 60_000);
    if (minutes < 60) return `in ${minutes}m`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `in ${hours}h`;
    return `in ${Math.floor(hours / 24)}d`;
  }
</script>

<div class="space-y-3">
  <!-- ── Toolbar: kind filter + bulk actions ── -->
  <div class="flex flex-wrap items-center justify-between gap-2">
    <label class="flex items-center gap-2">
      <span class="text-label text-text-muted">Kind</span>
      <Select size="sm" value={kind} options={kindOptions} onchange={setKind} />
    </label>
    <div class="flex items-center gap-2">
      <span class="font-mono text-[0.68rem] text-text-muted">
        {selected.size > 0 ? `${selected.size} selected` : `${total} total`}
      </span>
      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={selected.size === 0 || acting}
        onclick={() => void searchSelected()}
        class="gap-1.5 px-2.5 py-1 text-xs"
      >
        <Search class="h-3.5 w-3.5" />
        Search now
      </Button>
      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={selected.size === 0 || acting}
        onclick={() => void unmonitorSelected()}
        class="gap-1.5 px-2.5 py-1 text-xs"
      >
        <X class="h-3.5 w-3.5" />
        Unmonitor
      </Button>
    </div>
  </div>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
      {error}
    </div>
  {/if}

  <!-- ── List ── -->
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
          aria-label="Select all on this page"
        />
        <span class="flex-1">Item</span>
        {#if showQuality}
          <span class="hidden w-40 shrink-0 sm:block">Quality</span>
        {/if}
        <span class="hidden w-28 shrink-0 md:block">Next search</span>
        <span class="w-16 shrink-0 text-right">Actions</span>
      </div>

      {#each items as item (item.monitorId)}
        {@const href = entityHref(item)}
        <div class="flex items-center gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0">
          <Checkbox
            checked={selected.has(item.monitorId)}
            onchange={() => toggleRow(item.monitorId)}
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
              {#if item.acquisitionStatus}
                <Badge variant="info">
                  {acquisitionStatusLabel(item.acquisitionStatus as AcquisitionStatusCode)}
                </Badge>
              {/if}
              <span class="font-mono">last: {formatRelativeTime(item.lastSearchedAt, true)}</span>
              {#if Number(item.barrenSearches) > 0}
                <span class="font-mono" title="Consecutive searches that found nothing better">
                  {item.barrenSearches} barren
                </span>
              {/if}
            </div>
          </div>

          {#if showQuality}
            <div class="hidden w-40 shrink-0 items-center gap-1 font-mono text-[0.7rem] sm:flex">
              <span class="truncate text-text-secondary">{item.ownedQuality ?? "—"}</span>
              <ChevronRight class="h-3 w-3 shrink-0 text-text-muted" />
              <span class="truncate text-text-accent">{item.cutoffQuality ?? "—"}</span>
            </div>
          {/if}

          <span class="hidden w-28 shrink-0 font-mono text-[0.7rem] text-text-muted md:block">
            {nextSearchLabel(item.nextSearchAt)}
          </span>

          <div class="flex w-16 shrink-0 items-center justify-end gap-1">
            {#if item.acquisitionId}
              <button
                type="button"
                onclick={() => void searchOne(item)}
                disabled={acting}
                class="rounded-xs p-1 text-text-muted transition-colors hover:text-text-accent disabled:opacity-40"
                title="Search now"
                aria-label={`Search now for ${item.title}`}
              >
                <Search class="h-3.5 w-3.5" />
              </button>
            {/if}
            <button
              type="button"
              onclick={() => void unmonitorOne(item)}
              disabled={acting}
              class="rounded-xs p-1 text-text-muted transition-colors hover:text-error-text disabled:opacity-40"
              title="Unmonitor"
              aria-label={`Unmonitor ${item.title}`}
            >
              <X class="h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      {/each}
    </div>

    <!-- ── Pagination ── -->
    {#if totalPages > 1}
      <div class="flex items-center justify-between gap-2">
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
        <span class="font-mono text-[0.7rem] text-text-muted">
          Page {page} of {totalPages}
        </span>
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
  {:else if loading}
    <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading…</span>
    </div>
  {:else}
    <div class="empty-rack-slot p-8 text-center">
      <p class="text-sm text-text-muted">
        {variant === "missing"
          ? "Nothing is missing. Monitored items not yet acquired will appear here."
          : "Nothing is below its quality cutoff. Upgradable items still chasing a better release appear here."}
      </p>
    </div>
  {/if}
</div>
