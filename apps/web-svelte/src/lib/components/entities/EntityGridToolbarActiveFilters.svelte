<script lang="ts">
  import { RotateCcw, SlidersHorizontal, X } from "@lucide/svelte";
  import { cubicOut } from "svelte/easing";
  import { slide } from "svelte/transition";
  import { Button } from "@prismedia/ui-svelte";
  import type { EntityGridFilterOption } from "$lib/entities/entity-grid";

  interface Props {
    activeFilterIds: string[];
    activeFilters: EntityGridFilterOption[];
    canClearFilters: boolean;
    onActiveFilterIdsChange: (ids: string[]) => void;
    onClearFilters: () => void;
  }

  let {
    activeFilterIds,
    activeFilters,
    canClearFilters,
    onActiveFilterIdsChange,
    onClearFilters,
  }: Props = $props();

  function removeFilter(id: string) {
    onActiveFilterIdsChange(activeFilterIds.filter((filterId) => filterId !== id));
  }
</script>

<div class="filter-row toolbar-bar" transition:slide={{ duration: 200, easing: cubicOut }}>
  <div class="filter-scroll" aria-live="polite">
    {#if activeFilters.length > 0}
      <span class="filter-chip-label" aria-hidden="true">
        <SlidersHorizontal class="h-3 w-3 shrink-0" />
        ACTIVE
      </span>
      {#each activeFilters as option (option.id)}
        <Button variant="outline" size="xs" onclick={() => removeFilter(option.id)}>
          <span class="max-w-48 truncate">{option.label}</span>
          <X class="h-3 w-3" />
        </Button>
      {/each}
    {/if}
  </div>

  {#if canClearFilters}
    <Button
      variant="ghost"
      size="sm"
      title="Clear search and filters"
      aria-label="Clear search and filters"
      class="ml-auto shrink-0"
      onclick={onClearFilters}
    >
      <RotateCcw class="h-3.5 w-3.5 shrink-0" />
      Clear
    </Button>
  {/if}
</div>

<style>
  /* Active filters pull up behind the hero so the rows read as one panel. */
  .filter-row {
    z-index: 1;
    gap: 0.4rem;
    min-height: 2.1rem;
    padding: calc(0.4rem + var(--toolbar-bar-overlap)) 0.7rem 0.4rem;
    pointer-events: auto;
  }

  .filter-scroll {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    flex: 1 1 auto;
    min-width: 0;
    overflow-x: auto;
    scrollbar-width: thin;
  }




</style>
