<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    Search as SearchIcon,
    X,
    ChevronDown,
    Loader2,
    SlidersHorizontal,
    Star,
  } from "@lucide/svelte";
  import { Button, SearchInput, cn } from "@prismedia/ui-svelte";
  import SearchResultCard from "$lib/components/SearchResultCard.svelte";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { ALL_SEARCH_KINDS, type SearchEntityKind, type SearchResponse, type SearchResultItem } from "$lib/search/models";
  import { firstSearchResult, searchEntities } from "$lib/search/entity-search";
  import { entityTerms } from "$lib/terminology";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { SEARCH_KIND_CONFIG } from "$lib/components/search-kind-config";
  import DateField from "$lib/components/forms/DateField.svelte";

  const PAGE_SIZE = 20;
  const nsfw = useNsfw();
  const currentPath = $derived(`${page.url.pathname}${page.url.search}`);

  let query = $state(page.url.searchParams.get("q") ?? "");
  let activeKinds = $state<Set<SearchEntityKind>>(initialKinds());
  let filtersOpen = $state(false);
  let minRating = $state<number | null>(null);
  let dateFrom = $state("");
  let dateTo = $state("");

  let results = $state<SearchResponse | null>(null);
  let loading = $state(false);
  let expanded = $state<
    Record<string, { items: SearchResultItem[]; total: number; loading: boolean }>
  >({});

  let inputRef = $state<HTMLInputElement | null>(null);
  let activeRequest = 0;

  function initialKinds(): Set<SearchEntityKind> {
    const raw = page.url.searchParams.get("kinds");
    if (!raw) return new Set(ALL_SEARCH_KINDS);
    const parsed = raw
      .split(",")
      .filter((k): k is SearchEntityKind => (ALL_SEARCH_KINDS as readonly string[]).includes(k));
    return parsed.length > 0 ? new Set(parsed) : new Set(ALL_SEARCH_KINDS);
  }

  const kindsArray = $derived(Array.from(activeKinds));
  const hasQuery = $derived(query.trim().length >= 2);
  const hasResults = $derived(
    results != null && results.groups.some((g) => g.items.length > 0),
  );
  const hasFiltersApplied = $derived(
    minRating != null || dateFrom !== "" || dateTo !== "",
  );
  const topResult = $derived(firstSearchResult(results));

  $effect(() => {
    inputRef?.focus();
  });

  // Keep URL in sync (debounced).
  $effect(() => {
    const q = query.trim();
    const kindsList = Array.from(activeKinds);
    const timer = window.setTimeout(() => {
      const params = new URLSearchParams();
      if (q) params.set("q", q);
      if (kindsList.length < ALL_SEARCH_KINDS.length) {
        params.set("kinds", kindsList.join(","));
      }
      const qs = params.toString();
      void goto(`/search${qs ? `?${qs}` : ""}`, {
        replaceState: true,
        keepFocus: true,
        noScroll: true,
      });
    }, 400);
    return () => window.clearTimeout(timer);
  });

  // Debounced fetch.
  $effect(() => {
    const q = query.trim();
    const kindsList = kindsArray;
    const rating = minRating;
    const from = dateFrom;
    const to = dateTo;
    const nsfwMode = nsfw.mode;

    expanded = {};

    if (q.length < 2) {
      results = null;
      loading = false;
      activeRequest += 1;
      return;
    }

    loading = true;
    const requestId = ++activeRequest;
    const timer = window.setTimeout(async () => {
      try {
        void rating;
        void from;
        void to;
        const data = await searchEntities({
          query: q,
          hideNsfw: nsfwMode === "off",
          kinds: kindsList,
          directLimit: 160,
          relatedSourceLimit: 6,
          relatedLimitPerSource: 60,
        });
        if (requestId === activeRequest) {
          results = filterSearchResponse(data);
        }
      } catch {
        if (requestId === activeRequest) {
          results = null;
        }
      } finally {
        if (requestId === activeRequest) {
          loading = false;
        }
      }
    }, 300);

    return () => window.clearTimeout(timer);
  });

  function kindLabel(kind: SearchEntityKind): string {
    if (kind === ENTITY_KIND.movie) return entityTerms.movies;
    if (kind === ENTITY_KIND.video) return entityTerms.videos;
    if (kind === ENTITY_KIND.person) return entityTerms.performers;
    if (kind === ENTITY_KIND.studio) return entityTerms.studios;
    if (kind === ENTITY_KIND.tag) return entityTerms.tags;
    return SEARCH_KIND_CONFIG[kind].label;
  }

  function toggleKind(kind: SearchEntityKind) {
    const next = new Set(activeKinds);
    if (next.has(kind)) {
      if (next.size > 1) next.delete(kind);
    } else {
      next.add(kind);
    }
    activeKinds = next;
  }

  async function loadMore(_kind: SearchEntityKind, _currentCount: number, _total: number) {
    //  entity search currently returns one page per request. The result groups
    // report their returned total, so this path is only here for template parity.
  }

  function filterSearchResponse(response: SearchResponse): SearchResponse {
    return {
      ...response,
      groups: response.groups
        .map((group) => {
          const groupItems = group.items
            .filter((item) => activeKinds.has(item.kind))
            .filter((item) => minRating == null || (item.rating ?? 0) >= minRating);
          return {
            ...group,
            items: groupItems.slice(0, PAGE_SIZE),
            total: groupItems.length,
          };
        })
        .filter((group) => group.items.length > 0),
    };
  }

  function navigateToTopResult() {
    if (!topResult) return;
    void goto(buildHrefWithFrom(topResult.href, currentPath));
  }

  function gridClassFor(kind: SearchEntityKind): string {
    switch (kind) {
      case ENTITY_KIND.movie:
        return "grid gap-2 grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5";
      case ENTITY_KIND.video:
        return "grid gap-2 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4";
      case ENTITY_KIND.videoSeries:
        return "grid gap-2 grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5";
      case ENTITY_KIND.gallery:
        return "grid gap-2 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4";
      case ENTITY_KIND.image:
        return "grid gap-2 grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5";
      case ENTITY_KIND.person:
        return "grid gap-2 grid-cols-2 sm:grid-cols-3 xl:grid-cols-5";
      case ENTITY_KIND.studio:
        return "grid gap-2 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3";
      case ENTITY_KIND.tag:
        return "grid gap-2 grid-cols-2 sm:grid-cols-3 lg:grid-cols-4";
      default:
        return "grid gap-2 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3";
    }
  }

  function groupItemsFor(kind: SearchEntityKind, baseItems: SearchResultItem[]) {
    const extra = expanded[kind];
    const items = extra ? [...baseItems, ...extra.items] : baseItems;
    return items;
  }
