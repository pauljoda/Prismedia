<script lang="ts">
  import { onMount } from "svelte";
  import { AlertTriangle, Loader2, PackageSearch, PlugZap } from "@lucide/svelte";
  import { Alert, Button, ChoiceGroup } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { ENTITY_KIND, type RequestMediaKindCode } from "$lib/api/generated/codes";
  import { fetchSettingsValues } from "$lib/api/settings";
  import type { ExternalIdentity, RequestSearchResult } from "$lib/api/generated/model";
  import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import { searchRequestsByPlugin } from "$lib/api/requests";
  import PluginSearchSurface from "$lib/components/plugins/PluginSearchSurface.svelte";
  import {
    nextPluginSearchLimit,
    PLUGIN_SEARCH_MAX_LIMIT,
    PLUGIN_SEARCH_PAGE_SIZE,
  } from "$lib/components/plugins/plugin-search-paging";
  import {
    hasRequiredPluginSearchFields,
    seedPluginSearchFields,
    submittedPluginSearchFields,
  } from "$lib/components/plugins/plugin-search-fields";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { discoverSearchProviders, discoverSearchSupport } from "$lib/requests/discovery-plugins";
  import { DISCOVERABLE_REQUEST_KINDS, numericValue } from "$lib/requests/request-helpers";
  import { requestKindAccent, requestKindIcon } from "$lib/requests/request-kind-presentation";
  import { settingKeys, valueAsStringMap } from "$lib/settings/app-settings";

  interface Props {
    /** Search-page query state to restore when the review page's Back action is used. */
    back?: string | null;
  }

  type NavigableRequestResult = RequestSearchResult & {
    pluginId: string;
    externalIdentity: ExternalIdentity;
  };

  interface CandidateEntry {
    result: NavigableRequestResult;
    candidate: EntitySearchCandidate;
  }

  let { back = null }: Props = $props();

  const nsfw = useNsfw();

  let providers = $state.raw<PluginProvider[]>([]);
  let defaultProviders = $state<Record<string, string>>({});
  let providersLoading = $state(true);
  let providersError = $state<string | null>(null);
  let selectedKind = $state<RequestMediaKindCode | null>(null);
  let selectedProviderId = $state("");
  let searchValues = $state<Record<string, string>>({});
  let results = $state.raw<RequestSearchResult[]>([]);
  let hasSearched = $state(false);
  let searching = $state(false);
  let searchError = $state<string | null>(null);
  let providerWarnings = $state.raw<string[]>([]);
  let activeCandidateKey = $state<string | null>(null);
  let searchRevision = 0;
  let searchLimit = $state(PLUGIN_SEARCH_PAGE_SIZE);

  const hideNsfw = $derived(nsfw.mode !== "show");
  const selectedKindInfo = $derived(
    DISCOVERABLE_REQUEST_KINDS.find((kind) => kind.kind === selectedKind) ?? null,
  );
  const defaultProviderId = $derived(
    selectedKindInfo
      ? defaultProviders[selectedKindInfo.pluginEntityKind]
      : null,
  );
  const eligibleProviders = $derived(
    selectedKind
      ? discoverSearchProviders(providers, selectedKind, hideNsfw, defaultProviderId)
      : [],
  );
  const activeProvider = $derived(
    eligibleProviders.find((provider) => provider.id === selectedProviderId) ?? eligibleProviders[0] ?? null,
  );
  const activeSupport = $derived(
    activeProvider && selectedKind
      ? discoverSearchSupport(activeProvider, selectedKind, hideNsfw)
      : null,
  );
  const activeSearchFields = $derived(activeSupport?.search?.fields ?? []);
  const canSubmitSearch = $derived(
    Boolean(activeProvider) &&
      activeSearchFields.length > 0 &&
      hasRequiredPluginSearchFields(activeSearchFields, searchValues),
  );
  const candidateEntries = $derived.by(() =>
    results.flatMap((result): CandidateEntry[] => {
      if (!isNavigableResult(result)) return [];
      return [{ result, candidate: toCandidate(result) }];
    }),
  );
  const candidates = $derived(candidateEntries.map((entry) => entry.candidate));

  /**
   * The registry lists kinds in its own grouping order, which reads as arbitrary in a picker.
   * Sorting by label gives the chooser and the chip row one predictable order.
   */
  const orderedKinds = [...DISCOVERABLE_REQUEST_KINDS].sort((left, right) =>
    left.plural.localeCompare(right.plural),
  );
  const kindChoices = orderedKinds.map(kind => ({ value: kind.kind, label: kind.plural, icon: requestKindIcon(kind.kind), iconColor: requestKindAccent(kind.kind) }));

  /**
   * How many installed providers can actually search each kind. Surfacing this on the chooser
   * answers "what can I even request?" before a selection is made, instead of after.
   */
  const sourceCountByKind = $derived.by(() => {
    const counts = new Map<RequestMediaKindCode, number>();
    for (const info of DISCOVERABLE_REQUEST_KINDS) {
      counts.set(
        info.kind,
        discoverSearchProviders(
          providers,
          info.kind,
          hideNsfw,
          defaultProviders[info.pluginEntityKind] ?? null,
        ).length,
      );
    }
    return counts;
  });
  const canLoadMore = $derived(
    hasSearched && results.length >= searchLimit && searchLimit < PLUGIN_SEARCH_MAX_LIMIT,
  );
  let lastHideNsfw: boolean | null = null;

  onMount(() => {
    let mounted = true;

    void Promise.all([
      fetchPluginProviders(),
      fetchSettingsValues([settingKeys.identifyDefaultProviders]),
    ])
      .then(([loadedProviders, settings]) => {
        if (!mounted) return;
        providers = loadedProviders;
        defaultProviders = valueAsStringMap(
          settings.values[settingKeys.identifyDefaultProviders],
        );
      })
      .catch((error: unknown) => {
        if (!mounted) return;
        providersError = error instanceof Error ? error.message : "Failed to load discovery providers";
      })
      .finally(() => {
        if (mounted) providersLoading = false;
      });

    return () => {
      mounted = false;
      searchRevision += 1;
    };
  });

  // Provider eligibility is part of the NSFW boundary. A mode change invalidates both the
  // selected provider and every result returned under the previous boundary, including an
  // in-flight response. Re-seed the first newly eligible provider just like a fresh kind choice.
  $effect(() => {
    const nextHideNsfw = hideNsfw;
    if (lastHideNsfw === null) {
      lastHideNsfw = nextHideNsfw;
      return;
    }
    if (nextHideNsfw === lastHideNsfw) return;
    lastHideNsfw = nextHideNsfw;
    resetSearch();

    const kind = selectedKind;
    if (!kind) {
      selectedProviderId = "";
      searchValues = {};
      return;
    }

    const preferredProviderId = defaultProviders[
      DISCOVERABLE_REQUEST_KINDS.find((candidate) => candidate.kind === kind)?.pluginEntityKind ?? ""
    ];
    const nextProvider = discoverSearchProviders(
      providers,
      kind,
      nextHideNsfw,
      preferredProviderId,
    )[0] ?? null;
    selectedProviderId = nextProvider?.id ?? "";
    const fields = nextProvider
      ? discoverSearchSupport(nextProvider, kind, nextHideNsfw)?.search?.fields ?? []
      : [];
    searchValues = seedPluginSearchFields(fields, {}, "");
  });

  function isNavigableResult(result: RequestSearchResult): result is NavigableRequestResult {
    return Boolean(
      result.pluginId?.trim() &&
      result.externalIdentity?.namespace.trim() &&
      result.externalIdentity.value.length > 0,
    );
  }

  function toCandidate(result: NavigableRequestResult): EntitySearchCandidate {
    return {
      externalIds: { [result.externalIdentity.namespace]: result.externalIdentity.value },
      title: result.title,
      year: numericValue(result.year),
      overview: result.overview,
      posterUrl: result.posterUrl,
      popularity: null,
      candidateId: result.externalIdentity.value,
      source: result.pluginId,
      confidence: null,
      matchReason: result.subtitle,
    };
  }

  function resetSearch() {
    searchRevision += 1;
    results = [];
    hasSearched = false;
    searching = false;
    searchError = null;
    providerWarnings = [];
    activeCandidateKey = null;
    searchLimit = PLUGIN_SEARCH_PAGE_SIZE;
  }

  function chooseKind(kind: RequestMediaKindCode) {
    selectedKind = kind;
    const pluginEntityKind = DISCOVERABLE_REQUEST_KINDS
      .find((candidate) => candidate.kind === kind)?.pluginEntityKind;
    const nextProviders = discoverSearchProviders(
      providers,
      kind,
      hideNsfw,
      pluginEntityKind ? defaultProviders[pluginEntityKind] : null,
    );
    const nextProvider = nextProviders[0] ?? null;
    selectedProviderId = nextProvider?.id ?? "";
    const fields = nextProvider
      ? discoverSearchSupport(nextProvider, kind, hideNsfw)?.search?.fields ?? []
      : [];
    searchValues = seedPluginSearchFields(fields, {}, "");
    resetSearch();
  }

  function chooseProvider(providerId: string) {
    selectedProviderId = providerId;
    const provider = eligibleProviders.find((item) => item.id === providerId) ?? null;
    const fields = provider && selectedKind
      ? discoverSearchSupport(provider, selectedKind, hideNsfw)?.search?.fields ?? []
      : [];
    searchValues = seedPluginSearchFields(fields, {}, "");
    resetSearch();
  }

  function clearSearch() {
    searchValues = Object.fromEntries(activeSearchFields.map((field) => [field.key, ""]));
    resetSearch();
  }

  async function runSearch(limit = PLUGIN_SEARCH_PAGE_SIZE) {
    if (!selectedKind || !activeProvider || !canSubmitSearch) return;

    const revision = ++searchRevision;
    const loadingMore = limit > PLUGIN_SEARCH_PAGE_SIZE && results.length > 0;
    searching = true;
    searchError = null;
    providerWarnings = [];
    if (!loadingMore) results = [];
    activeCandidateKey = null;

    try {
      const response = await searchRequestsByPlugin({
        kind: selectedKind,
        pluginId: activeProvider.id,
        fields: submittedPluginSearchFields(activeSearchFields, searchValues),
        limit,
        hideNsfw,
      });
      if (revision !== searchRevision) return;

      // Plugin order is the ranking contract. Invalid legacy rows are omitted without re-sorting.
      results = response.results;
      searchLimit = limit;
      hasSearched = true;
      providerWarnings = Array.from(new Set(
        response.providerErrors.map((warning) => `${warning.displayName}: ${warning.message}`),
      ));
    } catch (error) {
      if (revision !== searchRevision) return;
      searchError = error instanceof Error ? error.message : "Search failed";
      hasSearched = true;
    } finally {
      if (revision === searchRevision) searching = false;
    }
  }

  function activateCandidate(candidate: EntitySearchCandidate, candidateKey: string) {
    const entry = candidateEntries.find((item) => item.candidate === candidate);
    if (!entry || !selectedKind) return;

    activeCandidateKey = candidateKey;
    const { result } = entry;
    const query = new URLSearchParams({
      plugin: result.pluginId,
      namespace: result.externalIdentity.namespace,
    });
    if (back?.trim()) query.set("back", back.trim());

    const href = `/request/${encodeURIComponent(selectedKind)}/${encodeURIComponent(result.externalIdentity.value)}?${query.toString()}`;
    void goto(resolve(href as "/"));
  }
