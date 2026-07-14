<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { browser } from "$app/environment";
  import { Search, X, Clock, ArrowRight, Trash2 } from "@lucide/svelte";
  import { cn, dur, ease, flyDown } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";
  import SearchResultCard from "$lib/components/SearchResultCard.svelte";
  import { useSearch } from "$lib/stores/search.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { entityTerms } from "$lib/terminology";
  import { recentSearches } from "$lib/stores/recent-searches.svelte";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import type { SearchEntityKind, SearchResponse } from "$lib/search/models";
  import { firstSearchResult, searchEntities } from "$lib/search/entity-search";

  const search = useSearch();
  const nsfw = useNsfw();
  const recent = recentSearches();

  let query = $state("");
  let results = $state<SearchResponse | null>(null);
  let loading = $state(false);
  let inputRef = $state<HTMLInputElement | null>(null);
  let activeResultId = $state<string | null>(null);

  let activeRequest = 0;
  const currentPath = $derived(`${page.url.pathname}${page.url.search}`);
  const open = $derived(search.open);
  const hasQuery = $derived(query.trim().length >= 2);
  const hasResults = $derived(
    results != null && results.groups.some((group) => group.items.length > 0),
  );
  // Show only a few results per entity kind so high-count kinds (e.g. People)
  // don't bury everything else. The per-section "see all" row links to the full
  // search page filtered to that kind.
  const PER_KIND_LIMIT = 3;
  const displayGroups = $derived(
    (results?.groups ?? [])
      .filter((group) => group.items.length > 0)
      .map((group) => ({
        group,
        shownItems: group.items.slice(0, PER_KIND_LIMIT),
      })),
  );
  const flatResults = $derived(displayGroups.flatMap((entry) => entry.shownItems));
  const activeResult = $derived(
    flatResults.find((item) => item.id === activeResultId) ?? flatResults[0] ?? null,
  );

  function closePalette() {
    search.closePalette();
  }

  function clearQuery() {
    query = "";
    results = null;
    loading = false;
    activeResultId = null;
  }

  async function runSearch(term: string) {
    const trimmed = term.trim();
    const requestId = ++activeRequest;

    if (trimmed.length < 2) {
      results = null;
      loading = false;
      activeResultId = null;
      return;
    }

    loading = true;
    try {
      const data = await searchEntities({
        query: trimmed,
        hideNsfw: nsfw.mode === "off",
        directLimit: 30,
        relatedSourceLimit: 3,
        relatedLimitPerSource: 8,
      });
      if (requestId === activeRequest) {
        results = data;
        activeResultId = firstSearchResult(data)?.id ?? null;
      }
    } catch {
      if (requestId === activeRequest) {
        results = null;
        activeResultId = null;
      }
    } finally {
      if (requestId === activeRequest) {
        loading = false;
      }
    }
  }

  function navigateTo(href: string) {
    const trimmed = query.trim();
    if (trimmed) recent.add(trimmed);
    closePalette();
    void goto(buildHrefWithFrom(href, currentPath));
  }

  function submitSearch() {
    const trimmed = query.trim();
    if (!trimmed) return;
    recent.add(trimmed);
    closePalette();
    void goto(`/search?q=${encodeURIComponent(trimmed)}`);
  }

  function seeAllForKind(kind: SearchEntityKind) {
    const trimmed = query.trim();
    if (!trimmed) return;
    recent.add(trimmed);
    closePalette();
    void goto(`/search?q=${encodeURIComponent(trimmed)}&kinds=${kind}`);
  }

  function moveActiveResult(delta: number) {
    if (flatResults.length === 0) return;
    const currentIndex = Math.max(0, flatResults.findIndex((item) => item.id === activeResultId));
    const nextIndex = (currentIndex + delta + flatResults.length) % flatResults.length;
    activeResultId = flatResults[nextIndex].id;
  }

  function handleSearchKeydown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      if (activeResult) {
        navigateTo(activeResult.href);
      } else {
        submitSearch();
      }
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      moveActiveResult(1);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      moveActiveResult(-1);
    }
  }

  $effect(() => {
    if (!open) {
      query = "";
      results = null;
      loading = false;
      activeResultId = null;
      activeRequest += 1;
      return;
    }

    if (browser) {
      requestAnimationFrame(() => inputRef?.focus());
    }
  });

  $effect(() => {
    if (!browser || !open) return;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = "";
    };
  });

  $effect(() => {
    if (!open) return;

    const trimmed = query.trim();
    if (trimmed.length < 2) {
      results = null;
      loading = false;
      activeResultId = null;
      activeRequest += 1;
      return;
    }

    loading = true;
    const timer = window.setTimeout(() => {
      void runSearch(trimmed);
    }, 250);

    return () => {
      window.clearTimeout(timer);
    };
  });
</script>

