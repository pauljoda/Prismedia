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

<div class="library-search">
  <SearchInput value={query} ariaLabel="Search the library" placeholder="Search the library…"
    class="min-w-0 flex-1" oninput={(event) => onQueryChange(event.currentTarget.value)} onClear={() => onQueryChange("")} />
  <div class="library-sort">
    <Select {options} value={sortBy} ariaLabel="Sort by" onchange={(next) => {
      const option = options.find((option) => option.value === next);
      if (option) onSortByChange(option.value);
    }} />
    {#if sortBy === ENTITY_GRID_SORT.random}
      <Button variant="ghost" size="icon" aria-label="Reshuffle the random order" title="Reshuffle" onclick={onReshuffle}>
        <Shuffle class="size-4" />
      </Button>
    {:else}
      <Button variant="ghost" size="icon" aria-label={ascending ? "Sort ascending; switch to descending" : "Sort descending; switch to ascending"}
        title={ascending ? "Ascending" : "Descending"}
        onclick={() => onSortDirChange(ascending ? ENTITY_SORT_DIRECTION.descending : ENTITY_SORT_DIRECTION.ascending)}>
        {#if ascending}<ArrowUpNarrowWide class="size-4" />{:else}<ArrowDownWideNarrow class="size-4" />{/if}
      </Button>
    {/if}
  </div>
</div>

<style>
  .library-search { display: flex; flex: 1 1 28rem; align-items: center; gap: 0.5rem; min-width: 0; flex-wrap: wrap; }
  .library-sort { display: flex; align-items: center; gap: 0.25rem; flex: 0 0 10.5rem; }
  .library-search :global(.surface-well) { min-width: 9rem; }
</style>
