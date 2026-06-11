<script lang="ts">
  import { onMount } from "svelte";
  import { Search } from "@lucide/svelte";
  import { Badge, Button, TextInput } from "@prismedia/ui-svelte";
  import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import { fetchRequestServices, searchRequests } from "$lib/api/requests";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import type { RequestSearchResult, RequestServiceInstanceSummary } from "$lib/requests/request-model";
  import { numericValue } from "$lib/requests/request-helpers";

  const kindOptions: { label: string; value: RequestMediaKindCode | "all" }[] = [
    { label: "All", value: "all" },
    { label: "Movies", value: REQUEST_MEDIA_KIND.movie },
    { label: "Series", value: REQUEST_MEDIA_KIND.series },
    { label: "Artists", value: REQUEST_MEDIA_KIND.artist },
    { label: "Albums", value: REQUEST_MEDIA_KIND.album },
    { label: "Plugins", value: REQUEST_MEDIA_KIND.plugin },
  ];

  let query = $state("");
  let selectedKind = $state<RequestMediaKindCode | "all">("all");
  let selectedSource = $state<RequestProviderKindCode | "all">("all");
  let services = $state<RequestServiceInstanceSummary[]>([]);
  let results = $state<RequestSearchResult[]>([]);
  let loading = $state(false);
  let error = $state<string | null>(null);

  onMount(async () => {
    try {
      services = await fetchRequestServices();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request services";
    }
  });

  async function runSearch() {
    if (!query.trim()) return;
    loading = true;
    error = null;
    try {
      const response = await searchRequests({
        query: query.trim(),
        kinds: selectedKind === "all" ? [] : [selectedKind],
        sources: selectedSource === "all" ? [] : [selectedSource],
      });
      results = response.results;
      if (response.providerErrors.length > 0) {
        error = response.providerErrors.map((item) => `${item.displayName}: ${item.message}`).join(" ");
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Search failed";
    } finally {
      loading = false;
    }
  }

  function sourceName(source: RequestProviderKindCode) {
    return services.find((service) => service.kind === source)?.displayName ?? source;
  }

  function detailHref(result: RequestSearchResult) {
    const params = new URLSearchParams({ source: result.source, serviceId: result.serviceId });
    return `/request/${result.kind}/${encodeURIComponent(result.externalId)}?${params.toString()}`;
  }

  function ratingLabel(value: RequestSearchResult["rating"]) {
    const rating = numericValue(value);
    return rating === null ? null : rating.toFixed(1);
  }
</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>

<main class="request-page">
  <section class="request-header">
    <div>
      <p class="eyebrow">Operate</p>
      <h1>Request</h1>
    </div>
    <form class="search-bar" onsubmit={(event) => { event.preventDefault(); void runSearch(); }}>
      <TextInput
        value={query}
        oninput={(event) => (query = event.currentTarget.value)}
        placeholder="Search movies, series, artists, albums"
        aria-label="Search requests"
      />
      <Button type="submit" disabled={loading || !query.trim()}>
        <Search size={16} />
        {loading ? "Searching" : "Search"}
      </Button>
    </form>
  </section>

  <section class="filters" aria-label="Request filters">
    <div class="chip-row">
      {#each kindOptions as option}
        <Button
          variant={selectedKind === option.value ? "primary" : "secondary"}
          size="sm"
          onclick={() => (selectedKind = option.value)}
        >
          {option.label}
        </Button>
      {/each}
    </div>
    <div class="chip-row">
      <Button
        variant={selectedSource === "all" ? "primary" : "secondary"}
        size="sm"
        onclick={() => (selectedSource = "all")}
      >
        All sources
      </Button>
      {#each [REQUEST_PROVIDER_KIND.radarr, REQUEST_PROVIDER_KIND.sonarr, REQUEST_PROVIDER_KIND.lidarr, REQUEST_PROVIDER_KIND.plugin] as source}
        <Button
          variant={selectedSource === source ? "primary" : "secondary"}
          size="sm"
          onclick={() => (selectedSource = source)}
        >
          {source}
        </Button>
      {/each}
    </div>
  </section>

  {#if error}
    <p class="notice">{error}</p>
  {/if}

  <section class="results" aria-label="Request search results">
    {#if results.length === 0}
      <div class="empty">No request results loaded.</div>
    {:else}
      {#each results as result}
        <a class="result-row" href={detailHref(result)}>
          <div class="poster">
            {#if result.posterUrl}
              <img src={result.posterUrl} alt="" loading="lazy" />
            {/if}
          </div>
          <div class="result-copy">
            <div class="result-title">
              <h2>{result.title}</h2>
              {#if result.year}<span>{result.year}</span>{/if}
            </div>
            <p>{result.overview ?? "No overview available."}</p>
            <div class="badges">
              <Badge>{sourceName(result.source)}</Badge>
              <Badge variant="accent">{result.kind}</Badge>
              {#if ratingLabel(result.rating)}
                <Badge>{ratingLabel(result.rating)}</Badge>
              {/if}
              <Badge variant={result.requestable ? "success" : "warning"}>{result.requestable ? "Requestable" : "Unavailable"}</Badge>
            </div>
          </div>
        </a>
      {/each}
    {/if}
  </section>
</main>

<style>
  .request-page {
    display: grid;
    gap: 1rem;
    padding: 1rem;
  }

  .request-header {
    display: grid;
    gap: 1rem;
  }

  .eyebrow {
    color: var(--color-text-muted);
    font-size: 0.75rem;
    margin: 0 0 0.25rem;
    text-transform: uppercase;
  }

  h1, h2, p {
    margin: 0;
  }

  h1 {
    font-family: var(--font-display);
    font-size: 2rem;
  }

  .search-bar {
    display: grid;
    gap: 0.5rem;
  }

  .filters {
    display: grid;
    gap: 0.5rem;
  }

  .chip-row, .badges {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }

  .notice, .empty {
    border: 1px solid var(--color-border-subtle);
    border-radius: 6px;
    color: var(--color-text-secondary);
    padding: 0.75rem;
  }

  .results {
    display: grid;
    gap: 0.75rem;
  }

  .result-row {
    display: grid;
    grid-template-columns: 64px 1fr;
    gap: 0.875rem;
    padding: 0.75rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: 8px;
    background: var(--color-surface-1);
    color: inherit;
    text-decoration: none;
  }

  .poster {
    aspect-ratio: 2 / 3;
    overflow: hidden;
    border-radius: 4px;
    background: var(--color-surface-2);
  }

  .poster img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .result-copy {
    min-width: 0;
    display: grid;
    gap: 0.5rem;
  }

  .result-title {
    display: flex;
    gap: 0.5rem;
    align-items: baseline;
  }

  .result-title h2 {
    font-size: 1rem;
  }

  .result-copy p {
    color: var(--color-text-secondary);
    font-size: 0.875rem;
    line-height: 1.45;
  }

  @media (min-width: 760px) {
    .request-page {
      padding: 1.5rem;
    }

    .request-header {
      grid-template-columns: minmax(180px, 0.7fr) minmax(360px, 1.3fr);
      align-items: end;
    }

    .search-bar {
      grid-template-columns: 1fr auto;
    }

    .result-row {
      grid-template-columns: 92px 1fr;
    }
  }
</style>