{#if open}
  <div class="fixed inset-0 z-[70] flex items-start justify-center pt-[12vh] sm:pt-[10vh]">
    <button
      type="button"
      class="app-overlay-backdrop absolute inset-0"
      aria-label="Close search"
      onclick={closePalette}
      transition:fade={{ duration: dur.normal, easing: ease.enter }}
    ></button>

    <div
      role="dialog"
      aria-modal="true"
      aria-label="Search"
      class={cn(
        "relative mx-4 flex max-h-[70vh] w-full max-w-2xl flex-col",
        "app-dialog-surface overflow-hidden",
      )}
      transition:flyDown
    >
      <div class="flex items-center gap-3 border-b border-border-subtle px-4 py-3">
        <Search class="h-4 w-4 shrink-0 text-text-muted" />
        <input
          bind:this={inputRef}
          bind:value={query}
          type="text"
          placeholder={`Search ${entityTerms.videos.toLowerCase()}, ${entityTerms.performers.toLowerCase()}, ${entityTerms.studios.toLowerCase()}, ${entityTerms.tags.toLowerCase()}...`}
          class="flex-1 bg-transparent text-sm text-text-primary placeholder:text-text-disabled focus:outline-none"
          onkeydown={handleSearchKeydown}
        />
        {#if query}
          <button
            type="button"
            class="text-text-disabled transition-colors duration-fast hover:text-text-muted"
            aria-label="Clear search"
            onclick={clearQuery}
          >
            <X class="h-3.5 w-3.5" />
          </button>
        {/if}
        <kbd class="kbd hidden shrink-0 text-text-disabled sm:inline-flex">ESC</kbd>
      </div>

      <div class="flex-1 overflow-y-auto">
        {#if !hasQuery}
          {#if recent.value.length === 0}
            <div class="px-4 py-8 text-center">
              <Search class="mx-auto mb-2 h-5 w-5 text-text-disabled opacity-50" />
              <div class="text-sm text-text-disabled">Start typing to search...</div>
            </div>
          {:else}
            <div class="py-1">
              <div class="flex items-center justify-between px-4 py-1.5">
                <span class="text-kicker">Recent Searches</span>
                <button
                  type="button"
                  class="flex items-center gap-1 text-[0.6rem] text-text-disabled transition-colors duration-fast hover:text-text-muted"
                  onclick={recent.clear}
                >
                  <Trash2 class="h-2.5 w-2.5" />
                  Clear
                </button>
              </div>
              {#each recent.value as previousQuery (previousQuery)}
                <div class="group flex items-center gap-2 px-4 py-1.5 transition-colors duration-fast hover:bg-surface-2">
                  <Clock class="h-3.5 w-3.5 shrink-0 text-text-disabled" />
                  <button
                    type="button"
                    class="flex-1 truncate text-left text-sm text-text-muted group-hover:text-text-primary"
                    onclick={() => {
                      query = previousQuery;
                    }}
                  >
                    {previousQuery}
                  </button>
                  <button
                    type="button"
                    class="opacity-0 transition-all duration-fast group-hover:opacity-100 text-text-disabled hover:text-text-muted"
                    onclick={() => recent.remove(previousQuery)}
                    aria-label={`Remove ${previousQuery}`}
                  >
                    <X class="h-3 w-3" />
                  </button>
                </div>
              {/each}
            </div>
          {/if}
        {:else if loading && !results}
          <div class="px-4 py-8 text-center">
            <div class="text-sm text-text-disabled">Searching...</div>
          </div>
        {:else if results && !hasResults && !loading}
          <div class="px-4 py-8 text-center">
            <div class="text-sm text-text-disabled">No results for "{query}"</div>
          </div>
        {:else if results && hasResults}
          <div class="py-1">
            {#each displayGroups as { group, shownItems } (group.kind)}
              <div class="py-1">
                <div class="flex items-center justify-between px-4 py-1.5">
                  <span class="text-kicker">{group.label}</span>
                  <span class="text-[0.6rem] text-text-disabled">{group.total}</span>
                </div>
                {#each shownItems as item, itemIndex (item.id)}
                  <SearchResultCard
                    {item}
                    index={itemIndex}
                    variant="compact"
                    {currentPath}
                    highlighted={item.id === activeResult?.id}
                    onSelect={navigateTo}
                  />
                {/each}
                {#if group.total > shownItems.length}
                  <button
                    type="button"
                    class="flex w-full items-center justify-between px-4 py-1.5 text-left text-[0.72rem] text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-accent"
                    onclick={() => seeAllForKind(group.kind)}
                  >
                    <span>See all {group.total} {group.label.toLowerCase()}</span>
                    <ArrowRight class="h-3 w-3 shrink-0" />
                  </button>
                {/if}
              </div>
            {/each}
          </div>
        {/if}
      </div>

      {#if hasQuery}
        <div class="flex items-center justify-between border-t border-border-subtle px-4 py-2">
          <button
            type="button"
            class="flex items-center gap-1.5 text-[0.72rem] text-text-muted transition-colors duration-fast hover:text-text-accent"
            onclick={submitSearch}
          >
            <span>See all results</span>
            <ArrowRight class="h-3 w-3" />
          </button>
        </div>
      {/if}
    </div>
  </div>
{/if}
