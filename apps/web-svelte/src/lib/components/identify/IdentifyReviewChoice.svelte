<script lang="ts">
  import {
    ChevronLeft,
    ChevronRight,
    Film,
    Loader2,
    Search,
    Star,
    Wand,
    X,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { identifyEntity, type EntitySearchCandidate } from "$lib/api/identify";
  import type { EntityCard } from "$lib/api/prismedia";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    candidates: EntitySearchCandidate[];
  }

  let { entity, candidates }: Props = $props();

  const store = useIdentifyStore();
  const defaultProvider = $derived(store.providersForKind(entity.kind)[0] ?? null);

  let searchTitle = $state("");
  let searchYear = $state("");
  let searching = $state(false);
  let localCandidates = $state<EntitySearchCandidate[]>(candidates);

  async function handleSearch() {
    if (!defaultProvider || !searchTitle.trim()) return;
    searching = true;
    store.error = null;
    try {
      const result = await identifyEntity(entity.id, defaultProvider.id, {
        title: searchTitle.trim() || undefined,
      } as any);
      localCandidates = result.candidates;
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Search failed";
    } finally {
      searching = false;
    }
  }

  function pickCandidate(candidate: EntitySearchCandidate) {
    if (!defaultProvider) return;
    void store.identifyWithCandidate(entity, defaultProvider.id, candidate);
  }

  function candidateToCard(candidate: EntitySearchCandidate, index: number): EntityThumbnailCard {
    return {
      entity: {
        id: `candidate-${index}`,
        kind: entity.kind,
        title: candidate.title,
        parentEntityId: null,
        sortOrder: null,
        capabilities: [],
        childrenByKind: [],
        relationships: [],
      },
      aspectRatio: "poster",
      cover: candidate.posterUrl ? { src: candidate.posterUrl, alt: candidate.title } : null,
      hover: { kind: "none" },
      meta: [],
    };
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Back nav + context -->
  <div class="flex items-center gap-3">
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={() => store.navigateToDashboard()}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Back
    </button>
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1 rounded-xs border border-border-default bg-surface-2 px-2 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3"
      onclick={() => store.navigateToDashboard()}
    >
      Skip
    </button>
  </div>

  <!-- Entity context bar -->
  <div class="grid grid-cols-[auto_1fr_auto] items-center gap-4 rounded-sm border border-border-subtle border-l-2 border-l-warning bg-surface-1 p-3.5">
    <div class="grid h-11 w-11 place-items-center rounded-xs border border-dashed border-border-default bg-surface-3 text-text-disabled">
      <Film class="h-5 w-5" />
    </div>
    <div class="min-w-0">
      <div class="flex items-baseline gap-2">
        <span class="text-kicker">{entity.kind} · awaiting match</span>
        <span class="rounded-xs border border-warning/30 bg-warning-muted px-1.5 py-0.5 font-mono text-[0.6rem] text-warning-text">
          {localCandidates.length} candidates
        </span>
      </div>
      <h3 class="mt-1 truncate font-mono text-[1rem] font-medium">{entity.title}</h3>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Filename</span>
      <span class="text-[0.74rem] text-text-secondary">{entity.title}</span>
    </div>
  </div>

  <!-- Manual search panel -->
  <section class="surface-panel overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Search class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Query</span>
      <span class="font-mono text-[0.7rem] text-text-muted">refine and re-search</span>
    </header>
    <div class="flex flex-wrap items-center gap-2 p-3.5">
      <span class="font-mono text-[0.72rem] text-text-muted">title:</span>
      <input
        type="text"
        class="allow-compact-input-text min-w-[12rem] flex-1 rounded-xs border border-border-default bg-surface-1 px-2.5 py-1.5 text-[0.82rem] text-text-primary outline-none transition-colors focus:border-border-accent"
        placeholder="Search titles…"
        bind:value={searchTitle}
        onkeydown={(e) => { if (e.key === "Enter") void handleSearch(); }}
      />
      <span class="font-mono text-[0.72rem] text-text-muted">year:</span>
      <input
        type="text"
        class="allow-compact-input-text w-20 rounded-xs border border-border-default bg-surface-1 px-2.5 py-1.5 text-[0.82rem] text-text-primary outline-none transition-colors focus:border-border-accent"
        placeholder="optional"
        bind:value={searchYear}
      />
      {#if defaultProvider}
        <span class="font-mono text-[0.72rem] text-text-muted">provider:</span>
        <span class="rounded-xs border border-border-accent bg-accent-950/30 px-2 py-0.5 font-mono text-[0.66rem] text-text-accent">
          {defaultProvider.name}
        </span>
      {/if}
      <div class="flex-1"></div>
      <button
        type="button"
        class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-primary transition-colors hover:bg-surface-3"
        onclick={() => { searchTitle = ""; searchYear = ""; }}
      >
        <X class="h-3.5 w-3.5" />
        Clear
      </button>
      <button
        type="button"
        class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-3 text-[0.78rem] text-text-accent transition-colors hover:bg-accent-950/60 disabled:opacity-40"
        disabled={searching || !searchTitle.trim()}
        onclick={handleSearch}
      >
        {#if searching}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <Search class="h-3.5 w-3.5" />
        {/if}
        Search
      </button>
    </div>
  </section>

  <!-- Candidates grid -->
  <section class="surface-panel overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <span class="text-kicker text-text-accent">Candidates</span>
      <span class="font-mono text-[0.7rem] text-text-muted">{localCandidates.length} found</span>
    </header>
    <div class="grid grid-cols-2 gap-4 p-4 sm:grid-cols-3 lg:grid-cols-4">
      {#each localCandidates as candidate, i (candidate.title + i)}
        <div class="relative flex flex-col gap-2">
          {#if i === 0}
            <div class="absolute -top-2 left-2.5 z-10">
              <span class="inline-flex items-center gap-1 rounded-xs border border-border-accent bg-accent-950/60 px-1.5 py-0.5 font-mono text-[0.6rem] text-text-accent">
                <Star class="h-2.5 w-2.5" />
                Best
              </span>
            </div>
          {/if}

          <EntityThumbnail
            card={candidateToCard(candidate, i)}
            onActivate={() => pickCandidate(candidate)}
          />

          <p class="line-clamp-3 text-[0.74rem] leading-snug text-text-muted">
            {candidate.overview ?? ""}
          </p>

          <div class="flex items-center gap-2 text-[0.66rem] text-text-muted">
            {#if candidate.year}
              <span class="font-mono">{candidate.year}</span>
            {/if}
            {#if candidate.popularity}
              <span class="font-mono">★ {candidate.popularity}</span>
            {/if}
          </div>

          <div class="flex gap-1.5">
            <button
              type="button"
              class={cn(
                "inline-flex h-7 flex-1 items-center justify-center gap-1 rounded-xs border text-[0.72rem] font-medium transition-colors",
                i === 0
                  ? "border-border-accent-strong bg-accent-950/40 text-text-accent hover:bg-accent-950/60"
                  : "border-border-default bg-surface-2 text-text-primary hover:bg-surface-3",
              )}
              onclick={() => pickCandidate(candidate)}
              disabled={store.identifyingId !== null}
            >
              {#if store.identifyingId}
                <Loader2 class="h-3 w-3 animate-spin" />
              {/if}
              {i === 0 ? "Pick" : "Use"}
              <ChevronRight class="h-3 w-3" />
            </button>
          </div>
        </div>
      {/each}
    </div>
  </section>
</div>
