<script lang="ts">
  import { onMount } from "svelte";
  import { browser as isBrowser } from "$app/environment";
  import { ArrowDown, ArrowUp, ChevronsDownUp, ChevronsUpDown, Columns3, HardDriveDownload, Loader2, Trash2 } from "@lucide/svelte";
  import { Button, Checkbox, Empty, SearchInput, Select, ToggleGroup, cn } from "@prismedia/ui-svelte";
  import { SvelteSet } from "svelte/reactivity";
  import { formatSpeed, numberValue } from "$lib/utils/format";
  import DownloadTreeRows from "./DownloadTreeRows.svelte";
  import DownloadTableHeader from "./DownloadTableHeader.svelte";
  import {
    DEFAULT_DOWNLOAD_COLUMN_WIDTHS,
    DOWNLOAD_TABLE_COLUMN_KEYS,
    DOWNLOAD_TABLE_COLUMNS,
    clampDownloadColumnWidth,
    downloadColumnTemplate,
    downloadTableWidth,
    sortDownloadTree,
    type DownloadColumnKey,
    type DownloadColumnWidths,
    type DownloadSortDirection,
  } from "./download-table";
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
    selectedKey = null,
    onSelect,
    onRemove,
  }: {
    entries: DownloadManagerEntry[];
    thumbnails: ReadonlyMap<string, EntityThumbnail>;
    loading?: boolean;
    error?: string | null;
    acting?: boolean;
    selectedKey?: string | null;
    onSelect: (id: string) => void;
    onRemove: (ids: string[]) => void;
  } = $props();

  let query = $state("");
  let activeStatus = $state("all");
  let sortKey = $state<DownloadColumnKey>("updated");
  let sortDirection = $state<DownloadSortDirection>("desc");
  let columnWidths = $state<DownloadColumnWidths>({ ...DEFAULT_DOWNLOAD_COLUMN_WIDTHS });
  let resizing = $state<{
    key: DownloadColumnKey;
    adjacentKey: DownloadColumnKey | null;
    startX: number;
    startWidth: number;
    startAdjacentWidth: number;
  } | null>(null);
  const expanded = new SvelteSet<string>();
  const checkedIds = new SvelteSet<string>();
  const DOWNLOAD_COLUMN_WIDTHS_STORAGE_KEY = "prismedia.downloads.column-widths";

  const statusFilters = [
    { value: "all", label: "All", match: () => true },
    { value: "active", label: "Active", match: (entry: DownloadManagerEntry) => ["downloading", "searching", "queued"].includes(entry.item.tone) },
    { value: "waiting", label: "Waiting", match: (entry: DownloadManagerEntry) => entry.item.tone === "muted" },
    { value: "attention", label: "Attention", match: (entry: DownloadManagerEntry) => ["attention", "failed"].includes(entry.item.tone) },
    { value: "cleanup", label: "Cleaning up", match: (entry: DownloadManagerEntry) => entry.item.tone === "cleanup" },
  ];

  const visibleEntries = $derived.by(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    const statusFilter = statusFilters.find((filter) => filter.value === activeStatus) ?? statusFilters[0];
    return entries.filter((entry) => {
      if (!statusFilter.match(entry)) return false;
      if (!normalizedQuery) return true;
      return [entry.item.title, entry.item.subtitle, entry.item.clientLabel, entry.row.transferState]
        .filter(Boolean)
        .some((value) => value!.toLocaleLowerCase().includes(normalizedQuery));
    });
  });

  const entriesById = $derived(new Map(visibleEntries.map((entry) => [entry.item.id, entry])));
  const unsortedTree = $derived(buildDownloadTree(visibleEntries, thumbnails));
  const tree = $derived(sortDownloadTree(unsortedTree, entriesById, sortKey, sortDirection));
  const expandableKeys = $derived(expandableDownloadNodeKeys(tree));
  const filtering = $derived(query.trim().length > 0 || activeStatus !== "all");
  const effectiveExpanded = $derived(filtering ? new Set(expandableKeys) : expanded);
  const visibleSelectableIds = $derived(visibleEntries.filter((entry) => entry.item.selectable !== false).map((entry) => entry.item.id));
  const availableIds = $derived(new Set(entries.filter((entry) => entry.item.selectable !== false).map((entry) => entry.item.id)));
  const safeCheckedIds = $derived(new Set([...checkedIds].filter((id) => availableIds.has(id))));
  const allVisibleChecked = $derived(visibleSelectableIds.length > 0 && visibleSelectableIds.every((id) => safeCheckedIds.has(id)));
  const aggregateSpeed = $derived(entries.reduce((sum, entry) => sum + (numberValue(entry.row.downloadSpeedBytesPerSecond) ?? 0), 0));
  const gridTemplate = $derived(downloadColumnTemplate(columnWidths));
  const gridWidth = $derived(downloadTableWidth(columnWidths));
  const sortOptions = DOWNLOAD_TABLE_COLUMN_KEYS.map((key) => ({ value: key, label: DOWNLOAD_TABLE_COLUMNS[key].label }));

  function toggleExpanded(key: string) {
    if (expanded.has(key)) expanded.delete(key);
    else expanded.add(key);
  }

  function setChecked(ids: string[], checked: boolean) {
    ids.forEach((id) => {
      if (checked) checkedIds.add(id);
      else checkedIds.delete(id);
    });
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

  function sortColumn(key: DownloadColumnKey) {
    if (sortKey === key) {
      sortDirection = sortDirection === "asc" ? "desc" : "asc";
      return;
    }
    sortKey = key;
    sortDirection = key === "entity" || key === "status" ? "asc" : "desc";
  }

  function persistColumnWidths() {
    if (!isBrowser) return;
    try {
      localStorage.setItem(DOWNLOAD_COLUMN_WIDTHS_STORAGE_KEY, JSON.stringify(columnWidths));
    } catch {
      // Column resizing remains usable when browser storage is unavailable.
    }
  }

  function resizeColumn(key: DownloadColumnKey, width: number) {
    columnWidths = { ...columnWidths, [key]: clampDownloadColumnWidth(key, width) };
  }

  function nextColumnKey(key: DownloadColumnKey): DownloadColumnKey | null {
    const index = DOWNLOAD_TABLE_COLUMN_KEYS.indexOf(key);
    return DOWNLOAD_TABLE_COLUMN_KEYS[index + 1] ?? null;
  }

  function headerWidth(event: Event): number | null {
    const handle = event.currentTarget;
    if (!(handle instanceof HTMLElement)) return null;
    return handle.parentElement?.getBoundingClientRect().width ?? null;
  }

  function adjacentHeaderWidth(event: Event): number | null {
    const handle = event.currentTarget;
    if (!(handle instanceof HTMLElement)) return null;
    return handle.parentElement?.nextElementSibling?.getBoundingClientRect().width ?? null;
  }

  function resizeColumnPair(
    key: DownloadColumnKey,
    adjacentKey: DownloadColumnKey | null,
    startWidth: number,
    startAdjacentWidth: number,
    requestedDelta: number,
  ) {
    if (!adjacentKey) {
      resizeColumn(key, startWidth + requestedDelta);
      return;
    }
    const definition = DOWNLOAD_TABLE_COLUMNS[key];
    const adjacentDefinition = DOWNLOAD_TABLE_COLUMNS[adjacentKey];
    const minimumDelta = Math.max(
      definition.minWidth - startWidth,
      startAdjacentWidth - adjacentDefinition.maxWidth,
    );
    const maximumDelta = Math.min(
      definition.maxWidth - startWidth,
      startAdjacentWidth - adjacentDefinition.minWidth,
    );
    const delta = Math.max(minimumDelta, Math.min(maximumDelta, requestedDelta));
    columnWidths = {
      ...columnWidths,
      [key]: Math.round(startWidth + delta),
      [adjacentKey]: Math.round(startAdjacentWidth - delta),
    };
  }

  function startColumnResize(event: PointerEvent, key: DownloadColumnKey) {
    event.preventDefault();
    event.stopPropagation();
    const adjacentKey = nextColumnKey(key);
    resizing = {
      key,
      adjacentKey,
      startX: event.clientX,
      startWidth: headerWidth(event) ?? columnWidths[key],
      startAdjacentWidth: adjacentHeaderWidth(event) ?? (adjacentKey ? columnWidths[adjacentKey] : 0),
    };
  }

  function continueColumnResize(event: PointerEvent) {
    if (!resizing) return;
    resizeColumnPair(
      resizing.key,
      resizing.adjacentKey,
      resizing.startWidth,
      resizing.startAdjacentWidth,
      event.clientX - resizing.startX,
    );
  }

  function finishColumnResize() {
    if (!resizing) return;
    resizing = null;
    persistColumnWidths();
  }

  function nudgeColumnWidth(event: KeyboardEvent, key: DownloadColumnKey, delta: number) {
    const adjacentKey = nextColumnKey(key);
    resizeColumnPair(
      key,
      adjacentKey,
      headerWidth(event) ?? columnWidths[key],
      adjacentHeaderWidth(event) ?? (adjacentKey ? columnWidths[adjacentKey] : 0),
      delta,
    );
    persistColumnWidths();
  }

  function resetColumnWidth(key: DownloadColumnKey) {
    resizeColumn(key, DOWNLOAD_TABLE_COLUMNS[key].defaultWidth);
    persistColumnWidths();
  }

  function resetColumnWidths() {
    columnWidths = { ...DEFAULT_DOWNLOAD_COLUMN_WIDTHS };
    persistColumnWidths();
  }

  onMount(() => {
    try {
      const stored = JSON.parse(localStorage.getItem(DOWNLOAD_COLUMN_WIDTHS_STORAGE_KEY) ?? "null") as Partial<DownloadColumnWidths> | null;
      if (!stored) return;
      columnWidths = Object.fromEntries(Object.keys(DOWNLOAD_TABLE_COLUMNS).map((rawKey) => {
        const key = rawKey as DownloadColumnKey;
        return [key, clampDownloadColumnWidth(key, stored[key] ?? DEFAULT_DOWNLOAD_COLUMN_WIDTHS[key])];
      })) as DownloadColumnWidths;
    } catch {
      // Ignore malformed or unavailable local preferences and keep the design defaults.
    }
  });
</script>

<svelte:window onpointermove={continueColumnResize} onpointerup={finishColumnResize} onpointercancel={finishColumnResize} />

<section class={cn("manager-shell", resizing && "is-resizing")} aria-label="Download manager">
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
      />
      <ToggleGroup.Root type="single" bind:value={() => activeStatus, next => { if (next) activeStatus = next; }} variant="outline" spacing={2} size="sm" class="status-filters flex-wrap justify-start" aria-label="Filter downloads by status">
        {#each statusFilters as filter (filter.value)}
          {@const count = entries.filter(filter.match).length}
          <ToggleGroup.Item value={filter.value}>
            {filter.label}
            {#if filter.value !== "all" && count > 0}<span class="font-mono text-xs tabular-nums text-muted-foreground">{count}</span>{/if}
          </ToggleGroup.Item>
        {/each}
      </ToggleGroup.Root>
      <Button variant="ghost" size="sm" class="columns-reset" onclick={resetColumnWidths} title="Reset all column widths">
        <Columns3 data-icon="inline-start" /> Reset columns
      </Button>
    </div>
  </header>

  <div class="mobile-sort">
    <Select ariaLabel="Sort downloads by" options={sortOptions} value={sortKey} onchange={next => { if (Object.hasOwn(DOWNLOAD_TABLE_COLUMNS, next)) sortColumn(next as DownloadColumnKey); }} />
    <Button variant="outline" size="icon" aria-label={sortDirection === "asc" ? "Ascending order; switch to descending" : "Descending order; switch to ascending"} onclick={() => sortColumn(sortKey)}>
      {#if sortDirection === "asc"}<ArrowUp />{:else}<ArrowDown />{/if}
    </Button>
  </div>

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
          <ChevronsDownUp data-icon="inline-start" /> Collapse all
        {:else}
          <ChevronsUpDown data-icon="inline-start" /> Expand all
        {/if}
      </Button>
      {#if safeCheckedIds.size > 0}
        <Button
          variant="danger"
          size="sm"
          disabled={acting}
          onclick={() => onRemove([...safeCheckedIds])}
        >
          <Trash2 data-icon="inline-start" /> Remove
        </Button>
      {/if}
    </div>
  </div>

  {#if error}
    <div role="alert" class="manager-error">{error}</div>
  {/if}

  <div class="table-scroll">
    <div class="downloads-grid" role="treegrid" aria-label="Downloads" style:--download-grid-width={`${gridWidth}px`}>
      <DownloadTableHeader
        columnTemplate={gridTemplate}
        {sortKey}
        {sortDirection}
        onSort={sortColumn}
        onResizeStart={startColumnResize}
        onResizeNudge={nudgeColumnWidth}
        onResizeReset={resetColumnWidth}
      />

      {#if tree.length > 0}
        <DownloadTreeRows
          nodes={tree}
          {entriesById}
          expanded={effectiveExpanded}
          {selectedKey}
          checkedIds={safeCheckedIds}
          columnTemplate={gridTemplate}
          onToggleExpanded={toggleExpanded}
          {onSelect}
          onSetChecked={setChecked}
        />
      {:else if loading}
        <div class="manager-state"><Loader2 class="h-4 w-4 animate-spin" /> Loading downloads…</div>
      {:else}
        <Empty.Root class="min-h-40 rounded-none border-0 p-6">
          <Empty.Header>
            <Empty.Media variant="icon"><HardDriveDownload /></Empty.Media>
            <Empty.Title>{filtering ? "Nothing matches these filters" : "Nothing downloading"}</Empty.Title>
            <Empty.Description>{filtering ? "Try a different title, client, or status." : "Requested entities will appear here as soon as their acquisition begins."}</Empty.Description>
          </Empty.Header>
        </Empty.Root>
      {/if}
    </div>
  </div>
</section>

<style>
  .manager-shell {
    display: flex;
    min-height: 0;
    flex-direction: column;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-md, 8px) var(--radius-md, 8px) 0 0;
    background:
      linear-gradient(180deg, rgb(255 255 255 / 0.018), transparent 6rem),
      var(--color-surface-1);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.025), 0 12px 35px rgb(0 0 0 / 0.16);
  }
  .manager-shell.is-resizing { cursor: col-resize; user-select: none; }

  .manager-toolbar {
    display: flex;
    flex: none;
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

  .toolbar-controls { display: flex; min-width: 0; flex: 1 1 auto; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--spacing-control-gap); }
  :global(.download-search) { width: min(18rem, 28vw); flex-shrink: 0; }
  :global(.status-filters) { max-width: 100%; }
  .mobile-sort { display: none; }

  .selection-bar { display: flex; flex: none; min-height: 2.35rem; align-items: center; justify-content: space-between; gap: var(--spacing-control-gap); padding: calc(var(--spacing) * 2) calc(var(--spacing) * 3); border-bottom: 1px solid var(--color-border-subtle); color: var(--color-text-muted); background: var(--color-surface-1); font-size: var(--text-caption); }
  .selection-bar.has-selection { background: color-mix(in srgb, var(--color-accent-500) 5%, var(--color-surface-1)); }
  .selection-check, .selection-actions { display: flex; align-items: center; gap: 0.55rem; }

  .manager-error { border-bottom: 1px solid rgb(225 80 80 / 0.25); border-left: 2px solid var(--color-error); padding: 0.55rem 0.8rem; background: var(--color-error-muted); color: var(--color-error-text); font-size: 0.76rem; }
  .table-scroll { min-height: 0; flex: 1 1 auto; overflow: auto; scrollbar-gutter: stable; }
  .downloads-grid { width: 100%; min-width: var(--download-grid-width); }
  .manager-state { display: flex; min-height: 10rem; align-items: center; justify-content: center; gap: 0.5rem; color: var(--color-text-muted); font-size: 0.78rem; }
  @media (max-width: 960px) {
    .manager-toolbar { align-items: flex-start; flex-direction: column; }
    .toolbar-controls { width: 100%; flex-wrap: wrap; justify-content: flex-start; }
    :global(.download-search) { width: 100%; }
  }

  @media (max-width: 640px) {
    .manager-shell { overflow: auto; border-radius: var(--radius-md); }
    .manager-toolbar { gap: calc(var(--spacing) * 3); padding: calc(var(--spacing) * 3); }
    .toolbar-controls { gap: calc(var(--spacing) * 3); }
    .mobile-sort { display: flex; flex: none; gap: var(--spacing-control-gap); padding: 0 calc(var(--spacing) * 3) calc(var(--spacing) * 3); }
    .mobile-sort > :global(:first-child) { flex: 1; }
    :global(.columns-reset) { display: none; }
    .selection-bar { flex-wrap: wrap; }
    .selection-actions { flex-wrap: wrap; }
    .table-scroll { flex: none; overflow: visible; scrollbar-gutter: auto; }
    .downloads-grid { min-width: 0; }
  }
</style>
