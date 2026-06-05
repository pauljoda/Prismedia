<script lang="ts">
  import {
    ChevronRight,
    Layers,
    Loader2,
    RefreshCw,
    Search,
    Star,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import IdentifyProviderSelect from "./IdentifyProviderSelect.svelte";
  import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";
  import type { EntitySearchCandidate } from "$lib/api/identify-types";
  import type { EntityCard } from "$lib/api/entities";
  import { identifyCandidateKey } from "./identify-candidate-card";
  import { entityKindIcon } from "./identify-icons";
  import { aspectRatioForKind, toAspectRatioValue } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    entity: EntityCard;
    candidates: EntitySearchCandidate[];
    providerId?: string | null;
  }

  let { entity, candidates, providerId = null }: Props = $props();

  const store = useIdentifyStore();
  const candidateAspect = $derived(toAspectRatioValue(aspectRatioForKind(entity.kind)));
  const CandidateKindIcon = $derived(entityKindIcon(entity.kind));
  let searchTitle = $state("");
  let searchYear = $state("");
  let selectedProviderId = $state<string | null>(null);
  let searchedProviderId = $state<string | null>(null);
  let searching = $state(false);
  let rescanning = $state(false);
  let checkingCandidateKey = $state<string | null>(null);
  let checkingCandidateTitle = $state<string | null>(null);
  let searchedCandidates = $state<EntitySearchCandidate[] | null>(null);

  const providerOptions = $derived(store.providersForKind(entity.kind));
  const activeProviderId = $derived(selectedProviderId ?? providerId ?? providerOptions[0]?.id ?? "");
  const activeProvider = $derived(
    providerOptions.find((provider) => provider.id === activeProviderId) ?? null,
  );
  const candidateProvider = $derived(
    (searchedProviderId ? store.providers.find((provider) => provider.id === searchedProviderId) : null) ??
      (providerId ? store.providers.find((provider) => provider.id === providerId) : null) ??
      activeProvider,
  );
  const localCandidates = $derived(searchedCandidates ?? candidates);

  // Navigating between items reuses this component instance, so local search state must be cleared
  // when the entity changes — otherwise a previous item's searched candidates and query stick around.
  let lastEntityId = $state<string | null>(null);
  $effect(() => {
    if (entity.id === lastEntityId) return;
    lastEntityId = entity.id;
    searchedCandidates = null;
    searchedProviderId = null;
    searchTitle = "";
    searchYear = "";
    selectedProviderId = null;
    checkingCandidateKey = null;
    checkingCandidateTitle = null;
  });

  async function handleRescan() {
    if (!activeProvider || rescanning) return;
    rescanning = true;
    store.error = null;
    try {
      const result = await store.identifyEntity(entity, activeProvider.id);
      if (result?.state === "search") {
        searchedCandidates = result.candidates;
        searchedProviderId = result.provider ?? activeProvider.id;
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Rescan failed";
    } finally {
      rescanning = false;
    }
  }

  async function handleSearch() {
    if (!activeProvider || !searchTitle.trim()) return;
    searching = true;
    store.error = null;
    try {
      const result = await store.identifyEntity(entity, activeProvider.id, {
        title: searchTitle.trim() || undefined,
      });
      if (result?.state === "search") {
        searchedCandidates = result.candidates;
        searchedProviderId = result.provider ?? activeProvider.id;
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Search failed";
    } finally {
      searching = false;
    }
  }

  async function pickCandidate(candidate: EntitySearchCandidate, candidateKey: string) {
    if (!candidateProvider || store.identifyingId !== null) return;
    checkingCandidateKey = candidateKey;
    checkingCandidateTitle = candidate.title;
    try {
      await store.identifyWithCandidate(entity, candidateProvider.id, candidate);
    } finally {
      if (checkingCandidateKey === candidateKey) {
        checkingCandidateKey = null;
        checkingCandidateTitle = null;
      }
    }
  }

  function candidateActionLabel(candidate: EntitySearchCandidate): string {
    return `Use ${candidateTitle(candidate)}${candidate.year ? ` (${candidate.year})` : ""}`;
  }

  function handleCandidateKeydown(event: KeyboardEvent, candidate: EntitySearchCandidate, candidateKey: string) {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    void pickCandidate(candidate, candidateKey);
  }

  function candidateTitle(candidate: EntitySearchCandidate): string {
    return candidate.title?.trim() || "Untitled match";
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Preview of what we are identifying (collapsed by default) -->
  <IdentifyTargetPreview {entity} />

  <!-- Entity context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if entity.coverUrl}
      <img src={entity.coverUrl} alt="" class="h-16 w-11 rounded-xs object-cover" decoding="async" />
    {:else}
      <div class="grid h-16 w-11 place-items-center rounded-xs bg-surface-3">
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <div class="flex items-baseline gap-2">
        <h2 class="truncate">{entity.title}</h2>
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] text-phosphor-600">
          {entity.kind}
        </span>
      </div>
      <div class="mt-0.5 truncate font-mono text-[0.7rem] text-text-muted">awaiting match</div>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Candidates</span>
      <span class="font-mono font-semibold text-text-accent">{localCandidates.length}</span>
    </div>
    {#if activeProvider}
      <div class="hidden flex-col items-end gap-0.5 md:flex">
        <span class="text-kicker">Provider</span>
        <span class="text-[0.82rem] text-text-primary">{activeProvider.name}</span>
      </div>
    {/if}
  </div>

  <!-- Manual search panel -->
  <section class="surface-panel relative z-20 overflow-visible">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Search class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Query</span>
      <span class="font-mono text-[0.7rem] text-text-muted">refine and re-search</span>
      <div class="flex-1"></div>
      <button
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary disabled:opacity-40"
        disabled={rescanning}
        onclick={handleRescan}
      >
        <RefreshCw class={cn("h-3 w-3", rescanning && "animate-spin")} />
        Rescan
      </button>
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
      {#if providerOptions.length > 0}
        <span class="font-mono text-[0.72rem] text-text-muted">provider:</span>
        <IdentifyProviderSelect
          providers={providerOptions}
          selectedId={activeProviderId}
          onChange={(providerId) => (selectedProviderId = providerId)}
          compact
        />
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

  <!-- Candidates list -->
  <section class="surface-panel relative z-0 overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <span class="text-kicker text-text-accent">Candidates</span>
      <span class="font-mono text-[0.7rem] text-text-muted">{localCandidates.length} found</span>
      {#if checkingCandidateTitle}
        <span class="hidden font-mono text-[0.7rem] text-text-muted md:inline">
          Match found. Identifying related items; this may take a while.
        </span>
      {/if}
    </header>
    <div class="flex flex-col gap-2.5 p-3.5">
      {#each localCandidates as candidate, i (identifyCandidateKey(candidate, i))}
        {@const candidateKey = identifyCandidateKey(candidate, i)}
        {@const hasCover = Boolean(candidate.posterUrl)}
        {@const isChecking = checkingCandidateKey === candidateKey}
        <div
          class={cn(
            "identify-candidate-card relative grid cursor-pointer items-center gap-3 rounded-sm border border-border-subtle bg-surface-1 p-2.5 text-left shadow-well transition-all hover:border-border-accent hover:bg-surface-2 hover:shadow-[0_0_20px_rgba(242,194,106,0.08)] focus-visible:border-border-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-accent-500/60",
            "grid-cols-[3.5rem_minmax(0,1fr)_auto] sm:grid-cols-[4rem_minmax(0,1fr)_auto]",
            store.identifyingId !== null && "cursor-wait opacity-60",
          )}
          role="button"
          tabindex={store.identifyingId !== null ? -1 : 0}
          aria-label={candidateActionLabel(candidate)}
          aria-disabled={store.identifyingId !== null}
          onclick={() => void pickCandidate(candidate, candidateKey)}
          onkeydown={(event) => handleCandidateKeydown(event, candidate, candidateKey)}
        >
          <div class="min-w-0">
            <div
              class="relative w-full overflow-hidden rounded-xs border border-border-subtle bg-surface-3"
              style="aspect-ratio: {candidateAspect};"
            >
              <div class="grid h-full w-full place-items-center">
                <CandidateKindIcon class="h-6 w-6 text-text-disabled" />
              </div>
              {#if hasCover}
                <img
                  src={candidate.posterUrl}
                  alt=""
                  loading="lazy"
                  decoding="async"
                  referrerpolicy="no-referrer"
                  class="absolute inset-0 h-full w-full object-cover"
                  onerror={(event) => ((event.currentTarget as HTMLImageElement).style.display = "none")}
                />
              {/if}
            </div>
          </div>

          <div class="flex min-w-0 flex-col justify-center gap-1.5 py-1">
            <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <span class="min-w-0 break-words font-heading text-[0.88rem] font-semibold text-text-primary">
                {candidateTitle(candidate)}
              </span>
              {#if candidate.year}
                <span class="font-mono text-[0.7rem] text-text-muted">{candidate.year}</span>
              {/if}
              {#if i === 0}
                <span class="inline-flex shrink-0 items-center gap-1 rounded-xs border border-border-accent bg-accent-950/60 px-1.5 py-0.5 font-mono text-[0.6rem] text-text-accent">
                  <Star class="h-2.5 w-2.5" />
                  Best
                </span>
              {/if}
            </div>
            {#if candidate.overview}
              <p class="text-[0.8rem] leading-relaxed text-text-secondary">
                {candidate.overview}
              </p>
            {:else}
              <p class="text-[0.78rem] leading-relaxed text-text-disabled">
                No provider description available.
              </p>
            {/if}
          </div>

          <div class="flex items-center self-stretch pl-1 text-text-accent">
            {#if isChecking}
              <Loader2 class="h-4 w-4 animate-spin" />
            {:else}
              <ChevronRight class="h-4 w-4" />
            {/if}
          </div>
        </div>
      {/each}
    </div>
  </section>
</div>
