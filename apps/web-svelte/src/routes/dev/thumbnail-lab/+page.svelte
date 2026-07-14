<script lang="ts">
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import type { EntityGridRequest } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { thumbnailLabRows } from "$lib/entities/thumbnail-lab-data";

  type LabState = "hydrated" | "loading" | "empty";

  const LAB_PAGE_SIZE = 250;
  const hydratedCards: EntityThumbnailCard[] = interleaveRows(thumbnailLabRows.map((row) => row.cards));
  const bulkActions = [
    { id: "review", label: "Mark reviewed", onRun: () => undefined },
    { id: "queue", label: "Queue preview", onRun: () => undefined },
  ];

  let labState = $state<LabState>("hydrated");
  let visibleCount = $state(LAB_PAGE_SIZE);
  let renderedCount = $state(0);
  let selectedIds = $state<string[]>([]);
  let lastRequest = $state<EntityGridRequest | null>(null);

  const cards = $derived(labState === "empty" ? [] : hydratedCards.slice(0, visibleCount));
  const hasMore = $derived(labState === "hydrated" && visibleCount < hydratedCards.length);
  const isLoading = $derived(labState === "loading");
  const loadedCount = $derived(cards.length);

  function interleaveRows(rows: EntityThumbnailCard[][]): EntityThumbnailCard[] {
    const maxLength = Math.max(...rows.map((row) => row.length));
    const results: EntityThumbnailCard[] = [];

    for (let index = 0; index < maxLength; index += 1) {
      for (const row of rows) {
        const card = row[index];
        if (card) results.push(card);
      }
    }

    return results;
  }

  function setLabState(state: LabState) {
    labState = state;
    visibleCount = state === "hydrated" ? LAB_PAGE_SIZE : 0;
    selectedIds = [];
  }

  async function loadMore() {
    if (!hasMore) return;
    visibleCount = Math.min(visibleCount + LAB_PAGE_SIZE, hydratedCards.length);
  }
</script>

<svelte:head>
  <title>Entity Grid Lab | Prismedia</title>
</svelte:head>

<main class="entity-grid-lab">
  <header>
    <div>
      <p>entity surface</p>
      <h1>Entity Grid Lab</h1>
    </div>
    <div class="state-switch" aria-label="Server state">
      <button
        type="button"
        class={labState === "hydrated" ? "is-active" : undefined}
        onclick={() => setLabState("hydrated")}
      >
        Hydrated
      </button>
      <button
        type="button"
        class={labState === "loading" ? "is-active" : undefined}
        onclick={() => setLabState("loading")}
      >
        Loading
      </button>
      <button
        type="button"
        class={labState === "empty" ? "is-active" : undefined}
        onclick={() => setLabState("empty")}
      >
        Empty
      </button>
    </div>
  </header>

  <section class="status-strip" aria-label="Grid state">
    <span>{hydratedCards.length} fixture entities</span>
    <span>{loadedCount} loaded / {hydratedCards.length}</span>
    <span>{renderedCount} rendered</span>
    <span>{thumbnailLabRows.length} entity kinds</span>
    <span>{selectedIds.length} selected</span>
    {#if lastRequest}
      <span>{lastRequest.kind ?? "all"} · {lastRequest.sortBy} {lastRequest.sortDir}</span>
    {/if}
  </section>

  <EntityGrid
    {cards}
    {bulkActions}
    loading={isLoading}
    hasMore={hasMore}
    loadingMore={false}
    remoteTotalCount={hydratedCards.length}
    onLoadMore={loadMore}
    prefsKey="thumbnail-lab-entity-grid-surface"
    minScale={2}
    maxScale={12}
    emptyTitle="Nothing present"
    emptyMessage={labState === "empty" ? "There are no items to show." : "Try adjusting your search or filters."}
    onRequestChange={(request) => (lastRequest = request)}
    onRenderedCountChange={(count) => (renderedCount = count)}
    onSelectionChange={(ids) => (selectedIds = ids)}
  />
</main>

<style>
  .entity-grid-lab {
    min-height: 100vh;
    background: var(--color-bg);
    color: var(--color-text-primary);
    padding: clamp(1rem, 3vw, 2rem);
  }

  header {
    display: flex;
    align-items: end;
    justify-content: space-between;
    gap: 1rem;
    padding-bottom: 0.75rem;
  }

  header p {
    margin: 0;
    color: var(--color-text-accent);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  h1 {
    margin: 0.2rem 0 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: clamp(1.7rem, 3vw, 2.5rem);
    line-height: 1;
    letter-spacing: 0;
  }

  .state-switch {
    display: inline-flex;
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    box-shadow: inset 0 2px 8px rgb(0 0 0 / 0.3);
  }

  .state-switch button {
    border: 0;
    border-right: 1px solid var(--color-border-subtle);
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    padding: 0.58rem 0.72rem;
    text-transform: uppercase;
  }

  .state-switch button:last-child {
    border-right: 0;
  }

  .state-switch button:hover {
    color: var(--color-text-primary);
  }

  .state-switch button.is-active {
    background: var(--color-accent-950);
    color: var(--color-text-accent);
    box-shadow: inset 0 0 0 1px rgb(199 201 204 / 0.32);
  }

  .status-strip {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    margin: 0.7rem 0 1rem;
  }

  .status-strip span {
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    padding: 0.45rem 0.58rem;
    white-space: nowrap;
  }

  @media (max-width: 640px) {
    .entity-grid-lab {
      padding: 0.9rem;
    }

    header {
      align-items: start;
      flex-direction: column;
    }

    .state-switch {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      width: 100%;
    }
  }
</style>
