<script lang="ts">
  import { RotateCcw, SlidersHorizontal, X } from "@lucide/svelte";
  import { cubicOut } from "svelte/easing";
  import { slide } from "svelte/transition";
  import type { EntityGridFilterOption } from "$lib/entities/entity-grid";

  interface Props {
    activeFilterIds: string[];
    activeFilters: EntityGridFilterOption[];
    canClearFiltersAndSort: boolean;
    onActiveFilterIdsChange: (ids: string[]) => void;
    onClearFiltersAndSort: () => void;
  }

  let {
    activeFilterIds,
    activeFilters,
    canClearFiltersAndSort,
    onActiveFilterIdsChange,
    onClearFiltersAndSort,
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
        <button type="button" class="filter-chip" onclick={() => removeFilter(option.id)}>
          <span>{option.label}</span>
          <X class="h-3 w-3" />
        </button>
      {/each}
    {/if}
  </div>

  {#if canClearFiltersAndSort}
    <button
      type="button"
      title="Clear filters, sort, search, and saved preferences"
      class="ctrl-btn ctrl-clear filter-reset"
      onclick={onClearFiltersAndSort}
    >
      <RotateCcw class="h-3.5 w-3.5 shrink-0" />
      <span class="ctrl-label">Clear</span>
    </button>
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

  .filter-reset {
    flex: 0 0 auto;
    height: 1.6rem;
    min-height: 1.6rem;
    margin-left: auto;
  }

  .filter-chip-label {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    flex-shrink: 0;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    font-weight: 600;
    letter-spacing: 0.16em;
  }

  .filter-chip {
    display: inline-flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.4rem;
    height: 1.6rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    line-height: 1;
    padding: 0 0.6rem;
    transition:
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default);
  }

  .filter-chip:hover {
    border-color: var(--color-error-border, rgba(168, 72, 80, 0.4));
    background: var(--color-surface-3, #151a28);
    color: var(--color-error-text, #cc7880);
    box-shadow: 0 0 0 1px rgba(168, 72, 80, 0.3), 0 0 8px rgba(168, 72, 80, 0.15);
  }

  .filter-chip span {
    max-width: 12rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
</style>