</script>

<div class="space-y-4">
  <section class="surface-panel overflow-visible">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <PackageSearch class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Discover</span>
      <span class="font-mono text-[0.7rem] text-text-muted">choose a kind, then a source</span>
    </header>

    <div class="space-y-3 p-3.5">
      <div class="space-y-1.5">
        <span class="font-mono text-[0.72rem] text-text-muted">Content kind</span>
        {#if selectedKind}
          <!-- Once a kind is chosen the chooser collapses to chips so the search surface leads. -->
          <ChoiceGroup type="single" options={kindChoices} value={selectedKind} onValueChange={chooseKind} ariaLabel="Choose a content kind" />
        {:else}
          <!--
            Nothing selected is the page's real starting point, so it gets a full chooser rather
            than a strip of chips over an empty page. Each card states whether a source exists.
          -->
          <div class="kind-chooser" role="group" aria-label="Choose a content kind">
            {#each orderedKinds as kind (kind.kind)}
              {@const KindIcon = requestKindIcon(kind.kind)}
              {@const sources = sourceCountByKind.get(kind.kind) ?? 0}
              <Button variant="outline"
                class={`kind-card h-auto ${!providersLoading && sources === 0 ? "has-no-source" : ""}`}
                style={`--family-accent: ${requestKindAccent(kind.kind)}`}
                aria-label={kind.plural}
                aria-describedby={`discover-sources-${kind.kind}`}
                onclick={() => chooseKind(kind.kind)}
              >
                <span class="kind-card-rail" aria-hidden="true"></span>
                <KindIcon class="kind-card-icon" aria-hidden="true" />
                <span class="kind-card-label">{kind.plural}</span>
                <span class="kind-card-sources" id={`discover-sources-${kind.kind}`}>
                  {#if providersLoading}
                    Checking sources…
                  {:else if sources === 0}
                    No source installed
                  {:else}
                    {sources} {sources === 1 ? "source" : "sources"}
                  {/if}
                </span>
              </Button>
            {/each}
          </div>
        {/if}
      </div>

      {#if selectedKind}
        {#if providersLoading}
          <div class="flex items-center gap-2 py-2 text-[0.78rem] text-text-muted" role="status">
            <Loader2 class="h-3.5 w-3.5 animate-spin" />
            Loading discovery sources…
          </div>
        {:else if providersError}
          <Alert.Root variant="destructive">
            <AlertTriangle />
            <Alert.Description>{providersError}</Alert.Description>
          </Alert.Root>
        {:else if eligibleProviders.length === 0}
          <StatePlaceholder icon={PlugZap} title="No compatible provider"
            description={`Enable a provider in Plugins that supports ${selectedKindInfo?.plural.toLowerCase() ?? "this kind"}.`} />
        {/if}
      {/if}
    </div>
  </section>

  {#if searchError}
    <Alert.Root variant="destructive"><AlertTriangle /><Alert.Description>{searchError}</Alert.Description></Alert.Root>
  {/if}
  {#each providerWarnings as warning (warning)}
    <Alert.Root role="status"><AlertTriangle /><Alert.Description>{warning}</Alert.Description></Alert.Root>
  {/each}

  {#if selectedKind && activeProvider}
    <PluginSearchSurface
      providers={eligibleProviders}
      selectedProviderId={activeProvider.id}
      fields={activeSearchFields}
      values={searchValues}
      onProviderChange={chooseProvider}
      onValuesChange={(values) => (searchValues = values)}
      onSubmit={() => void runSearch()}
      onClear={clearSearch}
      providerLabel="Source"
      {searching}
      submitDisabled={!canSubmitSearch}
      {candidates}
      entityKind={selectedKindInfo?.entityKind ?? ENTITY_KIND.book}
      {hasSearched}
      {activeCandidateKey}
      onActivate={activateCandidate}
      onLoadMore={canLoadMore ? () => void runSearch(nextPluginSearchLimit(searchLimit)) : null}
      loadingMore={searching && results.length > 0}
    />
  {/if}
</div>

<style>
  .kind-chooser {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr));
    gap: 0.5rem;
  }

  .kind-chooser :global(.kind-card) {
    position: relative;
    display: grid;
    grid-template-columns: 3px auto minmax(0, 1fr);
    grid-template-rows: auto auto;
    align-items: center;
    gap: 0.1rem 0.6rem;
    padding: 0.85rem 0.9rem 0.85rem 0;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-2);
    text-align: left;
    cursor: pointer;
    overflow: hidden;
    transition:
      border-color var(--duration-fast, 120ms) var(--ease-default, ease),
      background var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .kind-chooser :global(.kind-card:hover),
  .kind-chooser :global(.kind-card:focus-visible) {
    border-color: var(--color-border-default);
    background: var(--color-surface-3);
    outline: none;
  }

  .kind-chooser :global(.kind-card:focus-visible) {
    border-color: var(--color-border-accent-strong);
  }

  /* The family's colour is a leading rail, keeping the card itself neutral material. */
  .kind-card-rail {
    grid-row: 1 / span 2;
    align-self: stretch;
    background: var(--family-accent);
    opacity: 0.85;
  }

  .kind-chooser :global(.kind-card .kind-card-icon) {
    grid-row: 1 / span 2;
    box-sizing: content-box;
    width: 1.15rem;
    height: 1.15rem;
    margin-left: 0.75rem;
    padding: 0.45rem;
    border-radius: var(--radius-xs);
    background: var(--color-surface-1);
    color: var(--color-text-secondary);
  }

  .kind-card-label {
    font-family: var(--font-heading);
    font-size: 0.92rem;
    font-weight: 600;
    color: var(--color-text-primary);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .kind-card-sources {
    font-family: var(--font-mono);
    font-size: 0.64rem;
    color: var(--color-text-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  /* A kind with no installed provider stays selectable so the empty-state guidance can explain why. */
  .kind-chooser :global(.kind-card.has-no-source .kind-card-label) {
    color: var(--color-text-muted);
  }

  .kind-chooser :global(.kind-card.has-no-source .kind-card-rail) {
    opacity: 0.3;
  }

  @media (prefers-reduced-motion: reduce) {
    .kind-chooser :global(.kind-card) {
      transition: none;
    }
  }
</style>
