<script lang="ts">
  import { ChevronsDownUp, ChevronsUpDown, HardDriveDownload, Loader2, Trash2 } from "@lucide/svelte";
  import { Button, Checkbox, SearchInput, Select, cn } from "@prismedia/ui-svelte";
  import { SvelteSet } from "svelte/reactivity";
  import { formatSpeed, numberValue } from "$lib/utils/format";
  import DownloadTreeRows from "./DownloadTreeRows.svelte";
  import {
    buildDownloadTree,
    expandableDownloadNodeKeys,
    type DownloadManagerEntry,
  } from "./download-tree";
  import type { EntityThumbnail } from "$lib/api/generated/model";

  let {
    entries,
    thumbnails,
    loading = false,
    error = null,
    acting = false,
    selectedId = null,
    onSelect,
    onRemove,
  }: {
    entries: DownloadManagerEntry[];
    thumbnails: ReadonlyMap<string, EntityThumbnail>;
    loading?: boolean;
    error?: string | null;
    acting?: boolean;
    selectedId?: string | null;
    onSelect: (id: string) => void;
    onRemove: (ids: string[]) => void;
  } = $props();

  let query = $state("");
  let activeStatus = $state("all");
  let sortBy = $state("activity");
  const expanded = new SvelteSet<string>();
  const checkedIds = new SvelteSet<string>();

  const statusFilters = [
    { value: "all", label: "All", match: () => true },
    { value: "active", label: "Active", match: (entry: DownloadManagerEntry) => ["downloading", "searching", "queued"].includes(entry.item.tone) },
    { value: "attention", label: "Attention", match: (entry: DownloadManagerEntry) => ["attention", "failed"].includes(entry.item.tone) },
    { value: "cleanup", label: "Cleaning up", match: (entry: DownloadManagerEntry) => entry.item.tone === "cleanup" },
  ];

  const sortOptions = [
    { value: "activity", label: "Recent activity" },
    { value: "title", label: "Title A–Z" },
    { value: "progress", label: "Progress" },
  ];

  const visibleEntries = $derived.by(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    const statusFilter = statusFilters.find((filter) => filter.value === activeStatus) ?? statusFilters[0];
    const matched = entries.filter((entry) => {
      if (!statusFilter.match(entry)) return false;
      if (!normalizedQuery) return true;
      return [entry.item.title, entry.item.subtitle, entry.item.clientLabel, entry.row.transferState]
        .filter(Boolean)
        .some((value) => value!.toLocaleLowerCase().includes(normalizedQuery));
    });
    if (sortBy === "title") return [...matched].sort((a, b) => a.item.title.localeCompare(b.item.title, undefined, { numeric: true }));
    if (sortBy === "progress") return [...matched].sort((a, b) => (b.item.progress ?? -1) - (a.item.progress ?? -1));
    return matched;
  });

  const entriesById = $derived(new Map(visibleEntries.map((entry) => [entry.item.id, entry])));
  const tree = $derived(buildDownloadTree(visibleEntries, thumbnails));
  const expandableKeys = $derived(expandableDownloadNodeKeys(tree));
  const filtering = $derived(query.trim().length > 0 || activeStatus !== "all");
  const effectiveExpanded = $derived(filtering ? new Set(expandableKeys) : expanded);
  const visibleSelectableIds = $derived(visibleEntries.filter((entry) => entry.item.selectable !== false).map((entry) => entry.item.id));
  const availableIds = $derived(new Set(entries.filter((entry) => entry.item.selectable !== false).map((entry) => entry.item.id)));
  const safeCheckedIds = $derived(new Set([...checkedIds].filter((id) => availableIds.has(id))));
  const allVisibleChecked = $derived(visibleSelectableIds.length > 0 && visibleSelectableIds.every((id) => safeCheckedIds.has(id)));
  const aggregateSpeed = $derived(entries.reduce((sum, entry) => sum + (numberValue(entry.row.downloadSpeedBytesPerSecond) ?? 0), 0));

  function toggleExpanded(key: string) {
    if (expanded.has(key)) expanded.delete(key);
    else expanded.add(key);
  }

  function toggleChecked(id: string) {
    if (checkedIds.has(id)) checkedIds.delete(id);
    else checkedIds.add(id);
  }

  function toggleAllVisible() {
    if (allVisibleChecked) visibleSelectableIds.forEach((id) => checkedIds.delete(id));
    else visibleSelectableIds.forEach((id) => checkedIds.add(id));
  }

  function toggleAllExpanded() {
    if (expanded.size >= expandableKeys.length && expandableKeys.length > 0) {
      expanded.clear();
      return;
    }
    expandableKeys.forEach((key) => expanded.add(key));
  }
</script>

