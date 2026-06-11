<script lang="ts">
  import { onMount } from "svelte";
  import { CloudDownload, History, Loader2, Search, Send, Settings } from "@lucide/svelte";
  import { Button, Select, TextInput, cn } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import { fetchRequestServices, searchRequests } from "$lib/api/requests";
  import RequestPosterCard from "$lib/components/requests/RequestPosterCard.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type { RequestSearchResult, RequestServiceInstanceSummary } from "$lib/requests/request-model";
  import {
    REQUEST_KIND_LABELS_PLURAL,
    REQUEST_PROVIDER_LABELS,
    numericValue,
    thumbnailAspectForKind,
    trackedLabel,
  } from "$lib/requests/request-helpers";

  const kindsBySource: Record<string, RequestMediaKindCode[]> = {
    [REQUEST_PROVIDER_KIND.radarr]: [REQUEST_MEDIA_KIND.movie],
    [REQUEST_PROVIDER_KIND.sonarr]: [REQUEST_MEDIA_KIND.series],
    [REQUEST_PROVIDER_KIND.lidarr]: [REQUEST_MEDIA_KIND.artist, REQUEST_MEDIA_KIND.album],
    [REQUEST_PROVIDER_KIND.plugin]: [REQUEST_MEDIA_KIND.plugin],
  };

  const sortOptions = [
    { value: "relevance", label: "Relevance" },
    { value: "year", label: "Year (newest)" },
    { value: "rating", label: "Rating" },
    { value: "title", label: "Title A–Z" },
  ];

  const availabilityOptions: { value: "all" | "requestable" | "tracked"; label: string }[] = [
    { value: "all", label: "All" },
    { value: "requestable", label: "Not in service" },
    { value: "tracked", label: "Already tracked" },
  ];

  /** Order sections appear in mixed-kind results. */
  const sectionOrder: RequestMediaKindCode[] = [
    REQUEST_MEDIA_KIND.movie,
    REQUEST_MEDIA_KIND.series,
    REQUEST_MEDIA_KIND.artist,
    REQUEST_MEDIA_KIND.album,
    REQUEST_MEDIA_KIND.plugin,
  ];

  const nsfw = useNsfw();

  let query = $state("");
  let selectedKind = $state<RequestMediaKindCode | "all">("all");
  let selectedSource = $state<RequestProviderKindCode | "all">("all");
  let sortBy = $state("relevance");
  let availability = $state<"all" | "requestable" | "tracked">("all");
  let services = $state<RequestServiceInstanceSummary[]>([]);
  let servicesLoaded = $state(false);
  let results = $state<RequestSearchResult[]>([]);
  let hasSearched = $state(false);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let providerWarnings = $state<string[]>([]);
  let lastSearchKey = "";

  const availableSources = $derived(
    [...new Set(services.map((service) => service.kind))] as RequestProviderKindCode[],
  );
  const availableKinds = $derived(
    [...new Set(availableSources.flatMap((source) => kindsBySource[source] ?? []))],
  );

  const filteredResults = $derived(
    results.filter((result) =>
      availability === "all" ? true : availability === "tracked" ? result.tracked : !result.tracked,
    ),
  );

  const sections = $derived(
    sectionOrder
      .map((kind) => ({ kind, items: sortResults(filteredResults.filter((result) => result.kind === kind)) }))
      .filter((section) => section.items.length > 0),
  );

  const trackedCount = $derived(results.filter((result) => result.tracked).length);

  onMount(async () => {
    try {
      services = await fetchRequestServices();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request services";
    } finally {
      servicesLoaded = true;
    }
  });

  // The URL is the source of truth for search state, so back/forward and
  // shared links land on the same results. Effect re-runs on URL changes.
  $effect(() => {
    const params = page.url.searchParams;
    const urlQuery = params.get("q") ?? "";
    const urlKind = (params.get("kind") as RequestMediaKindCode | null) ?? "all";
    const urlSource = (params.get("source") as RequestProviderKindCode | null) ?? "all";
    const searchKey = `${urlQuery}::${urlKind}::${urlSource}::${nsfw.mode}`;
    if (searchKey === lastSearchKey) return;
    lastSearchKey = searchKey;

    query = urlQuery;
    selectedKind = urlKind;
    selectedSource = urlSource;
    if (urlQuery.trim()) {
      void runSearch(urlQuery, urlKind, urlSource);
    } else {
      results = [];
      hasSearched = false;
    }
  });

  function searchHref(value: string, kind: RequestMediaKindCode | "all", source: RequestProviderKindCode | "all") {
    const params = new URLSearchParams();
    if (value.trim()) params.set("q", value.trim());
    if (kind !== "all") params.set("kind", kind);
    if (source !== "all") params.set("source", source);
    const queryString = params.toString();
    return queryString ? `/request?${queryString}` : "/request";
  }

  function commitSearchState(kind = selectedKind, source = selectedSource) {
    void goto(searchHref(query, kind, source), {
      replaceState: hasSearched,
      keepFocus: true,
      noScroll: true,
    });
  }

  async function runSearch(
    value: string,
    kind: RequestMediaKindCode | "all",
    source: RequestProviderKindCode | "all",
  ) {
    loading = true;
    error = null;
    providerWarnings = [];
    try {
      const response = await searchRequests({
        query: value.trim(),
        kinds: kind === "all" ? [] : [kind],
        sources: source === "all" ? [] : [source],
        hideNsfw: nsfw.mode !== "show",
      });
      results = response.results;
      hasSearched = true;
      providerWarnings = response.providerErrors.map(
        (item) => `${item.displayName}: ${item.message}`,
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Search failed";
    } finally {
      loading = false;
    }
  }

  function setKind(kind: RequestMediaKindCode | "all") {
    selectedKind = kind;
    if (hasSearched || query.trim()) commitSearchState(kind, selectedSource);
  }

  function setSource(source: RequestProviderKindCode | "all") {
    selectedSource = source;
    if (hasSearched || query.trim()) commitSearchState(selectedKind, source);
  }

  function sortResults(items: RequestSearchResult[]) {
    if (sortBy === "relevance") return items;
    return [...items].sort((a, b) => {
      if (sortBy === "year") return (numericValue(b.year) ?? 0) - (numericValue(a.year) ?? 0);
      if (sortBy === "rating") return (numericValue(b.rating) ?? 0) - (numericValue(a.rating) ?? 0);
      return a.title.localeCompare(b.title);
    });
  }

  function sourceName(result: RequestSearchResult) {
    return (
      services.find((service) => service.id === result.serviceId)?.displayName ??
      REQUEST_PROVIDER_LABELS[result.source] ??
      result.source
    );
  }

  function detailHref(result: RequestSearchResult) {
    const params = new URLSearchParams({ source: result.source, serviceId: result.serviceId });
    const backQuery = searchHref(query, selectedKind, selectedSource).split("?")[1];
    if (backQuery) params.set("back", backQuery);
    return `/request/${result.kind}/${encodeURIComponent(result.externalId)}?${params.toString()}`;
  }

  function cardChips(result: RequestSearchResult) {
    const year = numericValue(result.year);
    const runtime = numericValue(result.runtimeMinutes);
    const trackCount = numericValue(result.trackCount);
    return [
      year ? String(year) : null,
      result.certification,
      runtime ? `${runtime} min` : null,
      trackCount ? `${trackCount} tracks` : null,
    ];
  }

  function isMusic(kind: RequestMediaKindCode) {
    return kind === REQUEST_MEDIA_KIND.artist || kind === REQUEST_MEDIA_KIND.album;
  }
</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>

<div class="space-y-5">
  <!-- ── Header ── -->
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5">
        <Send class="h-5 w-5 text-text-accent" />
        Request
      </h1>
      <p class="mt-1 text-[0.78rem] text-text-muted">
        Search connected services and request new movies, series, and music
      </p>
    </div>
    <Button
      type="button"
      variant="secondary"
      size="sm"
      onclick={() => void goto("/request/history")}
      class="no-lift gap-1.5 px-3 py-1.5 text-xs"
    >
      <History class="h-3.5 w-3.5" />
      History
    </Button>
  </div>

  {#if servicesLoaded && services.length === 0}
    <div class="empty-rack-slot flex flex-col items-center gap-2 p-10 text-center">
      <CloudDownload class="h-8 w-8 text-text-disabled" />
      <p class="text-sm font-medium text-text-secondary">No request services configured</p>
      <p class="max-w-md text-[0.78rem] text-text-muted">
        Connect a Radarr, Sonarr, or Lidarr instance in Settings to start requesting media.
      </p>
      <Button
        type="button"
        variant="primary"
        size="sm"
        onclick={() => void goto("/settings")}
        class="mt-2 gap-1.5 px-3 py-1.5 text-xs"
      >
        <Settings class="h-3.5 w-3.5" />
        Open Settings
      </Button>
    </div>
  {:else}
    <!-- ── Search ── -->
    <form
      class="flex flex-wrap items-center gap-2"
      onsubmit={(event) => {
        event.preventDefault();
        if (query.trim()) commitSearchState();
      }}
    >
      <div class="min-w-0 flex-1 basis-64">
        <TextInput
          value={query}
          oninput={(event) => (query = event.currentTarget.value)}
          placeholder="Search movies, series, artists, albums…"
          aria-label="Search requests"
          autocomplete="off"
        />
      </div>
      <Button type="submit" variant="primary" disabled={loading || !query.trim()} class="gap-2">
        {#if loading}
          <Loader2 class="h-4 w-4 animate-spin" />
        {:else}
          <Search class="h-4 w-4" />
        {/if}
        Search
      </Button>
    </form>

    <!-- ── Filters ── -->
    <div class="space-y-2">
      {#if availableKinds.length > 1}
        <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by kind">
          {#each [{ value: "all", label: "All" }, ...availableKinds.map((kind) => ({ value: kind, label: REQUEST_KIND_LABELS_PLURAL[kind] ?? kind }))] as option (option.value)}
            <button
              type="button"
              onclick={() => setKind(option.value as RequestMediaKindCode | "all")}
              class={cn(
                "rounded-xs border px-2.5 py-1 text-[0.72rem] font-medium transition-all duration-fast",
                selectedKind === option.value
                  ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                  : "bg-surface-1 border-border-subtle text-text-muted hover:border-border-default hover:text-text-primary",
              )}
            >
              {option.label}
            </button>
          {/each}
        </div>
      {/if}
      {#if availableSources.length > 1}
        <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by source">
          {#each [{ value: "all", label: "All sources" }, ...availableSources.map((source) => ({ value: source, label: REQUEST_PROVIDER_LABELS[source] ?? source }))] as option (option.value)}
            <button
              type="button"
              onclick={() => setSource(option.value as RequestProviderKindCode | "all")}
              class={cn(
                "rounded-xs border px-2.5 py-1 text-[0.72rem] font-medium transition-all duration-fast",
                selectedSource === option.value
                  ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                  : "bg-surface-1 border-border-subtle text-text-muted hover:border-border-default hover:text-text-primary",
              )}
            >
              {option.label}
            </button>
          {/each}
        </div>
      {/if}

      {#if hasSearched && results.length > 0}
        <div class="flex flex-wrap items-center justify-between gap-2 pt-1">
          <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by availability">
            {#each availabilityOptions as option (option.value)}
              <button
                type="button"
                onclick={() => (availability = option.value)}
                class={cn(
                  "rounded-xs border px-2.5 py-1 text-[0.72rem] font-medium transition-all duration-fast",
                  availability === option.value
                    ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                    : "bg-surface-1 border-border-subtle text-text-muted hover:border-border-default hover:text-text-primary",
                )}
              >
                {option.label}
                {#if option.value === "tracked" && trackedCount > 0}
                  <span class="ml-1 font-mono text-[0.62rem] opacity-70">{trackedCount}</span>
                {/if}
              </button>
            {/each}
          </div>
          <label class="flex items-center gap-2">
            <span class="text-label text-text-muted">Sort</span>
            <Select
              size="sm"
              value={sortBy}
              options={sortOptions}
              onchange={(value) => (sortBy = value)}
            />
          </label>
        </div>
      {/if}
    </div>

    {#if error}
      <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
        {error}
      </div>
    {/if}
    {#each providerWarnings as warning}
      <div class="surface-panel border-l-2 border-warning px-4 py-2.5 text-sm text-warning-text">
        {warning}
      </div>
    {/each}

    <!-- ── Results ── -->
    {#if sections.length > 0}
      <div class="space-y-6" aria-label="Request search results">
        {#each sections as section (section.kind)}
          <section class="space-y-2.5">
            {#if sections.length > 1 || selectedKind === "all"}
              <h2 class="text-kicker text-text-primary">
                {REQUEST_KIND_LABELS_PLURAL[section.kind] ?? section.kind}
                <span class="ml-1.5 font-mono text-[0.68rem] font-normal text-text-muted">
                  {section.items.length}
                </span>
              </h2>
            {/if}
            <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
              {#each section.items as result (`${result.source}:${result.kind}:${result.externalId}`)}
                <RequestPosterCard
                  href={detailHref(result)}
                  title={result.title}
                  subtitle={result.subtitle ?? sourceName(result)}
                  imageUrl={result.posterUrl}
                  aspect={thumbnailAspectForKind(result.kind)}
                  chips={cardChips(result)}
                  trackedLabel={result.tracked ? trackedLabel(result.source) : null}
                  rating={numericValue(result.rating)}
                  placeholder={result.kind === REQUEST_MEDIA_KIND.artist
                    ? "person"
                    : isMusic(result.kind)
                      ? "music"
                      : "video"}
                />
              {/each}
            </div>
          </section>
        {/each}
      </div>
    {:else if hasSearched && !loading}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          {results.length > 0
            ? "Nothing matches the current filters."
            : "No results found. Try a different search."}
        </p>
      </div>
    {:else if !hasSearched && !loading}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          Search across your connected services to find media to request.
        </p>
      </div>
    {/if}
  {/if}
</div>
