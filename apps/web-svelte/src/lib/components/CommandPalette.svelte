<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { tick } from "svelte";
  import { Search, X, Clock, ArrowRight, LoaderCircle } from "@lucide/svelte";
  import { Button, Command, Dialog, Separator } from "@prismedia/ui-svelte";
  import CommandSearchResult from "./CommandSearchResult.svelte";
  import { useSearch } from "$lib/stores/search.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { recentSearches } from "$lib/stores/recent-searches.svelte";
  import { buildHrefWithFrom } from "$lib/back-navigation";
  import type { SearchEntityKind, SearchResponse } from "$lib/search/models";
  import { searchEntities } from "$lib/search/entity-search";

  const search = useSearch();
  const nsfw = useNsfw();
  const recent = recentSearches();
  const PER_KIND_LIMIT = 3;

  let query = $state("");
  let results = $state.raw<SearchResponse | null>(null);
  let loading = $state(false);
  let failed = $state(false);
  let inputRef = $state<HTMLInputElement | null>(null);
  let commandApi = $state<Command.CommandRootApi | null>(null);
  let activeRequest = 0;
  const currentPath = $derived(`${page.url.pathname}${page.url.search}`);
  const hasQuery = $derived(query.trim().length >= 2);
  const displayGroups = $derived((results?.groups ?? [])
    .filter((group) => group.items.length > 0)
    .map((group) => ({ group, shownItems: group.items.slice(0, PER_KIND_LIMIT) })));

  function clearQuery() {
    query = "";
    inputRef?.focus();
  }

  function keepActionEnterLocal(event: KeyboardEvent) {
    // Native action buttons must not also activate the selected command item.
    if (event.key === "Enter") event.stopPropagation();
  }

  async function runSearch(term: string, requestId: number) {
    try {
      const data = await searchEntities({
        query: term, hideNsfw: nsfw.mode === "off", directLimit: 30,
        relatedSourceLimit: 3, relatedLimitPerSource: 8,
      });
      if (requestId !== activeRequest) return;
      results = data;
    } catch {
      if (requestId !== activeRequest) return;
      failed = true;
    } finally {
      if (requestId === activeRequest) {
        loading = false;
        await tick();
        if (requestId === activeRequest) commandApi?.updateSelectedToIndex(0);
      }
    }
  }

  function retrySearch() {
    failed = false;
    loading = true;
    void runSearch(query.trim(), ++activeRequest);
    inputRef?.focus();
  }

  function navigateTo(href: string) {
    recent.add(query.trim());
    search.closePalette();
    void goto(buildHrefWithFrom(href, currentPath));
  }

  function submitSearch(kind?: SearchEntityKind) {
    const term = query.trim();
    if (!term) return;
    recent.add(term);
    search.closePalette();
    void goto(`/search?q=${encodeURIComponent(term)}${kind ? `&kinds=${kind}` : ""}`);
  }

  $effect(() => {
    if (!search.open) query = "";
  });

  // Invalidate immediately when the query changes, including during the debounce.
  // Command handles selection and scrolling; the API owns result ranking.
  $effect(() => {
    const term = query.trim();
    const requestId = ++activeRequest;
    results = null;
    failed = false;
    const shouldSearch = search.open && term.length >= 2;
    loading = shouldSearch;
    if (!shouldSearch) return;
    const timer = window.setTimeout(() => { void runSearch(term, requestId); }, 250);
    return () => { window.clearTimeout(timer); activeRequest++; };
  });
</script>