</script>

<svelte:head>
  <title>Search · Prismedia</title>
</svelte:head>

<div class="space-y-4">
  <!-- Search header -->
  <div class="space-y-3">
    <SearchInput
      bind:element={inputRef}
      bind:value={query}
      ariaLabel="Search everything"
      placeholder="Search everything..."
      {loading}
      onkeydown={(e) => {
          if (e.key === "Enter" && topResult) {
            e.preventDefault();
            navigateToTopResult();
          }
      }}
    />

    <!-- Entity kind toggles + filters button -->
    <div class="flex flex-wrap items-center gap-2">
      {#each ALL_SEARCH_KINDS as kind (kind)}
        {@const config = SEARCH_KIND_CONFIG[kind]}
        {@const Icon = config.icon}
        {@const active = activeKinds.has(kind)}
        {@const kindAccent = entityAccentForKind(kind).primary}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-pressed={active}
          class={cn(
            "tag-chip h-auto cursor-pointer gap-1.5 transition-colors duration-fast",
            active ? "tag-chip-accent" : "tag-chip-default",
          )}
          onclick={() => toggleKind(kind)}
        >
          <Icon class="h-3 w-3" color={kindAccent} aria-hidden="true" />
          {kindLabel(kind)}
        </Button>
      {/each}

      <div class="h-4 w-px bg-border-subtle"></div>

      <Button
        type="button"
        variant="ghost"
        size="sm"
        aria-expanded={filtersOpen}
        class={cn(
          "tag-chip h-auto cursor-pointer gap-1.5 transition-colors duration-fast",
          filtersOpen ? "tag-chip-info" : "tag-chip-default",
        )}
        onclick={() => (filtersOpen = !filtersOpen)}
      >
        <SlidersHorizontal class="h-3 w-3" />
        Filters
        {#if hasFiltersApplied}
          <span class="flex h-3.5 w-3.5 items-center justify-center bg-accent-800 text-[0.5rem] font-bold text-accent-200">
            !
          </span>
        {/if}
      </Button>
    </div>

    <!-- Filter panel -->
    {#if filtersOpen}
      <div class="surface-well grid grid-cols-1 gap-4 p-3 sm:grid-cols-3">
        <div>
          <div class="text-kicker mb-2">Min Rating</div>
          <div class="flex items-center gap-1">
            {#each [1, 2, 3, 4, 5] as n (n)}
              <Button
                type="button"
                variant="ghost"
                size="icon"
                class={cn(
                  "h-7 w-7 transition-colors duration-fast",
                  minRating && n <= minRating
                    ? "text-text-accent"
                    : "text-text-disabled hover:text-text-muted",
                )}
                onclick={() => (minRating = minRating === n ? null : n)}
                aria-label={`Rating ${n}`}
              >
                <Star
                  class="h-3.5 w-3.5"
                  fill={minRating && n <= minRating ? "currentColor" : "none"}
                />
              </Button>
            {/each}
            {#if minRating}
              <Button
                type="button"
                variant="ghost"
                size="icon"
                class="ml-1 h-7 w-7 text-text-disabled hover:text-text-muted"
                onclick={() => (minRating = null)}
                aria-label="Clear rating"
              >
                <X class="h-3 w-3" />
              </Button>
            {/if}
          </div>
        </div>
        <DateField label="Date From" value={dateFrom} onChange={(value) => (dateFrom = value)} />
        <DateField label="Date To" value={dateTo} onChange={(value) => (dateTo = value)} />
      </div>
    {/if}
  </div>

  <!-- Results states -->
  {#if !hasQuery}
    <div class="flex flex-col items-center justify-center py-20 text-text-disabled">
      <SearchIcon class="mb-3 h-8 w-8 opacity-30" />
      <div class="text-sm">
        Enter a search term to find videos, people, studios, and more
      </div>
    </div>
  {:else if loading && !results}
    <div class="flex items-center justify-center py-20">
      <Loader2 class="h-5 w-5 animate-spin text-text-disabled" />
    </div>
  {:else if results && !hasResults && !loading}
    <div class="flex flex-col items-center justify-center py-20 text-text-disabled">
      <SearchIcon class="mb-3 h-8 w-8 opacity-30" />
      <div class="text-sm">No results for "{query}"</div>
    </div>
  {:else if results && hasResults}
    <div class="space-y-6">
      {#each results.groups.filter((g) => g.items.length > 0 && activeKinds.has(g.kind)) as group (group.kind)}
        {@const items = groupItemsFor(group.kind, group.items)}
        {@const total = expanded[group.kind]?.total ?? group.total}
        {@const loadingMore = expanded[group.kind]?.loading ?? false}
        {@const hasMore = items.length < total}
        {@const Icon = SEARCH_KIND_CONFIG[group.kind].icon}
        {@const groupAccent = entityAccentForKind(group.kind).primary}

        <section>
          <div class="mb-3 flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Icon class="h-4 w-4" color={groupAccent} aria-hidden="true" />
              <span class="text-sm font-medium text-text-primary">
                {kindLabel(group.kind)}
              </span>
              <span class="font-mono text-[0.65rem] text-text-disabled">{total}</span>
            </div>
            <a
              href={SEARCH_KIND_CONFIG[group.kind].href}
              class="text-[0.68rem] text-text-muted transition-colors duration-fast hover:text-text-accent"
            >
              Browse all
            </a>
          </div>

          <div class={gridClassFor(group.kind)}>
            {#each items as item, index (item.id)}
              <SearchResultCard {item} {index} {currentPath} highlighted={item.id === topResult?.id} />
            {/each}
          </div>

          {#if hasMore}
            <div class="mt-3 flex justify-center">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                class={cn(
                  "surface-well gap-1.5 px-4 text-[0.72rem] text-text-muted hover:text-text-primary",
                  loadingMore && "cursor-wait opacity-60",
                )}
                disabled={loadingMore}
                onclick={() => loadMore(group.kind, items.length, total)}
              >
                {#if loadingMore}
                  <Loader2 class="h-3 w-3 animate-spin" />
                {:else}
                  <ChevronDown class="h-3 w-3" />
                {/if}
                Show more ({total - items.length} remaining)
              </Button>
            </div>
          {/if}
        </section>
      {/each}

    </div>
  {/if}
</div>
