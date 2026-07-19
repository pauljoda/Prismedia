<script lang="ts">
  import { Layers } from "@lucide/svelte";
  import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";
  import IdentifyRejectQueueActions from "./IdentifyRejectQueueActions.svelte";
  import PluginSearchSurface from "$lib/components/plugins/PluginSearchSurface.svelte";
  import {
    nextPluginSearchLimit,
    PLUGIN_SEARCH_MAX_LIMIT,
    PLUGIN_SEARCH_PAGE_SIZE,
  } from "$lib/components/plugins/plugin-search-paging";
  import UniversalLightbox from "$lib/components/UniversalLightbox.svelte";
  import { IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
  import type { EntitySearchCandidate } from "$lib/api/identify-types";
  import type { EntityCard } from "$lib/api/entities";
  import type { UniversalLightboxEntity } from "$lib/components/universal-lightbox-media";
  import { supportedProviderId } from "./identify-provider-selection";
  import { providerSeekOrder } from "./identify-provider-seek";
  import { useIdentifyStore, type IdentifyQueueItem } from "./identify-store.svelte";
  import {
    hasRequiredPluginSearchFields,
    pluginSearchCompatibilityTitle,
    seedPluginSearchFields,
    submittedPluginSearchFields,
  } from "$lib/components/plugins/plugin-search-fields";

  interface Props {
    entity: EntityCard;
    candidates: EntitySearchCandidate[];
    providerId?: string | null;
    hasSearched?: boolean;
    resultRevision?: string | null;
  }

  let {
    entity,
    candidates,
    providerId = null,
    hasSearched = candidates.length > 0,
    resultRevision = null,
  }: Props = $props();

  const store = useIdentifyStore();
  const entityTypeLabel = $derived(
    entity.meta.find((item) => item.icon === "book" && /^(book|comic|manga|novel)$/i.test(item.label))?.label ?? entity.kind,
  );
  let searchValuesBySchema = $state<Record<string, Record<string, string>>>({});
  let selectedProviderId = $state<string | null>(null);
  let searchedProviderId = $state<string | null>(null);
  let searching = $state(false);
  let seeking = $state(false);
  let rescanning = $state(false);
  let checkingCandidateKey = $state<string | null>(null);
  let checkingCandidateTitle = $state<string | null>(null);
  let previewCandidate = $state<UniversalLightboxEntity | null>(null);
  let searchedCandidates = $state<EntitySearchCandidate[] | null>(null);
  let invalidatedResultRevision = $state<string | null>(null);
  let candidatesInvalidated = $state(false);
  let searchLimit = $state(PLUGIN_SEARCH_PAGE_SIZE);

  const providerOptions = $derived(store.providersForKind(entity.kind));
  const activeProviderId = $derived(supportedProviderId(providerOptions, selectedProviderId, providerId));
  const activeProvider = $derived(
    providerOptions.find((provider) => provider.id === activeProviderId) ?? null,
  );
  const activeSearchFields = $derived(
    activeProvider?.supports.find((support) => support.entityKind === entity.kind)?.search?.fields ?? [],
  );
  const activeSearchFormKey = $derived(
    `${entity.id}:${activeProviderId}:${activeSearchFields.map((field) => field.key).join("|")}`,
  );
  const searchValues = $derived(
    searchValuesBySchema[activeSearchFormKey] ?? seedPluginSearchFields(activeSearchFields, {}, entity.title),
  );
  const submittedSearchValues = $derived(submittedPluginSearchFields(activeSearchFields, searchValues));
  const canSubmitSearch = $derived(
    Object.keys(submittedSearchValues).length > 0 &&
      hasRequiredPluginSearchFields(activeSearchFields, searchValues),
  );
  const candidateProvider = $derived(
    (searchedProviderId ? store.providers.find((provider) => provider.id === searchedProviderId) : null) ??
      (providerId ? store.providers.find((provider) => provider.id === providerId) : null) ??
      activeProvider,
  );
  const queryInFlight = $derived(searching || seeking || rescanning || store.isItemBusy(entity.id));
  const localCandidates = $derived(
    queryInFlight
      ? []
      : searchedCandidates ??
        (candidatesInvalidated && resultRevision === invalidatedResultRevision ? [] : candidates),
  );
  const nextQueueItem = $derived(store.nextQueueItem(entity.id));
  const previewEntities = $derived(previewCandidate ? [previewCandidate] : []);
  const seekDisabled = $derived(searching || seeking || rescanning || store.isItemBusy(entity.id) || providerOptions.length === 0);
  const searchStatus = $derived(store.itemSearchStatus(entity.id));
  const surfaceSearching = $derived(searching || store.isItemBusy(entity.id));
  const canLoadMore = $derived(
    !queryInFlight &&
      localCandidates.length >= searchLimit &&
      searchLimit < PLUGIN_SEARCH_MAX_LIMIT,
  );

  // Navigating between items reuses this component instance, so local search state must be cleared
  // when the entity changes — otherwise a previous item's searched candidates and query stick around.
  let lastEntityId = $state<string | null>(null);
  $effect(() => {
    if (entity.id === lastEntityId) return;
    lastEntityId = entity.id;
    searchedCandidates = null;
    searchedProviderId = null;
    selectedProviderId = null;
    checkingCandidateKey = null;
    checkingCandidateTitle = null;
    previewCandidate = null;
    invalidatedResultRevision = null;
    candidatesInvalidated = false;
    searchLimit = PLUGIN_SEARCH_PAGE_SIZE;
  });

  function setSearchValues(values: Record<string, string>) {
    searchValuesBySchema = { ...searchValuesBySchema, [activeSearchFormKey]: values };
  }

  function chooseProvider(providerId: string) {
    selectedProviderId = providerId;
    searchLimit = PLUGIN_SEARCH_PAGE_SIZE;
  }

  async function handleRescan() {
    if (!activeProvider || rescanning) return;
    rescanning = true;
    store.error = null;
    searchedCandidates = null;
    searchedProviderId = null;
    invalidatedResultRevision = resultRevision;
    candidatesInvalidated = true;
    try {
      const result = await store.identifyEntity(entity, activeProvider.id);
      if (result?.state === IDENTIFY_QUEUE_STATE.search) {
        searchedCandidates = result.candidates;
        searchedProviderId = result.provider ?? activeProvider.id;
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Rescan failed";
    } finally {
      rescanning = false;
    }
  }

  async function handleSearch(limit = PLUGIN_SEARCH_PAGE_SIZE) {
    if (!activeProvider || !canSubmitSearch) return;
    searchLimit = limit;
    searching = true;
    store.error = null;
    searchedCandidates = null;
    searchedProviderId = null;
    invalidatedResultRevision = resultRevision;
    candidatesInvalidated = true;
    try {
      // Manual searches always come back as candidates: a stored external id must not
      // re-lock the entity onto the match the user is here to change.
      const result = await store.identifyEntity(entity, activeProvider.id, {
        title: pluginSearchCompatibilityTitle(activeSearchFields, searchValues, entity.title),
        fields: submittedSearchValues,
        requireChoice: true,
        limit,
      });
      if (result?.state === IDENTIFY_QUEUE_STATE.search) {
        searchedCandidates = result.candidates;
        searchedProviderId = result.provider ?? activeProvider.id;
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Search failed";
    } finally {
      searching = false;
    }
  }

  async function handleSeek() {
    if (seekDisabled) return;

    const providerIds = providerOptions.map((provider) => provider.id);
    const orderedProviderIds = providerSeekOrder(providerIds, activeProviderId);
    const title = pluginSearchCompatibilityTitle(activeSearchFields, searchValues, entity.title) || null;

    if (orderedProviderIds.length === 0) return;

    seeking = true;
    store.error = null;
    searchedCandidates = null;
    searchedProviderId = null;
    invalidatedResultRevision = resultRevision;
    candidatesInvalidated = true;
    try {
      for (const seekProviderId of orderedProviderIds) {
        selectedProviderId = seekProviderId;
        const queued = await store.identifyEntity(entity, seekProviderId, {
          title,
          fields:
            seekProviderId === activeProviderId && Object.keys(submittedSearchValues).length > 0
              ? submittedSearchValues
              : undefined,
        });
        if (!queued) continue;

        const result = await store.waitForIdentifyResult(entity.id, seekProviderId);
        if (!result || !isSeekResult(result)) continue;

        searchedProviderId = result.provider ?? seekProviderId;
        if (result.state === IDENTIFY_QUEUE_STATE.search) {
          searchedCandidates = result.candidates;
          return;
        }

        store.reviewResolvedQueueItem(result);
        return;
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Seek failed";
    } finally {
      seeking = false;
    }
  }

  function isSeekResult(item: IdentifyQueueItem): boolean {
    return isReviewableIdentifyResult(item);
  }

  function isReviewableIdentifyResult(item: IdentifyQueueItem): boolean {
    return (
      (item.state === IDENTIFY_QUEUE_STATE.proposal && Boolean(item.proposal)) ||
      (item.state === IDENTIFY_QUEUE_STATE.search && item.candidates.length > 0)
    );
  }

  async function pickCandidate(candidate: EntitySearchCandidate, candidateKey: string) {
    if (!candidateProvider || store.isItemBusy(entity.id)) return;
    const providerId = candidateProvider.id;
    checkingCandidateKey = candidateKey;
    checkingCandidateTitle = candidate.title;
    store.error = null;
    try {
      const result = await store.identifyWithCandidate(entity, providerId, candidate);
      if (result && isReviewableIdentifyResult(result)) {
        store.reviewResolvedQueueItem(result);
      }
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Candidate identify failed";
    } finally {
      if (checkingCandidateKey === candidateKey) {
        checkingCandidateKey = null;
        checkingCandidateTitle = null;
      }
    }
  }

  function candidateTitle(candidate: EntitySearchCandidate): string {
    return candidate.title?.trim() || "Untitled match";
  }

  function openCandidatePreview(candidate: EntitySearchCandidate, candidateKey: string) {
    if (!candidate.posterUrl) return;
    previewCandidate = {
      id: `candidate-${candidateKey}`,
      kind: entity.kind,
      title: candidateTitle(candidate),
      capabilities: [],
      coverUrl: candidate.posterUrl,
      isNsfw: entity.isNsfw,
    };
  }

  function closeCandidatePreview() {
    previewCandidate = null;
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Preview of what we are identifying (collapsed by default) -->
  <IdentifyTargetPreview {entity} />

  <!-- Entity context bar -->
  <div class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well">
    {#if entity.coverUrl}
      <img src={entity.coverUrl} alt="" class="h-16 w-11 rounded-xs object-cover" decoding="async" />
    {:else}
      <div class="grid h-16 w-11 place-items-center rounded-xs bg-surface-3">
        <Layers class="h-5 w-5 text-text-disabled" />
      </div>
    {/if}
    <div class="min-w-0">
      <div class="flex items-baseline gap-2">
        <h2 class="truncate">{entity.title}</h2>
        <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] text-phosphor-600">
          {entityTypeLabel}
        </span>
      </div>
      <div class="mt-0.5 truncate font-mono text-[0.7rem] text-text-muted">awaiting match</div>
    </div>
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Candidates</span>
      <span class="font-mono font-semibold text-text-accent">{localCandidates.length}</span>
    </div>
    {#if activeProvider}
      <div class="hidden flex-col items-end gap-0.5 md:flex">
        <span class="text-kicker">Provider</span>
        <span class="text-[0.82rem] text-text-primary">{activeProvider.name}</span>
      </div>
    {/if}
  </div>

  <PluginSearchSurface
    providers={providerOptions}
    selectedProviderId={activeProviderId}
    fields={activeSearchFields}
    values={searchValues}
    onProviderChange={chooseProvider}
    onValuesChange={setSearchValues}
    onSubmit={() => void handleSearch()}
    onClear={() => setSearchValues(Object.fromEntries(activeSearchFields.map((field) => [field.key, ""])))}
    title="Query"
    description="refine and re-search"
    searching={surfaceSearching}
    disabled={seeking}
    submitDisabled={!canSubmitSearch}
    candidates={localCandidates}
    entityKind={entity.kind}
    {hasSearched}
    activeCandidateKey={checkingCandidateKey}
    candidateDisabled={store.isItemBusy(entity.id)}
    candidateStatus={checkingCandidateTitle
      ? "Match found. Identifying related items; this may take a while."
      : null}
    {searchStatus}
    onActivate={(candidate, candidateKey) => void pickCandidate(candidate, candidateKey)}
    onPreview={openCandidatePreview}
    onSeek={() => void handleSeek()}
    {seeking}
    {seekDisabled}
    onRescan={() => void handleRescan()}
    {rescanning}
    onLoadMore={canLoadMore ? () => void handleSearch(nextPluginSearchLimit(searchLimit)) : null}
    loadingMore={surfaceSearching && searchLimit > PLUGIN_SEARCH_PAGE_SIZE}
    noProvidersMessage={`No enabled provider supports ${entity.kind}.`}
  />

  <div class="flex flex-col gap-2 py-2 md:flex-row md:justify-end">
    <IdentifyRejectQueueActions
      entityId={entity.id}
      showNext={Boolean(nextQueueItem)}
      disabled={store.isItemBusy(entity.id)}
    />
  </div>

  {#if previewEntities.length > 0}
    <UniversalLightbox
      entities={previewEntities}
      initialIndex={0}
      onClose={closeCandidatePreview}
      showRatingControls={false}
      sharedKey="identify-candidate-preview"
    />
  {/if}
</div>
