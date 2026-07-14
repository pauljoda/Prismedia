<script lang="ts">
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityDetailPosterSize } from "$lib/components/entities/EntityDetail.svelte";
  import { baseDetailCard, detailLabRows } from "$lib/entities/detail-lab-data";
  import type { EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { presentSections } from "$lib/entities/entity-detail";

  type LabTab = "base" | "examples";

  const exampleCards = detailLabRows.flatMap((row) => row.cards);

  let activeTab = $state<LabTab>("base");
  let exampleIndex = $state(0);
  let ratingBusy = $state(false);

  // ── Base tab controls ──────────────────────────────────
  type HeroSource = "banner" | "poster-blur" | "gradient";
  let heroSource = $state<HeroSource>("banner");
  let posterSize = $state<EntityDetailPosterSize>("medium");
  let hiddenSections = $state<Set<string>>(new Set());

  // ── Optimistic flag/rating state for the lab (no real backend) ──
  let ratingOverride = $state<number | null>(null);
  let favoriteOverride = $state<boolean | null>(null);
  let organizedOverride = $state<boolean | null>(null);

  const allSectionNames = [
    "description",
    "rating",
    "flags",
    "tags",
    "links",
    "files",
  ];

  const baseCard = $derived.by((): EntityDetailCardFull => {
    const card = { ...baseDetailCard };

    if (heroSource === "poster-blur") card.hero = null;
    if (heroSource === "gradient") { card.hero = null; card.poster = null; }

    if (hiddenSections.has("description")) card.description = null;
    if (hiddenSections.has("rating")) card.rating = null;
    if (hiddenSections.has("flags")) card.flags = [];
    if (hiddenSections.has("tags")) card.tags = [];
    if (hiddenSections.has("links")) card.links = [];
    if (hiddenSections.has("files")) card.files = [];

    return card;
  });

  const activeCard = $derived.by(() => {
    const raw = activeTab === "base" ? baseCard : exampleCards[exampleIndex];
    if (!raw) return raw;

    const card = { ...raw };

    if (ratingOverride !== null && card.rating) {
      card.rating = { ...card.rating, value: ratingOverride };
    }

    if (favoriteOverride !== null || organizedOverride !== null) {
      card.flags = card.flags.map((f) => {
        if (f.code === "favorite" && favoriteOverride !== null) return { ...f, active: favoriteOverride };
        if (f.code === "organized" && organizedOverride !== null) return { ...f, active: organizedOverride };
        return f;
      });
    }

    return card;
  });

  const sections = $derived(activeCard ? presentSections(activeCard) : []);

  function handleRatingChange(value: number | null) {
    ratingBusy = true;
    ratingOverride = value ?? 0;
    setTimeout(() => (ratingBusy = false), 400);
  }

  function handleFavoriteToggle() {
    const current = favoriteOverride ?? (activeCard?.flags.find((f) => f.code === "favorite")?.active ?? false);
    favoriteOverride = !current;
  }

  function handleOrganizedToggle() {
    const current = organizedOverride ?? (activeCard?.flags.find((f) => f.code === "organized")?.active ?? false);
    organizedOverride = !current;
  }

  function toggleSection(name: string) {
    const next = new Set(hiddenSections);
    if (next.has(name)) next.delete(name);
    else next.add(name);
    hiddenSections = next;
  }

  function showAllSections() {
    hiddenSections = new Set();
  }

  function hideAllSections() {
    hiddenSections = new Set(allSectionNames);
  }
</script>

<svelte:head>
  <title>Entity Detail Lab | Prismedia</title>
</svelte:head>

<main class="detail-lab">
  <header>
    <div>
      <p>entity surface</p>
      <h1>Entity Detail Lab</h1>
    </div>
    <div class="tab-row" aria-label="Lab mode">
      <button
        type="button"
        class:is-active={activeTab === "base"}
        onclick={() => (activeTab = "base")}
      >
        Base
      </button>
      <button
        type="button"
        class:is-active={activeTab === "examples"}
        onclick={() => (activeTab = "examples")}
      >
        Examples
      </button>
    </div>
  </header>

  <!-- Controls -->
  {#if activeTab === "base"}
    <div class="controls-panel">
      <div class="control-group">
        <span class="control-label">Hero Source</span>
        <div class="toggle-row">
          {#each ["banner", "poster-blur", "gradient"] as src (src)}
            <button
              type="button"
              class:is-active={heroSource === src}
              onclick={() => (heroSource = src as HeroSource)}
            >{src}</button>
          {/each}
        </div>
      </div>

      <div class="control-group">
        <span class="control-label">Poster Size</span>
        <div class="toggle-row">
          {#each ["none", "small", "medium", "large"] as size (size)}
            <button
              type="button"
              class:is-active={posterSize === size}
              onclick={() => (posterSize = size as EntityDetailPosterSize)}
            >{size}</button>
          {/each}
        </div>
      </div>

      <div class="control-group sections-control">
        <div class="control-label-row">
          <span class="control-label">Sections</span>
          <div class="toggle-row">
            <button type="button" onclick={showAllSections}>All on</button>
            <button type="button" onclick={hideAllSections}>All off</button>
          </div>
        </div>
        <div class="section-toggles">
          {#each allSectionNames as name (name)}
            <button
              type="button"
              class="section-chip"
              class:is-hidden={hiddenSections.has(name)}
              onclick={() => toggleSection(name)}
            >{name}</button>
          {/each}
        </div>
      </div>
    </div>
  {:else}
    <div class="controls-panel">
      <div class="control-group">
        <span class="control-label">Entity Kind</span>
        <div class="toggle-row">
          {#each exampleCards as c, i (c.entity.id)}
            <button
              type="button"
              class:is-active={i === exampleIndex}
              onclick={() => (exampleIndex = i)}
            >{c.kindLabel}</button>
          {/each}
        </div>
      </div>
    </div>
  {/if}

  <section class="status-strip" aria-label="Detail state">
    <span>{activeCard?.entity.kind ?? "—"}</span>
    <span>{sections.length} sections</span>
    <span>{activeCard?.presentCapabilities.length ?? 0} capabilities</span>
    {#if activeTab === "base"}
      <span>{hiddenSections.size} hidden</span>
    {/if}
  </section>

  {#if activeCard}
    <EntityDetail
      card={activeCard}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      {ratingBusy}
      {posterSize}
      showHero={true}
    />
  {/if}
</main>

<style>
  .detail-lab {
    min-height: 100vh;
    background: var(--color-bg, #07080b);
    color: var(--color-text-primary, #f2eed8);
    padding: clamp(1rem, 3vw, 2rem);
  }

  header {
    display: flex;
    align-items: end;
    justify-content: space-between;
    gap: 1rem;
    padding-bottom: 0.75rem;
    border-bottom: 1px solid var(--color-border-subtle, #1c2235);
    flex-wrap: wrap;
  }

  header p {
    margin: 0;
    color: var(--color-text-accent, #c49a5a);
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

  /* ── Tab row ────────────────────────────────────────────── */

  .tab-row {
    display: flex;
    gap: 0.3rem;
  }

  .tab-row button {
    padding: 0.35rem 0.75rem;
    font-size: 0.78rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-weight: 600;
    letter-spacing: 0.03em;
    text-transform: uppercase;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s, box-shadow 0.15s;
  }

  .tab-row button:hover {
    color: var(--color-text-primary, #f2eed8);
    border-color: rgba(199, 201, 204, 0.35);
  }

  .tab-row button.is-active {
    color: #c49a5a;
    border-color: #c49a5a;
    box-shadow: 0 0 12px rgba(199, 201, 204, 0.2);
  }

  /* ── Controls panel ─────────────────────────────────────── */

  .controls-panel {
    display: grid;
    gap: 0.75rem;
    margin-top: 0.75rem;
    padding: 0.85rem 1rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
  }

  .control-group {
    display: grid;
    gap: 0.4rem;
  }

  .control-label {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.65rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--color-text-muted, #8a93a6);
  }

  .control-label-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
  }

  .toggle-row {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
  }

  .toggle-row button {
    padding: 0.22rem 0.5rem;
    font-size: 0.68rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s, box-shadow 0.15s;
  }

  .toggle-row button:hover {
    color: var(--color-text-primary, #f2eed8);
    border-color: rgba(199, 201, 204, 0.35);
  }

  .toggle-row button.is-active {
    color: #c49a5a;
    border-color: #c49a5a;
    box-shadow: 0 0 10px rgba(199, 201, 204, 0.15);
  }

  /* ── Section toggles ────────────────────────────────────── */

  .sections-control {
    border-top: 1px solid color-mix(in srgb, var(--color-border, #1c2235) 50%, transparent);
    padding-top: 0.65rem;
  }

  .section-toggles {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
  }

  .section-chip {
    padding: 0.2rem 0.45rem;
    font-size: 0.65rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    border: 1px solid rgba(78, 138, 98, 0.4);
    background: rgba(78, 138, 98, 0.1);
    color: #80b898;
    cursor: pointer;
    transition: all 0.15s;
  }

  .section-chip:hover {
    border-color: rgba(78, 138, 98, 0.6);
    background: rgba(78, 138, 98, 0.18);
  }

  .section-chip.is-hidden {
    border-color: rgba(168, 72, 80, 0.4);
    background: rgba(168, 72, 80, 0.08);
    color: #cc7880;
    text-decoration: line-through;
  }

  .section-chip.is-hidden:hover {
    border-color: rgba(168, 72, 80, 0.6);
    background: rgba(168, 72, 80, 0.15);
  }

  /* ── Status strip ───────────────────────────────────────── */

  .status-strip {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem 1.2rem;
    margin: 0.75rem 0;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    color: var(--color-text-muted, #8a93a6);
    text-transform: uppercase;
    letter-spacing: 0.03em;
  }
</style>
