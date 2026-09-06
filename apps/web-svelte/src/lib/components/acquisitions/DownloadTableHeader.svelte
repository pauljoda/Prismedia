<script lang="ts">
  import { ArrowDown, ArrowUp, ChevronsUpDown, GripVertical } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import {
    DOWNLOAD_TABLE_COLUMNS,
    type DownloadColumnKey,
    type DownloadSortDirection,
  } from "./download-table";

  let {
    columnTemplate,
    sortKey,
    sortDirection,
    onSort,
    onResizeStart,
    onResizeNudge,
    onResizeReset,
  }: {
    columnTemplate: string;
    sortKey: DownloadColumnKey;
    sortDirection: DownloadSortDirection;
    onSort: (key: DownloadColumnKey) => void;
    onResizeStart: (event: PointerEvent, key: DownloadColumnKey) => void;
    onResizeNudge: (event: KeyboardEvent, key: DownloadColumnKey, delta: number) => void;
    onResizeReset: (key: DownloadColumnKey) => void;
  } = $props();

  function resizeKeydown(event: KeyboardEvent, key: DownloadColumnKey) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    event.stopPropagation();
    onResizeNudge(event, key, event.key === "ArrowLeft" ? -12 : 12);
  }
</script>

<div class="table-head" role="row" style:grid-template-columns={columnTemplate}>
  <div role="columnheader" aria-label="Select"></div>
  {#each Object.entries(DOWNLOAD_TABLE_COLUMNS) as [rawKey, definition] (rawKey)}
    {@const key = rawKey as DownloadColumnKey}
    <div
      role="columnheader"
      aria-sort={sortKey === key ? (sortDirection === "asc" ? "ascending" : "descending") : "none"}
      class={key !== "entity" && key !== "status" ? "numeric-head" : undefined}
    >
      <Button
        variant="ghost"
        class="column-sort"
        aria-label={`Sort by ${definition.label}`}
        onclick={() => onSort(key)}
      >
        <span>{definition.label}</span>
        {#if sortKey === key}
          {#if sortDirection === "asc"}<ArrowUp class="h-3 w-3" />{:else}<ArrowDown class="h-3 w-3" />{/if}
        {:else}
          <ChevronsUpDown class="sort-idle h-3 w-3" />
        {/if}
      </Button>
      <Button
        variant="ghost"
        size="icon"
        class="resize-handle"
        aria-label={`Resize ${definition.label} column`}
        title={`Drag to resize ${definition.label}; double-click to reset`}
        onpointerdown={(event) => onResizeStart(event, key)}
        ondblclick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          onResizeReset(key);
        }}
        onkeydown={(event) => resizeKeydown(event, key)}
      >
        <GripVertical class="h-3.5 w-3.5" />
      </Button>
    </div>
  {/each}
</div>

<style>
  .table-head {
    position: sticky;
    top: 0;
    z-index: 5;
    display: grid;
    min-width: 100%;
    border-bottom: 1px solid var(--color-border-default);
    background: rgb(12 12 13 / 0.98);
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.28);
  }
  [role="columnheader"] {
    position: relative;
    display: flex;
    min-width: 0;
    align-items: stretch;
    border-right: 1px solid rgb(255 255 255 / 0.045);
    color: var(--color-text-muted);
  }
  [role="columnheader"]:last-child { border-right: 0; }
  [aria-sort]:not([aria-sort="none"]) { box-shadow: inset 0 -2px 0 var(--color-accent-400); }
  :global(.column-sort) {
    min-width: 0;
    height: auto;
    flex: 1 1 auto;
    justify-content: flex-start;
    padding: 0.42rem 1.1rem 0.42rem 0.7rem;
    border-radius: 0;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    font-weight: 600;
    letter-spacing: 0.07em;
    text-transform: uppercase;
  }
  .numeric-head :global(.column-sort) { justify-content: flex-end; }
  :global(.column-sort:hover), [aria-sort]:not([aria-sort="none"]) :global(.column-sort) { color: var(--color-text-primary); background: rgb(255 255 255 / 0.025); }
  :global(.column-sort span) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  :global(.sort-idle) { opacity: 0; transition: opacity 120ms ease; }
  [role="columnheader"]:hover :global(.sort-idle), :global(.column-sort:focus-visible .sort-idle) { opacity: 0.48; }
  :global(.resize-handle) {
    position: absolute;
    top: 0;
    right: -0.45rem;
    z-index: 3;
    width: 0.9rem;
    height: 100%;
    padding: 0;
    border-radius: 0;
    color: transparent;
    cursor: col-resize;
    touch-action: none;
  }
  :global(.resize-handle:hover), :global(.resize-handle:focus-visible) { color: var(--color-text-muted); background: rgb(255 255 255 / 0.06); }
  @media (max-width: 640px) {
    .table-head { display: none; }
  }
</style>
