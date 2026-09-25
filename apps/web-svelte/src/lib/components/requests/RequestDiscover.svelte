<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { AlertTriangle, PackageSearch, PlugZap, ArrowLeft, ArrowUpRight, Library } from "@lucide/svelte";
  import { Alert, Button, ChoiceGroup, Select } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { ENTITY_KIND, PLUGIN_CAPABILITY, CONNECTION_STATUS, type RequestMediaKindCode } from "$lib/api/generated/codes";
  import { fetchSettingsValues } from "$lib/api/settings";
  import type { ConnectionResponse, ExternalIdentity, RequestSearchResult } from "$lib/api/generated/model";
  import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import { searchRequestsByPlugin } from "$lib/api/requests";
  import PluginSearchForm from "$lib/components/plugins/PluginSearchForm.svelte";
  import DiscoveryResults from "./DiscoveryResults.svelte";
  import ConnectionCatalogBrowser from "$lib/components/integrations/ConnectionCatalogBrowser.svelte";
  import ConnectedLibraryBrowser from "$lib/components/integrations/ConnectedLibraryBrowser.svelte";
  import ConnectionCapabilityChips from "$lib/components/integrations/ConnectionCapabilityChips.svelte";
  import ManagerSourceBrowser from "$lib/components/integrations/ManagerSourceBrowser.svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
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
  import { canBrowseRequestSource, requestSourceMode } from "$lib/requests/request-source-compatibility";
  import { settingKeys, valueAsStringMap } from "$lib/settings/app-settings";
  import { mediaFamilyForKind, mediaFamilyOrder, type MediaFamily } from "$lib/entities/media-families";
  import RequestFamilyCard, { type RequestFamilyKind } from "./RequestFamilyCard.svelte";

  interface Props {
    /** Search-page query state to restore when the review page's Back action is used. */
    back?: string | null;
    connections?: ConnectionResponse[];
    initialConnectionId?: string | null;
    initialKind?: RequestMediaKindCode | null;
    onConnectionChange?: (id: string | null) => void;
    onKindChange?: (kind: RequestMediaKindCode | null) => void;
    /** Concept: family cards and a compact kind-and-source bar instead of tiles and helper text. */
    preview?: boolean;
  }

  type NavigableRequestResult = RequestSearchResult & {
    pluginId: string;
    externalIdentity: ExternalIdentity;
  };

  interface CandidateEntry {
    result: NavigableRequestResult;
    candidate: EntitySearchCandidate;
  }

  let {
    back = null,
    connections = [],
    initialConnectionId = null,
    initialKind,
    onConnectionChange,
    onKindChange,
    preview = false,
  }: Props = $props();
  // Preserve a browse draft while switching between workspace tabs.
  let selectedConnectionId = $state(untrack(() => initialConnectionId ?? ""));
  const selectedConnection = $derived(connections.find(item => item.id === selectedConnectionId));
  const browseConnections = $derived(connections.filter(item => canBrowseRequestSource(item)));
  const compatibleBrowseConnections = $derived.by(() => {
    const entityKind = selectedKindInfo?.entityKind;
    if (!entityKind) return browseConnections;
    return browseConnections.filter((source) => canBrowseRequestSource(source, entityKind));
  });
  function chooseSource(id: string) {
    const source = compatibleBrowseConnections.find(item => item.id === id);
    selectedConnectionId = source?.id ?? "";
    onConnectionChange?.(source?.id ?? null);
    if (!source) chooseProvider(id);
  }

  const nsfw = useNsfw();

  let providers = $state.raw<PluginProvider[]>([]);
  let defaultProviders = $state<Record<string, string>>({});
  let providersLoading = $state(true);
  let providersError = $state<string | null>(null);
  let selectedKind = $state<RequestMediaKindCode | null>(untrack(() => initialKind ?? null));
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
  const connection = $derived(selectedConnection && canBrowseRequestSource(selectedConnection, selectedKindInfo?.entityKind)
    ? selectedConnection : undefined);
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
  $effect(() => {
    const nextConnectionId = initialConnectionId ?? "";
    untrack(() => {
      if (nextConnectionId === selectedConnectionId) return;
      selectedConnectionId = nextConnectionId;
    });
  });
  $effect(() => {
    if (initialKind === undefined) return;
    const nextKind = initialKind ?? null;
    untrack(() => {
      if (nextKind === selectedKind) return;
      if (!nextKind) {
        selectedKind = null;
        selectedProviderId = "";
        searchValues = {};
        resetSearch();
        return;
      }
      applyKind(nextKind);
    });
  });
  $effect(() => {
    const source = selectedConnection;
    const entityKind = selectedKindInfo?.entityKind;
    if (!source || !entityKind || canBrowseRequestSource(source, entityKind)) return;
    selectedConnectionId = "";
    onConnectionChange?.(null);
  });
  const sourceOptions = $derived([
    ...eligibleProviders.map(item => ({ value: item.id, label: item.name, annotation: "Find new titles" })),
    ...compatibleBrowseConnections.map(item => ({ value: item.id, label: item.name,
      annotation: item.status !== CONNECTION_STATUS.ready ? "Unavailable"
        : requestSourceMode(item, selectedKindInfo?.entityKind) === PLUGIN_CAPABILITY.externalManager ? "Find & request"
        : requestSourceMode(item, selectedKindInfo?.entityKind) === PLUGIN_CAPABILITY.connectedLibrary ? "Your collection" : "Browse & import" })),
  ]);
  function sourceIconUrl(id: string) {
    const pluginId = connections.find(item => item.id === id)?.pluginId ?? id;
    return providers.find(provider => provider.id === pluginId)?.iconUrl;
  }
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
   * How many installed providers or connected sources can serve each kind. Surfacing this on the
   * chooser answers "what can I even request?" before a selection is made, instead of after.
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
        ).length + connections.filter((connection) => canBrowseRequestSource(connection, info.entityKind)).length,
      );
    }
    return counts;
  });
  /** The requestable kinds grouped by media family, each with the sources that can find it. */
  const familyGroups = $derived.by(() => {
    const groups = new Map<string, { family: MediaFamily; kinds: RequestFamilyKind[] }>();
    for (const info of orderedKinds) {
      const family = mediaFamilyForKind(info.entityKind);
      let group = groups.get(family.key);
      if (!group) {
        group = { family, kinds: [] };
        groups.set(family.key, group);
      }
      const searchSources = discoverSearchProviders(
        providers,
        info.kind,
        hideNsfw,
        defaultProviders[info.pluginEntityKind] ?? null,
      ).map((provider) => ({ id: provider.id, name: provider.name, iconUrl: provider.iconUrl }));
      const browseSources = connections
        .filter((source) => canBrowseRequestSource(source, info.entityKind))
        .map((source) => ({ id: source.id, name: source.name, iconUrl: sourceIconUrl(source.id) }));
      group.kinds.push({ kind: info.kind, label: info.plural, icon: requestKindIcon(info.kind), sources: [...searchSources, ...browseSources] });
    }
    return [...groups.values()].sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family));
  });
  const activeSourceId = $derived(connection?.id ?? activeProvider?.id ?? "");

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

  function applyKind(kind: RequestMediaKindCode) {
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

  function chooseKind(kind: RequestMediaKindCode) {
    applyKind(kind);
    onKindChange?.(kind);
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

  function resetToHome() {
    selectedKind = null;
    selectedProviderId = "";
    searchValues = {};
    resetSearch();
    onKindChange?.(null);
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
    if (preview) query.set("layout", "preview");

    const href = `/request/${encodeURIComponent(selectedKind)}/${encodeURIComponent(result.externalIdentity.value)}?${query.toString()}`;
    void goto(resolve(href as "/"));
  }
</script>

<div class="space-y-5">
  {#if preview}
    {#if !selectedKind && !connection}
      <div class="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-3" role="group" aria-label="Choose what to find">
        {#each familyGroups as group (group.family.key)}
          <RequestFamilyCard
            family={group.family}
            kinds={group.kinds}
            ready={!providersLoading}
            onChoose={(kind) => chooseKind(kind as RequestMediaKindCode)}
          />
        {/each}
      </div>
      {#if browseConnections.length}
        <section class="flex flex-col gap-2" aria-labelledby="request-sources">
          <h2 id="request-sources" class="font-heading text-base font-semibold text-text-primary">
            Sources <span class="ml-1 font-mono text-caption font-normal text-text-muted">{browseConnections.length}</span>
          </h2>
          <ul class="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
            {#each browseConnections as source (source.id)}
              <li>
                <Button variant="outline" class="h-auto w-full min-w-0 justify-start gap-3 p-3 text-left" onclick={() => chooseSource(source.id)}>
                  <PluginIcon name={source.name} iconUrl={sourceIconUrl(source.id)} class="size-8" />
                  <span class="min-w-0 flex-1">
                    <span class="block truncate text-sm font-semibold">{source.name}</span>
                    <span class="mt-1 block text-xs font-normal text-text-muted"><ConnectionCapabilityChips connection={source} /></span>
                  </span>
                  <ArrowUpRight class="size-4 shrink-0 text-text-muted" aria-hidden="true" />
                </Button>
              </li>
            {/each}
          </ul>
        </section>
      {/if}
    {:else}
      <div class="flex flex-wrap items-center gap-2 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-2">
        <Button variant="ghost" size="icon-sm" aria-label="All media types" title="All media types"
          onclick={() => { selectedConnectionId = ""; onConnectionChange?.(null); resetToHome(); }}>
          <ArrowLeft aria-hidden="true" />
        </Button>
        {#if selectedKind}
          <Select
            ariaLabel="Media type"
            size="sm"
            class="w-44"
            options={kindChoices.map((choice) => ({ value: choice.value, label: choice.label }))}
            value={selectedKind}
            onchange={(value) => chooseKind(value as RequestMediaKindCode)}
          >
            {#snippet optionLeading(option)}
              {@const OptionIcon = requestKindIcon(option.value as RequestMediaKindCode)}
              <OptionIcon class="size-4 text-text-muted" aria-hidden="true" />
            {/snippet}
          </Select>
        {/if}
        {#if sourceOptions.length}
          <span class="mx-1 hidden h-5 w-px bg-[var(--color-border-subtle)] sm:block" aria-hidden="true"></span>
          <div class="flex min-w-0 flex-wrap gap-1" role="group" aria-label="Source">
            {#each sourceOptions as option (option.value)}
              {@const active = option.value === activeSourceId}
              <Button
                variant="ghost"
                size="sm"
                class={active ? "bg-[var(--color-surface-3)] text-text-primary" : "text-text-secondary"}
                aria-pressed={active}
                title={option.annotation}
                onclick={() => chooseSource(option.value)}
              >
                <PluginIcon name={option.label} iconUrl={sourceIconUrl(option.value)} class="size-5" />
                {option.label}
              </Button>
            {/each}
          </div>
        {/if}
      </div>
    {/if}
  {:else}
  {#if !connection}
    <section aria-label="Choose what to find" class="space-y-3">
      {#if selectedKind}
        <ChoiceGroup type="single" options={kindChoices} value={selectedKind} onValueChange={chooseKind} ariaLabel="Choose a content kind" />
      {:else}
        <div class="space-y-1"><h2 class="text-lg font-semibold">What would you like to find?</h2><p class="text-sm text-text-muted">Choose a media type, or browse one of your connected sources below.</p></div>
        <div class="kind-chooser" role="group" aria-label="Choose a content kind">
          {#each orderedKinds as kind (kind.kind)}
            {@const KindIcon = requestKindIcon(kind.kind)}
            {@const sources = sourceCountByKind.get(kind.kind) ?? 0}
            <Button variant="outline" class={`kind-card h-auto ${!providersLoading && sources === 0 ? "has-no-source" : ""}`}
              style={`--family-accent: ${requestKindAccent(kind.kind)}`} aria-label={kind.plural} onclick={() => chooseKind(kind.kind)}>
              <span class="kind-card-rail" aria-hidden="true"></span><KindIcon class="kind-card-icon" aria-hidden="true" />
              <span class="kind-card-label">{kind.plural}</span>
              <span class="kind-card-sources">{providersLoading ? "Checking sources…" : sources ? `${sources} ${sources === 1 ? "source" : "sources"}` : "Add a source"}</span>
            </Button>
          {/each}
        </div>
      {/if}
    </section>
  {/if}

  {#if !selectedKind && !connection && browseConnections.length}
    <section class="space-y-3" aria-label="Your sources">
      <div class="space-y-1"><h2 class="text-lg font-semibold">Your sources</h2><p class="text-sm text-text-muted">Explore the collections and services you’ve connected.</p></div>
      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {#each browseConnections as source (source.id)}
          {@const sourceMode = requestSourceMode(source)}
          <Button variant="outline" class="h-auto min-w-0 justify-start gap-3 p-4 text-left" onclick={() => chooseSource(source.id)}>
            <PluginIcon name={source.name} iconUrl={sourceIconUrl(source.id)} class="size-7" />
            <span class="min-w-0 flex-1"><span class="block truncate text-sm font-semibold">{source.name}</span><span class="mt-1 block text-xs font-normal text-text-muted"><ConnectionCapabilityChips connection={source} /></span></span>
            <ArrowUpRight class="size-4 shrink-0 text-text-muted" />
          </Button>
        {/each}
      </div>
    </section>
  {:else if sourceOptions.length}
    <div class="flex flex-col gap-2 sm:flex-row sm:items-end sm:gap-4">
      <label class="w-full space-y-1.5 sm:max-w-sm"><span class="text-xs font-medium text-text-muted">Source</span>
        <Select ariaLabel="Source" options={sourceOptions} value={connection?.id ?? activeProvider?.id ?? ""}
          placeholder="Browse a connected source" onchange={chooseSource}>
          {#snippet optionLeading(option)}<PluginIcon name={option.label} iconUrl={sourceIconUrl(option.value)} class="size-5" />{/snippet}
        </Select>
      </label>
      {#if connection}<Button variant="ghost" size="sm" onclick={() => { selectedConnectionId = ""; resetToHome(); onConnectionChange?.(null); }}><ArrowLeft />Back to Request</Button>
      {:else if activeProvider}<p class="pb-2 text-xs text-text-muted">Search {activeProvider.name}, then choose how to add a title.</p>{/if}
    </div>
  {/if}

  {/if}

  {#if connection}
    {#key connection.id}
      {#if requestSourceMode(connection, selectedKindInfo?.entityKind) === PLUGIN_CAPABILITY.externalManager}<ManagerSourceBrowser {connection} initialEntityKind={selectedKindInfo?.entityKind} />
      {:else if requestSourceMode(connection, selectedKindInfo?.entityKind) === PLUGIN_CAPABILITY.connectedLibrary}<ConnectedLibraryBrowser {connection} initialEntityKind={selectedKindInfo?.entityKind} />
      {:else}<ConnectionCatalogBrowser {connection} initialEntityKind={selectedKindInfo?.entityKind} />{/if}
    {/key}
  {:else}
    {#if providersLoading}<StatePlaceholder icon={PackageSearch} title="Loading sources" busy />
    {:else if providersError}<Alert.Root variant="destructive"><Alert.Description>{providersError}</Alert.Description></Alert.Root>
    {:else if selectedKind && !eligibleProviders.length && !compatibleBrowseConnections.length}
      <StatePlaceholder icon={PlugZap} title="No compatible provider" description={`Enable a source in Plugins for ${selectedKindInfo?.plural.toLowerCase() ?? "this media type"}.`} />
      <a class="text-sm underline" href="/plugins">Browse plugins</a>
    {:else if selectedKind && !eligibleProviders.length && compatibleBrowseConnections.length}
      <StatePlaceholder icon={Library} title="Browse a compatible source" description="Choose a source above to browse its catalog or connected library." />
    {/if}
    {#if searchError}<Alert.Root variant="destructive"><AlertTriangle /><Alert.Description>{searchError}</Alert.Description></Alert.Root>{/if}
    {#each providerWarnings as warning (warning)}<Alert.Root role="status"><Alert.Description>{warning}</Alert.Description></Alert.Root>{/each}
    {#if selectedKind && activeProvider}
      <div class="surface-panel p-4">
        <PluginSearchForm compact fields={activeSearchFields} values={searchValues} onValuesChange={values => searchValues = values}
          onSubmit={() => void runSearch()} onClear={clearSearch} loading={searching} submitDisabled={!canSubmitSearch} />
      </div>
      {#if candidates.length}
        <div class="flex items-baseline justify-between gap-3"><h2 class="text-lg font-semibold">Search results</h2><span class="font-mono text-xs text-text-muted">{candidates.length} titles</span></div>
        <DiscoveryResults cards={candidateEntries.map(({ candidate, result }, index) => entityReferenceToThumbnailCard({
          id: String(index), kind: selectedKindInfo?.entityKind ?? ENTITY_KIND.book, title: candidate.title ?? "Untitled", thumbnailUrl: candidate.posterUrl,
        }, { subtitle: [result.year, result.subtitle].filter(Boolean).join(" · ") }))}
          disabled={searching} onActivate={card => { const entry = candidateEntries[Number(card.entity.id)]; if (entry) activateCandidate(entry.candidate, card.entity.id); }} />
        {#if canLoadMore}<div class="flex justify-center"><Button variant="secondary" disabled={searching} onclick={() => void runSearch(nextPluginSearchLimit(searchLimit))}>{searching ? "Loading more…" : "Load more"}</Button></div>{/if}
      {:else if searching}<StatePlaceholder icon={PackageSearch} title={`Searching ${activeProvider.name}`} busy />
      {:else if hasSearched}<StatePlaceholder icon={PackageSearch} title="No matching titles" description={preview ? undefined : "Try a different search or choose another source."} />
      {:else if !preview}<StatePlaceholder icon={PackageSearch} title={`Find your next ${selectedKindInfo?.label.toLowerCase() ?? "title"}`} description="Search by title or use the extra details above to narrow your results." />{/if}
    {/if}
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
