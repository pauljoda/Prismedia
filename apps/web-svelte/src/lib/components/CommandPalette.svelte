<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { browser } from "$app/environment";
  import { Search, X, Clock, ArrowRight, Trash2 } from "@lucide/svelte";
  import { cn, dur, ease, flyDown } from "@prismedia/ui-svelte";
  import { fade } from "svelte/transition";
  import { fetchEntities, type EntityCard } from "$lib/api/entities";
  import SearchResultCard from "$lib/components/SearchResultCard.svelte";
  import { useSearch } from "$lib/stores/search.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { entityTerms } from "$lib/terminology";
  import { recentSearches } from "$lib/stores/recent-searches.svelte";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import type { SearchEntityKind, SearchResponse, SearchResultItem } from "$lib/search/models";

  const search = useSearch();
  const nsfw = useNsfw();
  const recent = recentSearches();

  let query = $state("");
  let results = $state<SearchResponse | null>(null);
  let loading = $state(false);
  let inputRef = $state<HTMLInputElement | null>(null);

  let activeRequest = 0;
  const currentPath = $derived(`${page.url.pathname}${page.url.search}`);
  const open = $derived(search.open);
  const hasQuery = $derived(query.trim().length >= 2);
  const hasResults = $derived(
    results != null && results.groups.some((group) => group.items.length > 0),
  );

  function toSearchKind(kind: string): SearchEntityKind | null {
    if (kind === "person") return "performer";
    if (
      kind === "video" ||
      kind === "video-series" ||
      kind === "studio" ||
      kind === "tag" ||
      kind === "gallery" ||
      kind === "image" ||
      kind === "book" ||
      kind === "audio-library" ||
      kind === "audio-track"
    ) {
      return kind;
    }
    return null;
  }

  function entityToSearchItem(entity: EntityCard): SearchResultItem | null {
    const kind = toSearchKind(entity.kind);
    const href = resolveEntityHref(entity.kind, entity.id);
    if (!kind || !href) return null;

    return {
      href,
      id: entity.id,
      imagePath: entity.coverUrl ?? null,
      kind,
      meta: {},
      rating: typeof entity.rating === "number" ? entity.rating : null,
      score: 1,
      subtitle: labelForEntityKind(entity.kind),
      title: entity.title,
    };
  }

  function toSearchResponse(term: string, startedAt: number, items: EntityCard[]): SearchResponse {
    const groups = new Map<SearchEntityKind, SearchResultItem[]>();
    for (const entity of items) {
      const item = entityToSearchItem(entity);
      if (!item) continue;
      groups.set(item.kind, [...(groups.get(item.kind) ?? []), item]);
    }

    return {
      durationMs: Math.max(0, Math.round(performance.now() - startedAt)),
      groups: [...groups.entries()].map(([kind, groupItems]) => ({
        hasMore: false,
        items: groupItems,
        kind,
        label: labelForEntityKind(kind === "performer" ? "person" : kind),
        total: groupItems.length,
      })),
      query: term,
    };
  }

  function closePalette() {
    search.closePalette();
  }

  function clearQuery() {
    query = "";
    results = null;
    loading = false;
  }

  async function runSearch(term: string) {
    const trimmed = term.trim();
    const requestId = ++activeRequest;

    if (trimmed.length < 2) {
      results = null;
      loading = false;
      return;
    }

    loading = true;
    try {
      const startedAt = performance.now();
      const data = await fetchEntities({
        query: trimmed,
        hideNsfw: nsfw.mode === "off",
      });
      if (requestId === activeRequest) {
        results = toSearchResponse(trimmed, startedAt, data.items);
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

  $effect(() => {
    if (!open) {
      query = "";
      results = null;
      loading = false;
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
      class="absolute inset-0 bg-black/60 backdrop-blur-sm"
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
        "surface-elevated border border-border-subtle shadow-2xl",
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
          onkeydown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              submitSearch();
            }
          }}
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
            {#each results.groups.filter((group) => group.items.length > 0) as group (group.kind)}
              <div class="py-1">
                <div class="flex items-center justify-between px-4 py-1.5">
                  <span class="text-kicker">{group.label}</span>
                  <span class="text-[0.6rem] text-text-disabled">{group.total}</span>
                </div>
                {#each group.items as item, itemIndex (item.id)}
                  <SearchResultCard
                    {item}
                    index={itemIndex}
                    variant="compact"
                    {currentPath}
                    onSelect={navigateTo}
                  />
                {/each}
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
          <span class="font-mono text-[0.6rem] text-text-disabled">
            {loading ? "..." : `${results?.durationMs ?? 0}ms`}
          </span>
        </div>
      {/if}
    </div>
  </div>
{/if}
