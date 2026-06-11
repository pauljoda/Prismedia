<script lang="ts">
  import { onMount } from "svelte";
  import { CloudDownload, Loader2, Search, Send, Settings } from "@lucide/svelte";
  import { Badge, Button, TextInput, cn } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import { fetchRequestServices, searchRequests } from "$lib/api/requests";
  import type { RequestSearchResult, RequestServiceInstanceSummary } from "$lib/requests/request-model";
  import { numericValue, thumbnailAspectForKind } from "$lib/requests/request-helpers";

  const providerLabels: Record<string, string> = {
    [REQUEST_PROVIDER_KIND.radarr]: "Radarr",
    [REQUEST_PROVIDER_KIND.sonarr]: "Sonarr",
    [REQUEST_PROVIDER_KIND.lidarr]: "Lidarr",
    [REQUEST_PROVIDER_KIND.plugin]: "Plugin",
  };

  const kindLabels: Record<string, string> = {
    [REQUEST_MEDIA_KIND.movie]: "Movies",
    [REQUEST_MEDIA_KIND.series]: "Series",
    [REQUEST_MEDIA_KIND.artist]: "Artists",
    [REQUEST_MEDIA_KIND.album]: "Albums",
    [REQUEST_MEDIA_KIND.plugin]: "Plugins",
  };

  const kindsBySource: Record<string, RequestMediaKindCode[]> = {
    [REQUEST_PROVIDER_KIND.radarr]: [REQUEST_MEDIA_KIND.movie],
    [REQUEST_PROVIDER_KIND.sonarr]: [REQUEST_MEDIA_KIND.series],
    [REQUEST_PROVIDER_KIND.lidarr]: [REQUEST_MEDIA_KIND.artist, REQUEST_MEDIA_KIND.album],
    [REQUEST_PROVIDER_KIND.plugin]: [REQUEST_MEDIA_KIND.plugin],
  };

  let query = $state("");
  let selectedKind = $state<RequestMediaKindCode | "all">("all");
  let selectedSource = $state<RequestProviderKindCode | "all">("all");
  let services = $state<RequestServiceInstanceSummary[]>([]);
  let servicesLoaded = $state(false);
  let results = $state<RequestSearchResult[]>([]);
  let hasSearched = $state(false);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let providerWarnings = $state<string[]>([]);

  const availableSources = $derived(
    [...new Set(services.map((service) => service.kind))] as RequestProviderKindCode[],
  );
  const availableKinds = $derived(
    [...new Set(availableSources.flatMap((source) => kindsBySource[source] ?? []))],
  );

  onMount(async () => {
    try {
      services = await fetchRequestServices();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request services";
    } finally {
      servicesLoaded = true;
    }
  });

  async function runSearch() {
    if (!query.trim()) return;
    loading = true;
    error = null;
    providerWarnings = [];
    try {
      const response = await searchRequests({
        query: query.trim(),
        kinds: selectedKind === "all" ? [] : [selectedKind],
        sources: selectedSource === "all" ? [] : [selectedSource],
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
    if (hasSearched) void runSearch();
  }

  function setSource(source: RequestProviderKindCode | "all") {
    selectedSource = source;
    if (hasSearched) void runSearch();
  }

  function sourceName(result: RequestSearchResult) {
    return (
      services.find((service) => service.id === result.serviceId)?.displayName ??
      providerLabels[result.source] ??
      result.source
    );
  }

  function detailHref(result: RequestSearchResult) {
    const params = new URLSearchParams({ source: result.source, serviceId: result.serviceId });
    return `/request/${result.kind}/${encodeURIComponent(result.externalId)}?${params.toString()}`;
  }

  function ratingLabel(value: RequestSearchResult["rating"]) {
    const rating = numericValue(value);
    return rating === null || rating <= 0 ? null : rating.toFixed(1);
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
      onclick={() => void goto("/settings")}
      class="no-lift gap-1.5 px-3 py-1.5 text-xs"
    >
      <Settings class="h-3.5 w-3.5" />
      Manage Services
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
        void runSearch();
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
          {#each [{ value: "all", label: "All" }, ...availableKinds.map((kind) => ({ value: kind, label: kindLabels[kind] ?? kind }))] as option (option.value)}
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
          {#each [{ value: "all", label: "All sources" }, ...availableSources.map((source) => ({ value: source, label: providerLabels[source] ?? source }))] as option (option.value)}
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
    {#if results.length > 0}
      <div class="space-y-2.5" aria-label="Request search results">
        {#each results as result (`${result.source}:${result.kind}:${result.externalId}`)}
          <a
            class="surface-card no-lift flex gap-3.5 p-3 hover:border-border-accent/50 transition-colors"
            href={detailHref(result)}
          >
            <div
              class="w-16 shrink-0 self-start overflow-hidden rounded-xs bg-surface-1 md:w-[88px]"
              style:aspect-ratio={thumbnailAspectForKind(result.kind)}
            >
              {#if result.posterUrl}
                <img
                  src={result.posterUrl}
                  alt=""
                  loading="lazy"
                  class="h-full w-full object-cover"
                />
              {/if}
            </div>
            <div class="min-w-0 flex-1 space-y-1.5">
              <div class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                <span class="text-[0.92rem] font-medium leading-snug text-text-primary">
                  {result.title}
                </span>
                {#if result.year}
                  <span class="font-mono text-[0.72rem] text-text-muted">{result.year}</span>
                {/if}
              </div>
              {#if result.subtitle}
                <p class="truncate text-[0.78rem] font-medium text-text-secondary">
                  {result.subtitle}
                </p>
              {/if}
              {#if result.overview}
                <p class="line-clamp-2 text-[0.78rem] leading-relaxed text-text-secondary">
                  {result.overview}
                </p>
              {/if}
              <div class="flex flex-wrap items-center gap-1.5 pt-0.5">
                <Badge variant="accent">{kindLabels[result.kind] ?? result.kind}</Badge>
                <Badge>{sourceName(result)}</Badge>
                {#if result.certification}
                  <Badge>{result.certification}</Badge>
                {/if}
                {#if result.runtimeMinutes}
                  <Badge>{result.runtimeMinutes} min</Badge>
                {/if}
                {#if result.trackCount}
                  <Badge>{result.trackCount} tracks</Badge>
                {/if}
                {#if ratingLabel(result.rating)}
                  <Badge>★ {ratingLabel(result.rating)}</Badge>
                {/if}
                {#each result.tags.slice(0, 2) as tag}
                  <Badge variant="info">{tag}</Badge>
                {/each}
                {#if !result.requestable}
                  <Badge variant="warning">Unavailable</Badge>
                {/if}
              </div>
            </div>
          </a>
        {/each}
      </div>
    {:else if hasSearched && !loading}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">No results found. Try a different search.</p>
      </div>
    {:else if !hasSearched}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          Search across your connected services to find media to request.
        </p>
      </div>
    {/if}
  {/if}
</div>
