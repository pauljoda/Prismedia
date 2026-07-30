<script lang="ts">
  import {
    ArrowUpDown,
    Check,
    ChevronDown,
    Search,
    Shuffle,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import { isTaxonomyEntityKind } from "$lib/entities/entity-codes";
  import type { EntityGridSort, EntityGridSortDir } from "$lib/entities/entity-grid";

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

  let {
    entityKind,
    onQueryChange,
    onReshuffle,
    onSortByChange,
    onSortDirChange,
    query,
    sortBy,
    sortDir,
  }: Props = $props();

  const SORT_LABELS: Record<EntityGridSort, string> = {
    title: "Title",
    added: "Date added",
    rating: "Rating",
    random: "Random",
    kind: "Kind",
    position: "Position",
    references: "References",
  };

  // Reference-count sort only applies to taxonomy kinds (tags/people/studios), which are the
  // targets of relationship links; it is the default sort for those grids.
  const SORT_OPTIONS = $derived<{ value: EntityGridSort; label: string }[]>([
    { value: "title", label: "Title" },
    { value: "added", label: "Date added" },
    ...(entityKind != null && isTaxonomyEntityKind(entityKind)
      ? [{ value: "references" as const, label: "References" }]
      : []),
    { value: "rating", label: "Rating" },
    { value: "random", label: "Random" },
    { value: "kind", label: "Kind" },
    { value: "position", label: "Position" },
  ]);

  let sortOpen = $state(false);
</script>

<div class="search-row">
  <label class="search-box">
    <Search class="search-icon" aria-hidden="true" />
    <input
      type="search"
      placeholder="Search the library…"
      value={query}
      oninput={(event) => onQueryChange((event.currentTarget as HTMLInputElement).value)}
    />
    {#if query}
      <button
        type="button"
        class="search-clear"
        title="Clear search"
        aria-label="Clear search"
        onclick={() => onQueryChange("")}
      >
        <X class="h-3 w-3" />
      </button>
    {/if}
  </label>

  <div class="search-sort">
    <div class="relative">
      <button
        type="button"
        class="ctrl-btn ctrl-sort"
        onclick={() => (sortOpen = !sortOpen)}
      >
        <ArrowUpDown class="h-3.5 w-3.5" />
        <span class="ctrl-label">{SORT_LABELS[sortBy]}</span>
        <ChevronDown class="h-3 w-3 text-text-disabled" />
      </button>

      {#if sortOpen}
        <button
          type="button"
          class="fixed inset-0 z-40"
          aria-label="Close sort menu"
          onclick={() => (sortOpen = false)}
        ></button>
        <div class="floating-surface sort-menu sort-menu-end" use:keepFlyoutOnScreen>
          {#each SORT_OPTIONS as opt (opt.value)}
            <button
              type="button"
              class={cn("sort-menu-item", sortBy === opt.value && "is-active")}
              onclick={() => {
                onSortByChange(opt.value);
                sortOpen = false;
              }}
            >
              <Check class={cn("h-3 w-3", sortBy === opt.value ? "opacity-100" : "opacity-0")} />
              {opt.label}
            </button>
          {/each}
        </div>
      {/if}
    </div>

    {#if sortBy === "random"}
      <button
        type="button"
        class="ctrl-btn ctrl-icon"
        title="Reshuffle"
        aria-label="Reshuffle the random order"
        onclick={() => onReshuffle()}
      >
        <Shuffle class="h-3.5 w-3.5" />
      </button>
    {:else}
      <button
        type="button"
        class="ctrl-btn ctrl-icon"
        title={sortDir === "asc" ? "Ascending — click to reverse" : "Descending — click to reverse"}
        aria-label={`Sort direction: ${sortDir}`}
        onclick={() => onSortDirChange(sortDir === "asc" ? "desc" : "asc")}
      >
        <ChevronDown class={cn("h-3.5 w-3.5 dir-arrow", sortDir === "asc" && "is-up")} />
      </button>
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

  /* Anchor the dropdown to the right since the trigger sits near the edge. */
  .sort-menu-end {
    left: auto;
    right: 0;
  }

  .search-box {
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

  .search-box:focus-within {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30), 0 0 0 1px rgba(199, 201, 204,0.35), 0 0 8px rgba(199, 201, 204,0.15);
  }

  .search-box :global(.search-icon) {
    width: 0.95rem;
    height: 0.95rem;
    color: var(--color-text-disabled);
    flex-shrink: 0;
  }

  .search-box:focus-within :global(.search-icon) {
    color: var(--color-text-accent);
  }

  .search-box input {
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

  .search-box input::placeholder {
    color: var(--color-text-disabled);
    font-style: italic;
  }

  /* Hide the native WebKit/Chromium search clear so it doesn't collide with our
     own neutral accent-styled clear button. */
  .search-box input::-webkit-search-cancel-button,
  .search-box input::-webkit-search-decoration {
    appearance: none;
    -webkit-appearance: none;
    display: none;
  }

  .search-clear {
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

  .search-clear:hover {
    color: var(--color-text-accent);
    border-color: rgb(199 201 204 / 0.3);
  }

  .sort-menu {
    position: absolute;
    left: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    min-width: 10rem;
    padding: 0.3rem 0;
    overflow: hidden;
  }

  .sort-menu-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: calc(100% - 0.4rem);
    margin: 0 0.2rem;
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    border: 1px solid transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background-color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
  }

  .sort-menu-item:hover {
    background: rgb(255 255 255 / 0.04);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    color: var(--color-text-primary);
  }

  .sort-menu-item.is-active {
    background: var(--color-surface-2);
    border-color: var(--color-border-default);
    color: var(--color-text-primary);
    box-shadow: inset 2px 0 0 var(--entity-accent, var(--color-accent-500));
  }
</style>