<Dialog open={search.open} ariaLabel="Search library" onClose={() => search.closePalette()}
  initialFocus={() => inputRef}
  class="top-[10dvh] bottom-auto my-0 w-full max-h-[80dvh] overflow-hidden sm:max-w-2xl">
  <div class="flex max-h-[80dvh] flex-col">
    <header class="flex items-start justify-between gap-3 px-4 pt-4 pb-3">
      <div class="flex flex-col gap-2">
        <h2 class="font-heading text-base font-medium">Search library</h2>
        <p class="text-sm text-muted-foreground">Find media, people, studios, and collections.</p>
      </div>
      <Button variant="ghost" size="icon-sm" aria-label="Close search" onclick={() => search.closePalette()}><X /></Button>
    </header>
    <Command.Root bind:api={commandApi} shouldFilter={false} loop label="Search library" class="min-h-0 rounded-none">
      <div class="flex items-center gap-1 px-3 pb-3">
        <div class="min-w-0 flex-1">
          <Command.Input bind:ref={inputRef} bind:value={query} aria-label="Search library" placeholder="Search your library…" />
        </div>
        {#if query}
          <Button variant="ghost" size="icon-sm" aria-label="Clear search" onclick={clearQuery} onkeydown={keepActionEnterLocal}><X /></Button>
        {/if}
      </div>
      <Separator />
      <Command.List aria-label="Search results" aria-busy={loading} class="max-h-[50dvh] min-h-32 p-1">
        {#if !hasQuery}
          {#if recent.value.length > 0}
            <Command.Group heading="Recent searches">
              {#each recent.value as previousQuery (previousQuery)}
                <div class="flex items-center gap-1">
                  <Command.Item value={`recent:${previousQuery}`} showIndicator={false} class="min-w-0 flex-1"
                    onSelect={() => { query = previousQuery; inputRef?.focus(); }}>
                    <Clock /><span class="truncate">{previousQuery}</span>
                    <ArrowRight class="ml-auto" />
                  </Command.Item>
                  <Button variant="ghost" size="icon-sm" aria-label={`Remove ${previousQuery}`} onclick={() => recent.remove(previousQuery)} onkeydown={keepActionEnterLocal}><X /></Button>
                </div>
              {/each}
            </Command.Group>
          {:else}
            <Command.Empty forceMount>
              <span class="flex flex-col items-center gap-2 text-muted-foreground"><Search class="size-5" />Type at least two characters to search.</span>
            </Command.Empty>
          {/if}
        {:else if loading}
          <Command.Empty forceMount>
            <span role="status" class="flex items-center justify-center gap-2 text-muted-foreground"><LoaderCircle class="size-4 animate-spin motion-reduce:animate-none" />Searching your library…</span>
          </Command.Empty>
        {:else if failed}
          <Command.Empty forceMount>
            <div role="alert" class="flex flex-col items-center gap-3">
              <span>Search couldn't load. Please try again.</span>
              <Button variant="outline" size="sm" onclick={retrySearch} onkeydown={keepActionEnterLocal}>Retry search</Button>
            </div>
          </Command.Empty>
        {:else}
          {#if displayGroups.length === 0}
            <Command.Empty forceMount>No results for “{query.trim()}”</Command.Empty>
          {/if}
          {#each displayGroups as { group, shownItems } (group.kind)}
            <Command.Group heading={`${group.label} · ${group.total}`}>
              {#each shownItems as item (`${item.kind}:${item.id}`)}
                <CommandSearchResult {item} onSelect={navigateTo} />
              {/each}
              {#if group.total > shownItems.length}
                <Command.Item value={`kind:${group.kind}`} showIndicator={false} onSelect={() => submitSearch(group.kind)}>
                  <span>See all {group.total} {group.label.toLowerCase()}</span><ArrowRight class="ml-auto" />
                </Command.Item>
              {/if}
            </Command.Group>
          {/each}
          <Command.Group>
            <Command.Item value="all-results" showIndicator={false} onSelect={() => submitSearch()}>
              <Search /><span>See all results for “{query.trim()}”</span><ArrowRight class="ml-auto" />
            </Command.Item>
          </Command.Group>
        {/if}
      </Command.List>
    </Command.Root>
    <Separator />
    <div class="flex min-h-10 flex-wrap items-center justify-between gap-2 px-4 py-2 text-xs text-muted-foreground">
      <span class="flex items-center gap-3"><span><kbd class="kbd">↑↓</kbd> Navigate</span><span><kbd class="kbd">↵</kbd> Open</span><span><kbd class="kbd">esc</kbd> Close</span></span>
      {#if !hasQuery && recent.value.length > 0}
        <Button variant="ghost" size="sm" onclick={recent.clear}>Clear history</Button>
      {/if}
    </div>
  </div>
</Dialog>
