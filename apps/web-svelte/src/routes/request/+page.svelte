<script lang="ts">
  import { onMount } from "svelte";
  import {
    AlertTriangle,
    Compass,
    HardDriveDownload,
    History,
    Loader2,
    PackageSearch,
    Search,
    Send,
    Settings,
  } from "@lucide/svelte";
  import { Button, Select, TextInput, cn } from "@prismedia/ui-svelte";
  import type { SelectOption } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { ENTITY_KIND, type RequestMediaKindCode } from "$lib/api/generated/codes";
  import type { AcquisitionHistoryView } from "$lib/api/generated/model";
  import { fetchAcquisitionHistory } from "$lib/api/acquisitions";
  import { searchRequests } from "$lib/api/requests";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import AcquisitionHistoryList from "$lib/components/acquisitions/AcquisitionHistoryList.svelte";
  import DownloadsPanel from "$lib/components/acquisitions/DownloadsPanel.svelte";
  import WantedList from "$lib/components/acquisitions/WantedList.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { usePageSnapshots } from "$lib/stores/page-snapshots.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import type { RequestSearchResult } from "$lib/requests/request-model";
  import { requestSearchResultToThumbnailCard } from "$lib/requests/review-cards";
  import {
    DISCOVERABLE_REQUEST_KINDS,
    REQUEST_KIND_LABELS_PLURAL,
    numericValue,
  } from "$lib/requests/request-helpers";

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

  /** Order sections appear in mixed-kind results (the kind catalog's Discover order). */
  const sectionOrder: RequestMediaKindCode[] = DISCOVERABLE_REQUEST_KINDS.map((info) => info.kind);

  const nsfw = useNsfw();

  const tabs = [
    { id: "discover", label: "Discover", icon: Compass },
    { id: "downloads", label: "Downloads", icon: HardDriveDownload },
    { id: "missing", label: "Missing", icon: PackageSearch },
    { id: "cutoff", label: "Cutoff unmet", icon: AlertTriangle },
    { id: "history", label: "History", icon: History },
  ] as const;
  type RequestTab = (typeof tabs)[number]["id"];
  let activeTab = $state<RequestTab>("discover");

  // Kind filter options for the Wanted lists — the acquisition-capable library kinds plus "All".
  const wantedKindOptions: SelectOption[] = [
    { value: "all", label: "All kinds" },
    ...[ENTITY_KIND.book, ENTITY_KIND.movie, ENTITY_KIND.videoSeries, ENTITY_KIND.audioLibrary].map(
      (kind) => ({ value: kind, label: labelForEntityKind(kind) }),
    ),
  ];

  // Durable acquisition activity log (global, newest-first), loaded lazily when the History tab opens.
  let history = $state<AcquisitionHistoryView[]>([]);
  let historyLoading = $state(false);
  let historyError = $state<string | null>(null);
  let historyLoaded = false;

  async function loadHistory() {
    if (historyLoaded) return;
    historyLoading = true;
    historyError = null;
    try {
      history = await fetchAcquisitionHistory({ limit: 200 });
      historyLoaded = true;
    } catch (err) {
      historyError = err instanceof Error ? err.message : "Failed to load history";
    } finally {
      historyLoading = false;
    }
  }

  $effect(() => {
    if (activeTab === "history") void loadHistory();
  });

  let query = $state("");
  let selectedKind = $state<RequestMediaKindCode | "all">("all");
  let sortBy = $state("relevance");
  let availability = $state<"all" | "requestable" | "tracked">("all");
  let results = $state<RequestSearchResult[]>([]);
  let hasSearched = $state(false);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let providerWarnings = $state<string[]>([]);
  let lastSearchKey = "";

  // Kinds Prismedia searches via the plugin acquisition pipeline (from the shared kind catalog).
  const availableKinds: RequestMediaKindCode[] = DISCOVERABLE_REQUEST_KINDS.map((info) => info.kind);

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

  // Preserve the active tab across navigation so returning from a detail page lands back on Requests
  // rather than resetting to Discover. (Discover's search state already restores from the URL.)
  const pageSnapshots = usePageSnapshots();
  onMount(() =>
    pageSnapshots.registerSurface<{ tab: RequestTab }>("request-view", {
      capture: () => ({ tab: activeTab }),
      restore: (snapshot) => {
        // Ignore a snapshot naming a tab that no longer exists (e.g. the retired "requests" tab).
        if (tabs.some((tab) => tab.id === snapshot.tab)) {
          activeTab = snapshot.tab;
        }
      },
    }),
  );

  // The URL is the source of truth for search state, so back/forward and
  // shared links land on the same results. Effect re-runs on URL changes.
  $effect(() => {
    const params = page.url.searchParams;
    const urlQuery = params.get("q") ?? "";
    const urlKind = (params.get("kind") as RequestMediaKindCode | null) ?? "all";
    const searchKey = `${urlQuery}::${urlKind}::${nsfw.mode}`;
    if (searchKey === lastSearchKey) return;
    lastSearchKey = searchKey;

    query = urlQuery;
    selectedKind = urlKind;
    if (urlQuery.trim()) {
      void runSearch(urlQuery, urlKind);
    } else {
      results = [];
      hasSearched = false;
    }
  });

  function searchHref(value: string, kind: RequestMediaKindCode | "all") {
    const params = new URLSearchParams();
    if (value.trim()) params.set("q", value.trim());
    if (kind !== "all") params.set("kind", kind);
    const queryString = params.toString();
    return queryString ? `/request?${queryString}` : "/request";
  }

  function commitSearchState(kind = selectedKind) {
    void goto(searchHref(query, kind), {
      replaceState: hasSearched,
      keepFocus: true,
      noScroll: true,
    });
  }

  async function runSearch(value: string, kind: RequestMediaKindCode | "all") {
    loading = true;
    error = null;
    providerWarnings = [];
    // Clear immediately so toggling NSFW mode (or any new search) never shows
    // stale results from the previous mode while the request is in flight.
    results = [];
    try {
      const response = await searchRequests({
        query: value.trim(),
        kinds: kind === "all" ? [] : [kind],
        sources: [],
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
    if (hasSearched || query.trim()) commitSearchState(kind);
  }

  function sortResults(items: RequestSearchResult[]) {
    if (sortBy === "relevance") return items;
    return [...items].sort((a, b) => {
      if (sortBy === "year") return (numericValue(b.year) ?? 0) - (numericValue(a.year) ?? 0);
      if (sortBy === "rating") return (numericValue(b.rating) ?? 0) - (numericValue(a.rating) ?? 0);
      return a.title.localeCompare(b.title);
    });
  }

  function detailHref(result: RequestSearchResult) {
    const params = new URLSearchParams({ source: result.source });
    const backQuery = searchHref(query, selectedKind).split("?")[1];
    if (backQuery) params.set("back", backQuery);
    return `/request/${result.kind}/${encodeURIComponent(result.externalId)}?${params.toString()}`;
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
        Search for books and authors to add to your library
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
      Settings
    </Button>
  </div>

  <!-- ── Tabs ── -->
  <div class="primary-tabs" role="tablist" aria-label="Request views">
    {#each tabs as tab (tab.id)}
      {@const TabIcon = tab.icon}
      <button
        type="button"
        role="tab"
        aria-selected={activeTab === tab.id}
        onclick={() => (activeTab = tab.id)}
        class={cn("primary-tab", activeTab === tab.id && "is-active")}
      >
        <TabIcon class="h-4 w-4" />
        {tab.label}
      </button>
    {/each}
  </div>

  {#if activeTab === "downloads"}
    <!-- ── Global downloads: every active acquisition in one shared card list, live telemetry included ── -->
    <DownloadsPanel />
  {:else if activeTab === "missing"}
    <WantedList variant="missing" kindOptions={wantedKindOptions} />
  {:else if activeTab === "cutoff"}
    <WantedList variant="cutoffUnmet" kindOptions={wantedKindOptions} />
  {:else if activeTab === "history"}
    <!-- ── Activity log (global, newest-first) ── -->
    {#if historyError}
      <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
        {historyError}
      </div>
    {/if}
    {#if history.length > 0}
      <AcquisitionHistoryList entries={history} showKind />
    {:else if historyLoading}
      <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
        <Loader2 class="h-4 w-4 animate-spin" />
        <span class="text-sm">Loading history…</span>
      </div>
    {:else}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          No acquisition activity yet. Grabs, imports, failures, and removals will appear here.
        </p>
      </div>
    {/if}
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
          placeholder="Search books and authors…"
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
      {#if availableKinds.length >= 1}
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
                <!-- Every result opens its detail page first, where the user reviews it and explicitly
                     requests it. Nothing is queued on a thumbnail click. -->
                <EntityThumbnail card={requestSearchResultToThumbnailCard(result, detailHref(result))} />
              {/each}
            </div>
          </section>
        {/each}
      </div>
    {:else if loading}
      <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
        <Loader2 class="h-4 w-4 animate-spin" />
        <span class="text-sm">Searching…</span>
      </div>
    {:else if hasSearched}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          {results.length > 0
            ? "Nothing matches the current filters."
            : "No results found. Try a different search."}
        </p>
      </div>
    {:else if !hasSearched}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          Search for books and authors to add to your library.
        </p>
      </div>
    {/if}
  {/if}
</div>

<style>
  /* Primary mode tabs (Discover / Requests): the app's underline-glow tab treatment, scaled up for
     top-level navigation. */
  .primary-tabs {
    position: relative;
    display: flex;
    gap: 0.25rem;
  }

  .primary-tabs::after {
    content: "";
    position: absolute;
    inset: auto 0 0 0;
    height: 1px;
    background: linear-gradient(
      to right,
      transparent,
      var(--color-border-subtle) 8%,
      var(--color-border-subtle) 92%,
      transparent
    );
    pointer-events: none;
  }

  .primary-tab {
    position: relative;
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.92rem;
    font-weight: 600;
    line-height: 1;
    padding: 0.65rem 0.9rem;
    transition: color var(--duration-fast, 120ms) var(--ease-default);
  }

  .primary-tab::before {
    content: "";
    position: absolute;
    inset: auto 0.35rem 0 0.35rem;
    height: 2px;
    background: transparent;
    transition:
      background var(--duration-normal, 200ms) var(--ease-mechanical),
      box-shadow var(--duration-normal, 200ms) var(--ease-mechanical);
    z-index: 1;
  }

  .primary-tab:hover {
    color: var(--color-text-secondary);
  }

  .primary-tab:hover::before {
    background: rgb(255 255 255 / 0.16);
  }

  .primary-tab:focus-visible {
    outline: 1px solid rgb(242 194 106 / 0.72);
    outline-offset: 2px;
    border-radius: var(--radius-xs, 4px);
  }

  .primary-tab.is-active {
    color: var(--color-text-accent-bright, #f2c26a);
  }

  .primary-tab.is-active::before {
    background: linear-gradient(
      to right,
      var(--color-accent-overlay-faint),
      var(--color-accent-overlay-strong) 50%,
      var(--color-accent-overlay-faint)
    );
    box-shadow:
      0 0 8px var(--color-accent-overlay-light),
      0 0 16px rgba(196, 154, 90, 0.1);
  }
</style>
