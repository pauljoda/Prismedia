<script lang="ts">
  /**
   * The shared scaffold every acquisition list tab (Downloads, Missing, Cutoff Unmet) renders through:
   * a filter bar (text search + optional kind chips + optional status pills), selection with a bulk
   * action bar, a responsive column of {@link AcquisitionCard}s, and loading/empty states. Server-driven
   * controls a tab still owns — a kind <Select>, pagination — are injected via the `filters` and `footer`
   * snippets, so the shell stays generic and no tab needs a bespoke layout.
   */
  import type { Snippet } from "svelte";
  import { ArrowUpDown, Loader2, Search, X } from "@lucide/svelte";
  import { Checkbox, Select, cn } from "@prismedia/ui-svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import AcquisitionCard from "$lib/components/acquisitions/AcquisitionCard.svelte";
  import type {
    AcquisitionBulkAction,
    AcquisitionListItem,
  } from "$lib/requests/acquisition-list-item";

  /** A client-side status filter pill: a label plus the predicate that decides membership. */
  export interface AcquisitionStatusFilter {
    value: string;
    label: string;
    match: (item: AcquisitionListItem) => boolean;
  }

  let {
    items,
    loading = false,
    error = null,
    bulkActions = [],
    statusFilters = [],
    kindChips = false,
    searchable = true,
    emptyIcon,
    emptyTitle = "Nothing here",
    emptyMessage = "",
    countNoun = "item",
    filters,
    footer,
  }: {
    items: AcquisitionListItem[];
    loading?: boolean;
    error?: string | null;
    bulkActions?: AcquisitionBulkAction[];
    statusFilters?: AcquisitionStatusFilter[];
    kindChips?: boolean;
    searchable?: boolean;
    emptyIcon?: Snippet;
    emptyTitle?: string;
    emptyMessage?: string;
    countNoun?: string;
    filters?: Snippet;
    footer?: Snippet;
  } = $props();

  let query = $state("");
  let activeStatus = $state("all");
  let activeKind = $state("all");
  let sortBy = $state("recent");
  let selected = $state<Set<string>>(new Set());

  const selectable = $derived(bulkActions.length > 0);

  // Sort options are generic: preserve the server's order ("Recent activity"), sort by title, and — when
  // any row reports progress — by progress. Kept here so every tab gets the control for free.
  const sortOptions = $derived([
    { value: "recent", label: "Recent activity" },
    { value: "title", label: "Title A–Z" },
    ...(items.some((item) => item.progress != null) ? [{ value: "progress", label: "Progress" }] : []),
  ]);

  // Kind chips are derived from what's actually present, so a tab opts in without listing kinds.
  const kindOptions = $derived.by(() => {
    const kinds = Array.from(new Set(items.map((item) => item.kind)));
    return kinds.map((kind) => ({ value: kind, label: labelForEntityKind(kind) }));
  });

  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase();
    const statusFilter = statusFilters.find((filter) => filter.value === activeStatus);
    const matched = items.filter((item) => {
      if (q && !item.title.toLowerCase().includes(q)) return false;
      if (kindChips && activeKind !== "all" && item.kind !== activeKind) return false;
      if (statusFilter && !statusFilter.match(item)) return false;
      return true;
    });
    if (sortBy === "title") {
      return [...matched].sort((a, b) => a.title.localeCompare(b.title));
    }
    if (sortBy === "progress") {
      return [...matched].sort((a, b) => (b.progress ?? -1) - (a.progress ?? -1));
    }
    return matched; // "recent" keeps the server's order
  });

  // Prune selection to ids that still exist as the list refreshes (polling, pagination).
  $effect(() => {
    const ids = new Set(items.map((item) => item.id));
    if ([...selected].some((id) => !ids.has(id))) {
      selected = new Set([...selected].filter((id) => ids.has(id)));
    }
  });

  const allFilteredSelected = $derived(filtered.length > 0 && filtered.every((item) => selected.has(item.id)));

  function toggle(id: string) {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    selected = next;
  }

  function toggleAll() {
    selected = allFilteredSelected ? new Set() : new Set(filtered.map((item) => item.id));
  }

  function runBulk(action: AcquisitionBulkAction) {
    action.run([...selected]);
  }
