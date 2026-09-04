<script lang="ts">
  import { ArrowDownWideNarrow, ArrowUpNarrowWide, Shuffle } from "@lucide/svelte";
  import { Button, SearchInput, Select } from "@prismedia/ui-svelte";
  import { ENTITY_SORT_DIRECTION } from "$lib/api/generated/codes";
  import { isTaxonomyEntityKind } from "$lib/entities/entity-codes";
  import { ENTITY_GRID_SORT, type EntityGridSort, type EntityGridSortDir } from "$lib/entities/entity-grid";

  interface Props {
    entityKind?: string;
    onQueryChange: (query: string) => void;
    onReshuffle: () => void;
    onSortByChange: (sortBy: EntityGridSort) => void;
    onSortDirChange: (sortDir: EntityGridSortDir) => void;
    query: string;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
  }

  let { entityKind, onQueryChange, onReshuffle, onSortByChange, onSortDirChange, query, sortBy, sortDir }: Props = $props();
  const options = $derived<{ value: EntityGridSort; label: string }[]>([
    { value: ENTITY_GRID_SORT.title, label: "Title" },
    { value: ENTITY_GRID_SORT.added, label: "Date added" },
    ...(entityKind != null && isTaxonomyEntityKind(entityKind)
      ? [{ value: ENTITY_GRID_SORT.references, label: "References" }] : []),
    { value: ENTITY_GRID_SORT.rating, label: "Rating" },
    { value: ENTITY_GRID_SORT.random, label: "Random" },
    { value: ENTITY_GRID_SORT.kind, label: "Kind" },
    { value: ENTITY_GRID_SORT.position, label: "Position" },
  ]);
  const ascending = $derived(sortDir === ENTITY_SORT_DIRECTION.ascending);
</script>

<div class="search-row">
  <SearchInput value={query} ariaLabel="Search the library" placeholder="Search the library…"
    class="search-box" searchIconClass="search-icon" clearButtonClass="search-clear" clearIconClass="search-clear-icon" oninput={(event) => onQueryChange(event.currentTarget.value)} onClear={() => onQueryChange("")} />
  <div class="search-sort">
    <Select size="sm" class="w-auto max-[519px]:max-w-24" {options} value={sortBy} ariaLabel="Sort by" onchange={(next) => {
      const option = options.find((option) => option.value === next);
      if (option) onSortByChange(option.value);
    }} />
    {#if sortBy === ENTITY_GRID_SORT.random}
      <Button variant="ghost" size="icon" class="ctrl-btn ctrl-icon" aria-label="Reshuffle the random order" title="Reshuffle" onclick={onReshuffle}>
        <Shuffle class="size-4" />
      </Button>
    {:else}
      <Button variant="ghost" size="icon" class="ctrl-btn ctrl-icon" aria-label={ascending ? "Sort ascending; switch to descending" : "Sort descending; switch to ascending"}
        title={ascending ? "Ascending order; switch to descending" : "Descending order; switch to ascending"}
        onclick={() => onSortDirChange(ascending ? ENTITY_SORT_DIRECTION.descending : ENTITY_SORT_DIRECTION.ascending)}>
        {#if ascending}<ArrowUpNarrowWide class="size-3.5" />{:else}<ArrowDownWideNarrow class="size-3.5" />{/if}
      </Button>
    {/if}
  </div>
</div>

<style>
  .search-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
  }

  /* Compact sort controls tucked to the right of the search box. */
  .search-sort {
    display: flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.35rem;
  }

  .search-row :global(.search-box) {
    position: relative;
    display: flex;
    flex: 1 1 auto;
    align-items: center;
    gap: 0.55rem;
    min-width: 0;
    height: 2.1rem;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    padding: 0 0.65rem;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .search-row :global(.search-box:focus-within) {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30), 0 0 0 1px rgba(199, 201, 204,0.35), 0 0 8px rgba(199, 201, 204,0.15);
  }

  .search-row :global(.search-box .search-icon) {
    width: 0.95rem;
    height: 0.95rem;
    color: var(--color-text-disabled);
    flex-shrink: 0;
  }

  .search-row :global(.search-box:focus-within .search-icon) {
    color: var(--color-text-accent);
  }

  .search-row :global(.search-box input) {
    min-width: 0;
    width: 100%;
    border: 0;
    background: transparent;
    color: var(--color-text-primary);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.875rem;
    letter-spacing: 0;
    outline: 0;
  }

  .search-row :global(.search-box input::placeholder) {
    color: var(--color-text-disabled);
    font-style: italic;
  }

  /* Hide the native WebKit/Chromium search clear so it doesn't collide with our
     own neutral accent-styled clear button. */
  .search-row :global(.search-box input::-webkit-search-cancel-button),
  .search-row :global(.search-box input::-webkit-search-decoration) {
    appearance: none;
    -webkit-appearance: none;
    display: none;
  }

  .search-row :global(.search-clear) {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 1.25rem;
    height: 1.25rem;
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-disabled);
    flex-shrink: 0;
    transition:
      color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default);
  }

  .search-row :global(.search-clear:hover) {
    color: var(--color-text-accent);
    border-color: rgb(199 201 204 / 0.3);
  }

  .search-row :global(.search-clear-icon) {
    width: 0.75rem;
    height: 0.75rem;
  }
</style>
