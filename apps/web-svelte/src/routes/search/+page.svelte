<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { Search, SearchX, AlertTriangle, SlidersHorizontal } from "@lucide/svelte";
  import { Badge, Button, ChoiceGroup, SearchInput } from "@prismedia/ui-svelte";
  import SearchResultGroup from "$lib/components/SearchResultGroup.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import StarRatingPicker from "$lib/components/StarRatingPicker.svelte";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { ALL_SEARCH_KINDS, type SearchEntityKind, type SearchResponse } from "$lib/search/models";
  import { firstSearchResult, searchEntities } from "$lib/search/entity-search";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { SEARCH_KIND_CONFIG } from "$lib/components/search-kind-config";

  const nsfw = useNsfw();
  const currentPath = $derived(`${page.url.pathname}${page.url.search}`);
  const filterId = $props.id();
  let query = $state(page.url.searchParams.get("q") ?? "");
  let activeKinds = $state<SearchEntityKind[]>(initialKinds());
  let filtersOpen = $state(false);
  let minRating = $state<number | null>(null);
  let results = $state<SearchResponse | null>(null);
  let loading = $state(false);
  let failed = $state(false);
  let retry = $state(0);
  let inputRef = $state<HTMLInputElement | null>(null);

  function initialKinds(): SearchEntityKind[] {
    const raw = page.url.searchParams.get("kinds")?.split(",") ?? [];
    const parsed = ALL_SEARCH_KINDS.filter(kind => raw.includes(kind));
    return parsed.length ? parsed : [...ALL_SEARCH_KINDS];
  }

  const kindChoices = ALL_SEARCH_KINDS.map(kind => ({ value: kind, label: SEARCH_KIND_CONFIG[kind].label, icon: SEARCH_KIND_CONFIG[kind].icon, iconColor: entityAccentForKind(kind).primary }));
  const hasQuery = $derived(query.trim().length >= 2);
  const filtered = $derived(results ? {
    ...results,
    groups: results.groups.filter(group => activeKinds.includes(group.kind)).map(group => {
      const items = group.items.filter(item => minRating == null || (item.rating ?? 0) >= minRating);
      return { ...group, items, total: items.length };
    }).filter(group => group.items.length > 0),
  } : null);
  const topResult = $derived(firstSearchResult(filtered));
  const hasFilters = $derived(minRating != null || activeKinds.length !== ALL_SEARCH_KINDS.length);

  // Search is the page's primary task. Preserve focus while updating the URL.
  $effect(() => { inputRef?.focus(); });
  $effect(() => {
    const q = query.trim();
    const kinds = [...activeKinds];
    const timer = window.setTimeout(() => {
      const params = new URLSearchParams();
      if (q) params.set("q", q);
      if (kinds.length < ALL_SEARCH_KINDS.length) params.set("kinds", kinds.join(","));
      const qs = params.toString();
      void goto(`/search${qs ? `?${qs}` : ""}`, { replaceState: true, keepFocus: true, noScroll: true });
    }, 400);
    return () => window.clearTimeout(timer);
  });

  // Kind and rating filters operate on fetched matches without repeating the same request.
  $effect(() => {
    const q = query.trim();
    const hideNsfw = nsfw.mode === "off";
    void retry;
    let cancelled = false;
    results = null;
    failed = false;
    loading = q.length >= 2;
    if (q.length < 2) return;
    const timer = window.setTimeout(async () => {
      try {
        const data = await searchEntities({ query: q, hideNsfw, directLimit: 160, relatedSourceLimit: 6, relatedLimitPerSource: 60 });
        if (!cancelled) results = data;
      } catch {
        if (!cancelled) failed = true;
      } finally {
        if (!cancelled) loading = false;
      }
    }, 300);
    return () => { cancelled = true; window.clearTimeout(timer); };
  });

  function clearFilters() {
    minRating = null;
    activeKinds = [...ALL_SEARCH_KINDS];
  }
</script>

<svelte:head><title>Search · Prismedia</title></svelte:head>

<div class="flex min-w-0 flex-col gap-6">
  <div class="flex min-w-0 flex-col gap-3">
    <div class="flex items-start gap-2">
      <SearchInput bind:element={inputRef} bind:value={query} ariaLabel="Search everything" placeholder="Search everything…" {loading}
        onkeydown={event => {
          if (event.key === "Enter" && topResult && !loading) {
            event.preventDefault();
            void goto(buildHrefWithFrom(topResult.href, currentPath));
          }
        }} />
      <Button variant={filtersOpen ? "secondary" : "outline"} class="shrink-0" aria-expanded={filtersOpen} aria-controls={filterId} onclick={() => { filtersOpen = !filtersOpen; }}>
        <SlidersHorizontal data-icon="inline-start" /> Filters
        {#if minRating != null}<Badge variant="secondary">1</Badge>{/if}
      </Button>
    </div>
    <ChoiceGroup type="multiple" options={kindChoices} value={activeKinds} onValueChange={next => { activeKinds = next; }} ariaLabel="Search entity kinds" />
    {#if filtersOpen}
      <section id={filterId} aria-label="Search filters" class="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border bg-card p-4">
        <div class="flex flex-col gap-2">
          <h2 class="text-sm font-medium">Minimum rating</h2>
          <StarRatingPicker value={minRating} onChange={value => { minRating = value; }} ariaLabelPrefix="Minimum" />
        </div>
        {#if minRating != null}<Button variant="ghost" onclick={() => { minRating = null; }}>Clear rating</Button>{/if}
      </section>
    {/if}
  </div>

  {#if !hasQuery}
    <StatePlaceholder icon={Search} title="Search your library" description="Find titles, people, and their related media. Enter at least two characters." />
  {:else if loading}
    <StatePlaceholder icon={Search} title="Searching your library" busy />
  {:else if failed}
    <StatePlaceholder icon={AlertTriangle} title="Search couldn't load" description="Check your connection and try again.">
      <Button variant="outline" onclick={() => { retry += 1; }}>Try again</Button>
    </StatePlaceholder>
  {:else if !filtered?.groups.length}
    <StatePlaceholder icon={SearchX} title={`No matches for “${query.trim()}”`} description={hasFilters ? "Try another title or clear your filters." : "Try another title, person, or tag."}>
      {#if hasFilters}<Button variant="outline" onclick={clearFilters}>Clear filters</Button>{/if}
    </StatePlaceholder>
  {:else}
    {#each filtered.groups as group (`${filtered.query}:${group.kind}:${minRating}`)}
      <SearchResultGroup {group} {currentPath} topResultId={topResult?.id} />
    {/each}
  {/if}
</div>