<section class="manager-shell" aria-label="Download manager">
  <header class="manager-toolbar">
    <div class="manager-title">
      <span class="manager-mark"><HardDriveDownload class="h-4 w-4" /></span>
      <span>
        <span class="eyebrow">Transfer queue</span>
        <strong>{entries.length} {entries.length === 1 ? "download" : "downloads"}</strong>
      </span>
      {#if aggregateSpeed > 0}<span class="aggregate-speed">↓ {formatSpeed(aggregateSpeed)}</span>{/if}
    </div>

    <div class="toolbar-controls">
      <SearchInput
        bind:value={query}
        ariaLabel="Filter downloads"
        placeholder="Filter entities or clients…"
        class="download-search"
        inputClass="text-xs"
      />
      <div class="status-filters" role="group" aria-label="Filter downloads by status">
        {#each statusFilters as filter (filter.value)}
          {@const count = entries.filter(filter.match).length}
          <Button
            variant={activeStatus === filter.value ? "secondary" : "ghost"}
            size="sm"
            class={cn("filter-button", activeStatus === filter.value && "is-active")}
            onclick={() => (activeStatus = filter.value)}
          >
            {filter.label}
            {#if filter.value !== "all" && count > 0}<span class="filter-count">{count}</span>{/if}
          </Button>
        {/each}
      </div>
      <Select
        value={sortBy}
        options={sortOptions}
        size="sm"
        ariaLabel="Sort downloads"
        class="download-sort"
        onchange={(value) => (sortBy = value)}
      />
    </div>
  </header>

  <div class={["selection-bar", safeCheckedIds.size > 0 && "has-selection"]}>
    <span class="selection-check">
      <Checkbox
        size="md"
        checked={allVisibleChecked}
        indeterminate={safeCheckedIds.size > 0 && !allVisibleChecked}
        onchange={toggleAllVisible}
        aria-label="Select all shown downloads"
      />
      <span>{safeCheckedIds.size > 0 ? `${safeCheckedIds.size} selected` : `${visibleEntries.length} shown`}</span>
    </span>
    <div class="selection-actions">
      <Button
        variant="ghost"
        size="sm"
        aria-label="Select all downloads"
        disabled={visibleSelectableIds.length === 0}
        onclick={toggleAllVisible}
      >
        {allVisibleChecked ? "Clear selection" : "Select all"}
      </Button>
      <Button
        variant="ghost"
        size="sm"
        class="expand-button"
        disabled={expandableKeys.length === 0 || filtering}
        onclick={toggleAllExpanded}
      >
        {#if expanded.size >= expandableKeys.length && expandableKeys.length > 0}
          <ChevronsDownUp class="h-3.5 w-3.5" /> Collapse all
        {:else}
          <ChevronsUpDown class="h-3.5 w-3.5" /> Expand all
        {/if}
      </Button>
      {#if safeCheckedIds.size > 0}
        <Button
          variant="danger"
          size="sm"
          disabled={acting}
          onclick={() => onRemove([...safeCheckedIds])}
        >
          <Trash2 class="h-3.5 w-3.5" /> Remove
        </Button>
      {/if}
    </div>
  </div>

  {#if error}
    <div role="alert" class="manager-error">{error}</div>
  {/if}

  <div class="table-scroll">
    <div class="downloads-grid" role="treegrid" aria-label="Downloads">
      <div class="table-head" role="row">
        <div role="columnheader" aria-label="Select"></div>
        <div role="columnheader">Entity</div>
        <div role="columnheader" class="numeric-head">Size</div>
        <div role="columnheader">Progress</div>
        <div role="columnheader">Status</div>
        <div role="columnheader" class="numeric-head">Speed</div>
        <div role="columnheader" class="numeric-head">ETA</div>
        <div role="columnheader" class="numeric-head">Seeds / Peers</div>
        <div role="columnheader" class="numeric-head">Updated</div>
      </div>

      {#if tree.length > 0}
        <DownloadTreeRows
          nodes={tree}
          {entriesById}
          expanded={effectiveExpanded}
          {selectedId}
          checkedIds={safeCheckedIds}
          onToggleExpanded={toggleExpanded}
          {onSelect}
          onToggleChecked={toggleChecked}
        />
      {:else if loading}
        <div class="manager-state"><Loader2 class="h-4 w-4 animate-spin" /> Loading downloads…</div>
      {:else}
        <div class="manager-state empty-state">
          <HardDriveDownload class="h-6 w-6" />
          <strong>{filtering ? "Nothing matches these filters" : "Nothing downloading"}</strong>
          <span>{filtering ? "Try a different title, client, or status." : "Requested entities will appear here as soon as their acquisition begins."}</span>
        </div>
      {/if}
    </div>
  </div>
</section>

<style>
  .manager-shell {
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-md, 8px) var(--radius-md, 8px) 0 0;
    background:
      linear-gradient(180deg, rgb(255 255 255 / 0.018), transparent 6rem),
      var(--color-surface-1);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.025), 0 12px 35px rgb(0 0 0 / 0.16);
  }

  .manager-toolbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    min-height: 4.2rem;
    padding: 0.7rem 0.85rem;
    border-bottom: 1px solid var(--color-border-subtle);
    background: rgb(8 8 9 / 0.78);
  }
  .manager-title { display: flex; flex: 0 0 auto; align-items: center; gap: 0.65rem; }
  .manager-title > span:nth-child(2) { display: flex; flex-direction: column; gap: 0.05rem; }
  .manager-title strong { color: var(--color-text-primary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 0.92rem; font-weight: 600; }
  .eyebrow { color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.59rem; letter-spacing: 0.12em; text-transform: uppercase; }
  .manager-mark { display: grid; width: 2rem; height: 2rem; place-items: center; border: 1px solid var(--color-border-subtle); border-radius: var(--radius-xs, 4px); color: var(--color-text-secondary); background: var(--color-surface-2); box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.035); }
  .aggregate-speed { padding-left: 0.7rem; border-left: 1px solid var(--color-border-subtle); color: var(--color-text-accent); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; }

  .toolbar-controls { display: flex; min-width: 0; flex: 1 1 auto; align-items: center; justify-content: flex-end; gap: 0.5rem; }
  :global(.download-search) { width: min(18rem, 28vw); min-height: 2rem; padding-block: 0.35rem; border-radius: var(--radius-xs, 4px); }
  .status-filters { display: flex; align-items: center; gap: 0.15rem; }
  :global(.filter-button) { height: 1.85rem; padding-inline: 0.55rem; font-size: 0.68rem; }
  :global(.filter-button.is-active) { border-color: var(--color-border-default); color: var(--color-text-primary); box-shadow: inset 2px 0 0 var(--color-accent-400); }
  .filter-count { min-width: 1rem; padding: 0.05rem 0.25rem; border-radius: var(--radius-xs, 4px); background: rgb(255 255 255 / 0.055); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.59rem; }
  :global(.download-sort) { width: 9rem; }

  .selection-bar { display: flex; min-height: 2.35rem; align-items: center; justify-content: space-between; gap: 0.75rem; padding: 0.3rem 0.7rem; border-bottom: 1px solid var(--color-border-subtle); color: var(--color-text-muted); background: rgb(255 255 255 / 0.012); font-size: 0.68rem; }
  .selection-bar.has-selection { background: color-mix(in srgb, var(--color-accent-500) 5%, var(--color-surface-1)); }
  .selection-check, .selection-actions { display: flex; align-items: center; gap: 0.55rem; }
  :global(.selection-actions button) { height: 1.75rem; font-size: 0.66rem; }

  .manager-error { border-bottom: 1px solid rgb(225 80 80 / 0.25); border-left: 2px solid var(--color-error); padding: 0.55rem 0.8rem; background: var(--color-error-muted); color: var(--color-error-text); font-size: 0.76rem; }
  .table-scroll { max-height: clamp(19rem, 43vh, 35rem); overflow: auto; scrollbar-gutter: stable; }
  .downloads-grid { min-width: 70rem; }
  .table-head { position: sticky; top: 0; z-index: 5; display: grid; grid-template-columns: 2.25rem minmax(18rem, 1.8fr) 7.5rem minmax(10rem, 0.9fr) minmax(9rem, 0.85fr) 7rem 5.5rem 5.5rem 4.5rem; min-width: 70rem; border-bottom: 1px solid var(--color-border-default); background: rgb(12 12 13 / 0.98); box-shadow: 0 2px 8px rgb(0 0 0 / 0.28); }
  [role="columnheader"] { padding: 0.42rem 0.7rem; border-right: 1px solid rgb(255 255 255 / 0.045); color: var(--color-text-muted); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.58rem; font-weight: 600; letter-spacing: 0.07em; text-transform: uppercase; }
  [role="columnheader"]:last-child { border-right: 0; }
  .numeric-head { text-align: right; }
  .manager-state { display: flex; min-height: 10rem; align-items: center; justify-content: center; gap: 0.5rem; color: var(--color-text-muted); font-size: 0.78rem; }
  .empty-state { flex-direction: column; gap: 0.35rem; }
  .empty-state strong { color: var(--color-text-secondary); font-family: var(--font-heading, "Geist", sans-serif); font-size: 0.88rem; }
  .empty-state span { max-width: 28rem; text-align: center; font-size: 0.72rem; }

  @media (max-width: 960px) {
    .manager-toolbar { align-items: flex-start; flex-direction: column; }
    .toolbar-controls { width: 100%; flex-wrap: wrap; justify-content: flex-start; }
    :global(.download-search) { width: 100%; }
  }

  @media (max-width: 640px) {
    .manager-shell { border-radius: var(--radius-sm, 6px) var(--radius-sm, 6px) 0 0; }
    .status-filters { max-width: 100%; overflow-x: auto; }
    .selection-bar { align-items: flex-start; flex-direction: column; }
    .selection-actions { width: 100%; flex-wrap: wrap; }
    :global(.expand-button) { display: none; }
  }
</style>
