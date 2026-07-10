<script lang="ts">
  import { onMount } from "svelte";
  import { AlertTriangle, Loader2, PackageSearch, PlugZap } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { ENTITY_KIND, type RequestMediaKindCode } from "$lib/api/generated/codes";
  import type { ExternalIdentity, RequestSearchResult } from "$lib/api/generated/model";
  import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import { searchRequestsByPlugin } from "$lib/api/requests";
  import IdentifyProviderSelect from "$lib/components/identify/IdentifyProviderSelect.svelte";
  import PluginSearchForm from "$lib/components/plugins/PluginSearchForm.svelte";
  import {
    hasRequiredPluginSearchFields,
    seedPluginSearchFields,
    submittedPluginSearchFields,
  } from "$lib/components/plugins/plugin-search-fields";
  import PluginCandidateList from "$lib/components/review/PluginCandidateList.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { discoverSearchProviders, discoverSearchSupport } from "$lib/requests/discovery-plugins";
  import { DISCOVERABLE_REQUEST_KINDS, numericValue } from "$lib/requests/request-helpers";

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

  const hideNsfw = $derived(nsfw.mode !== "show");
  const selectedKindInfo = $derived(
    DISCOVERABLE_REQUEST_KINDS.find((kind) => kind.kind === selectedKind) ?? null,
  );
  const eligibleProviders = $derived(
    selectedKind ? discoverSearchProviders(providers, selectedKind, hideNsfw) : [],
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
  let lastHideNsfw: boolean | null = null;

  onMount(() => {
    let mounted = true;

    void fetchPluginProviders()
      .then((loadedProviders) => {
        if (!mounted) return;
        providers = loadedProviders;
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

    const nextProvider = discoverSearchProviders(providers, kind, nextHideNsfw)[0] ?? null;
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
  }

  function chooseKind(kind: RequestMediaKindCode) {
    selectedKind = kind;
    const nextProviders = discoverSearchProviders(providers, kind, hideNsfw);
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

  async function runSearch() {
    if (!selectedKind || !activeProvider || !canSubmitSearch) return;

    const revision = ++searchRevision;
    searching = true;
    searchError = null;
    providerWarnings = [];
    results = [];
    activeCandidateKey = null;

    try {
      const response = await searchRequestsByPlugin({
        kind: selectedKind,
        pluginId: activeProvider.id,
        fields: submittedPluginSearchFields(activeSearchFields, searchValues),
        hideNsfw,
      });
      if (revision !== searchRevision) return;

      // Plugin order is the ranking contract. Invalid legacy rows are omitted without re-sorting.
      results = response.results;
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
    if (!entry) return;

    activeCandidateKey = candidateKey;
    const { result } = entry;
    const query = new URLSearchParams({
      plugin: result.pluginId,
      namespace: result.externalIdentity.namespace,
    });
    if (back?.trim()) query.set("back", back.trim());

    void goto(
      `/request/${encodeURIComponent(result.kind)}/${encodeURIComponent(result.externalIdentity.value)}?${query.toString()}`,
    );
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
        <div class="flex flex-wrap gap-1.5" role="group" aria-label="Choose a content kind">
          {#each DISCOVERABLE_REQUEST_KINDS as kind (kind.kind)}
            <Button
              type="button"
              size="sm"
              variant={selectedKind === kind.kind ? "primary" : "secondary"}
              aria-pressed={selectedKind === kind.kind}
              onclick={() => chooseKind(kind.kind)}
            >
              {kind.label}
            </Button>
          {/each}
        </div>
      </div>

      {#if selectedKind}
        {#if providersLoading}
          <div class="flex items-center gap-2 py-2 text-[0.78rem] text-text-muted" role="status">
            <Loader2 class="h-3.5 w-3.5 animate-spin" />
            Loading discovery sources…
          </div>
        {:else if providersError}
          <div class="flex items-start gap-2 rounded-xs border border-error/20 bg-error-muted px-3 py-2 text-[0.78rem] text-error-text" role="alert">
            <AlertTriangle class="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {providersError}
          </div>
        {:else if eligibleProviders.length === 0}
          <div class="empty-rack-slot flex items-start gap-2 p-4 text-[0.78rem] text-text-muted" role="status">
            <PlugZap class="mt-0.5 h-4 w-4 shrink-0 text-text-disabled" />
            <p>
              No installed provider can search and review
              {selectedKindInfo?.plural.toLowerCase() ?? "this kind"}.
              Enable a compatible provider in Plugins first.
            </p>
          </div>
        {:else if activeProvider}
          <div class="request-discover-provider flex max-w-md flex-col gap-1.5">
            <span class="font-mono text-[0.72rem] text-text-muted">Source</span>
            <IdentifyProviderSelect
              providers={eligibleProviders}
              selectedId={activeProvider.id}
              onChange={chooseProvider}
              label="Source"
              compact
            />
          </div>

          <PluginSearchForm
            fields={activeSearchFields}
            values={searchValues}
            onValuesChange={(values) => (searchValues = values)}
            onSubmit={() => void runSearch()}
            onClear={clearSearch}
            loading={searching}
            submitDisabled={!canSubmitSearch}
          />
        {/if}
      {:else}
        <div class="empty-rack-slot p-4 text-[0.78rem] text-text-muted">
          Choose one content kind to see the providers and search fields built for it.
        </div>
      {/if}
    </div>
  </section>

  {#if searchError}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text" role="alert">
      {searchError}
    </div>
  {/if}
  {#each providerWarnings as warning (warning)}
    <div class="surface-panel border-l-2 border-warning px-4 py-2.5 text-sm text-warning-text" role="status">
      {warning}
    </div>
  {/each}

  {#if selectedKind && activeProvider}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <span class="text-kicker text-text-accent">Candidates</span>
        {#if hasSearched}
          <span class="font-mono text-[0.7rem] text-text-muted">{candidateEntries.length} found</span>
        {/if}
      </header>
      <div class="p-3.5">
        {#if candidates.length > 0}
          <PluginCandidateList
            {candidates}
            entityKind={selectedKindInfo?.entityKind ?? ENTITY_KIND.book}
            {activeCandidateKey}
            disabled={searching}
            onActivate={activateCandidate}
          />
        {:else if searching}
          <div class="flex items-center justify-center gap-2.5 p-7 text-text-muted" role="status">
            <Loader2 class="h-4 w-4 animate-spin" />
            <span class="text-sm">Searching {activeProvider.name}…</span>
          </div>
        {:else if hasSearched}
          <div class="empty-rack-slot p-6 text-center">
            <p class="text-sm text-text-muted">No usable candidates found. Try a different search.</p>
          </div>
        {:else}
          <div class="empty-rack-slot p-6 text-center">
            <p class="text-sm text-text-muted">Enter the provider-specific details above to find candidates.</p>
          </div>
        {/if}
      </div>
    </section>
  {/if}
</div>

<style>
  .request-discover-provider :global(.provider-select) {
    width: 100%;
  }
</style>