</script>

<div class="acq-list">
  <!-- ── Filter bar ── -->
  {#if searchable || statusFilters.length > 0 || kindChips || filters}
    <div class="filter-bar">
      {#if searchable}
        <label class="search">
          <Search size={15} />
          <input
            type="text"
            placeholder={`Filter ${countNoun}s…`}
            value={query}
            oninput={(event) => (query = event.currentTarget.value)}
            aria-label={`Filter ${countNoun}s`}
          />
          {#if query}
            <button type="button" class="search-clear" onclick={() => (query = "")} aria-label="Clear filter">
              <X size={13} />
            </button>
          {/if}
        </label>
      {/if}

      {#if filters}{@render filters()}{/if}

      {#if statusFilters.length > 0}
        <div class="chips" role="group" aria-label="Filter by status">
          <button type="button" class={cn("pill", activeStatus === "all" && "is-active")} onclick={() => (activeStatus = "all")}>
            All
          </button>
          {#each statusFilters as filter (filter.value)}
            {@const count = items.filter(filter.match).length}
            <button
              type="button"
              class={cn("pill", activeStatus === filter.value && "is-active")}
              onclick={() => (activeStatus = filter.value)}
            >
              {filter.label}
              {#if count > 0}<span class="pill-count">{count}</span>{/if}
            </button>
          {/each}
        </div>
      {/if}

      {#if kindChips && kindOptions.length > 1}
        <div class="chips" role="group" aria-label="Filter by kind">
          <button type="button" class={cn("pill", activeKind === "all" && "is-active")} onclick={() => (activeKind = "all")}>
            All kinds
          </button>
          {#each kindOptions as option (option.value)}
            <button
              type="button"
              class={cn("pill", activeKind === option.value && "is-active")}
              onclick={() => (activeKind = option.value)}
            >
              {option.label}
            </button>
          {/each}
        </div>
      {/if}

      <label class="sort">
        <ArrowUpDown size={14} />
        <Select size="sm" value={sortBy} options={sortOptions} onchange={(value) => (sortBy = value)} />
      </label>
    </div>
  {/if}

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
  {/if}

  <!-- ── Selection / bulk bar ── -->
  {#if selectable && filtered.length > 0}
    <div class="bulk-bar" class:has-selection={selected.size > 0}>
      <label class="bulk-all">
        <Checkbox
          size="md"
          checked={allFilteredSelected}
          indeterminate={selected.size > 0 && !allFilteredSelected}
          onchange={toggleAll}
          aria-label="Select all shown"
        />
        <span>{selected.size > 0 ? `${selected.size} selected` : `Select all (${filtered.length})`}</span>
      </label>
      {#if selected.size > 0}
        <div class="bulk-actions">
          {#each bulkActions as action (action.id)}
            {@const Icon = action.icon}
            <button type="button" class={`bulk-action bulk-${action.tone ?? "default"}`} onclick={() => runBulk(action)}>
              <Icon size={14} />
              {action.label}
            </button>
          {/each}
          <button type="button" class="bulk-clear" onclick={() => (selected = new Set())} aria-label="Clear selection">
            <X size={14} />
          </button>
        </div>
      {/if}
    </div>
  {/if}

  <!-- ── Cards ── -->
  {#if filtered.length > 0}
    <div class="cards">
      {#each filtered as item (item.id)}
        <AcquisitionCard {item} {selectable} selected={selected.has(item.id)} onToggleSelected={toggle} />
      {/each}
    </div>
    {#if footer}{@render footer()}{/if}
  {:else if loading}
    <div class="state">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span>Loading…</span>
    </div>
  {:else}
    <div class="empty">
      {#if emptyIcon}{@render emptyIcon()}{/if}
      <p class="empty-title">{query || activeStatus !== "all" || activeKind !== "all" ? "Nothing matches the filters" : emptyTitle}</p>
      {#if emptyMessage}<p class="empty-message">{emptyMessage}</p>{/if}
    </div>
  {/if}
</div>

<style>
  .acq-list {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }

  .filter-bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.6rem;
  }

  .search {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    flex: 1 1 14rem;
    min-width: 0;
    max-width: 22rem;
    height: 2.1rem;
    padding: 0 0.6rem;
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-sm, 6px);
    background: rgb(255 255 255 / 0.04);
    color: rgb(196 201 212 / 0.7);
    transition: border-color 120ms ease;
  }
  .search:focus-within { border-color: rgb(242 194 106 / 0.4); }
  .search input {
    flex: 1 1 auto;
    min-width: 0;
    border: none;
    background: transparent;
    color: rgb(244 239 230 / 0.95);
    font-size: 0.82rem;
    outline: none;
  }
  .search-clear {
    display: grid;
    place-items: center;
    color: rgb(196 201 212 / 0.6);
    cursor: pointer;
  }
  .search-clear:hover { color: rgb(244 239 230 / 0.9); }

  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem;
  }
  .pill {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.3rem 0.6rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid rgb(255 255 255 / 0.1);
    background: rgb(255 255 255 / 0.03);
    color: rgb(196 201 212 / 0.72);
    font-size: 0.72rem;
    font-weight: 600;
    cursor: pointer;
    transition: all 120ms ease;
    white-space: nowrap;
  }
  .pill:hover { color: rgb(244 239 230 / 0.92); border-color: rgb(255 255 255 / 0.18); }
  .pill.is-active {
    color: #f2c26a;
    border-color: rgb(242 194 106 / 0.5);
    background: rgb(50 38 14 / 0.5);
    box-shadow: 0 0 10px rgb(242 194 106 / 0.12);
  }
  .pill-count {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    opacity: 0.7;
  }

  .sort {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    margin-left: auto;
    color: var(--color-text-muted, rgb(196 201 212 / 0.6));
  }

  .bulk-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.6rem;
    flex-wrap: wrap;
    padding: 0.35rem 0.6rem;
    border: 1px solid rgb(255 255 255 / 0.08);
    border-radius: var(--radius-sm, 6px);
    background: rgb(255 255 255 / 0.02);
    transition: border-color 120ms ease, background 120ms ease;
  }
  .bulk-bar.has-selection {
    border-color: rgb(242 194 106 / 0.3);
    background: rgb(40 30 12 / 0.35);
  }
  .bulk-all {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.74rem;
    color: rgb(196 201 212 / 0.82);
    cursor: pointer;
  }
  .bulk-actions { display: flex; align-items: center; gap: 0.4rem; }
  .bulk-action {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 1.85rem;
    padding: 0 0.65rem;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid rgb(255 255 255 / 0.12);
    background: rgb(255 255 255 / 0.05);
    color: rgb(244 239 230 / 0.9);
    font-size: 0.74rem;
    font-weight: 600;
    cursor: pointer;
    transition: all 120ms ease;
  }
  .bulk-action:hover { background: rgb(255 255 255 / 0.1); }
  .bulk-danger { color: #ff9a86; border-color: rgb(255 122 92 / 0.3); }
  .bulk-danger:hover { background: rgb(48 18 14 / 0.6); border-color: rgb(255 122 92 / 0.5); }
  .bulk-clear {
    display: grid;
    place-items: center;
    width: 1.85rem;
    height: 1.85rem;
    border-radius: var(--radius-sm, 6px);
    color: rgb(196 201 212 / 0.6);
    cursor: pointer;
  }
  .bulk-clear:hover { color: rgb(244 239 230 / 0.9); background: rgb(255 255 255 / 0.06); }

  .cards {
    /* Establishes the size container the AcquisitionCard's responsive rules query against — each
       full-width card matches the list width, so cards restack by available width, not viewport. */
    container-type: inline-size;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  .state {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.6rem;
    padding: 2.5rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.7));
    font-size: 0.85rem;
  }

  .empty {
    display: grid;
    place-items: center;
    gap: 0.5rem;
    padding: 2.75rem 1rem;
    text-align: center;
    border: 1px dashed rgb(255 255 255 / 0.1);
    border-radius: var(--radius-md, 10px);
  }
  .empty-title { font-size: 0.9rem; font-weight: 600; color: rgb(244 239 230 / 0.9); }
  .empty-message { font-size: 0.8rem; color: var(--color-text-muted, rgb(196 201 212 / 0.7)); max-width: 28rem; }
</style>
