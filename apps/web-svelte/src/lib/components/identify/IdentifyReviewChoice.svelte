<script lang="ts">
  import { Layers, Loader2, RefreshCw, ScanSearch, Search } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import IdentifyProviderSelect from "./IdentifyProviderSelect.svelte";
  import IdentifyTargetPreview from "./IdentifyTargetPreview.svelte";
  import IdentifyRejectQueueActions from "./IdentifyRejectQueueActions.svelte";
  import PluginSearchForm from "$lib/components/plugins/PluginSearchForm.svelte";
  import PluginCandidateList from "$lib/components/review/PluginCandidateList.svelte";
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
  }

  let { entity, candidates, providerId = null }: Props = $props();

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
  const localCandidates = $derived(searchedCandidates ?? candidates);
  const nextQueueItem = $derived(store.nextQueueItem(entity.id));
  const previewEntities = $derived(previewCandidate ? [previewCandidate] : []);
  const seekDisabled = $derived(searching || seeking || rescanning || store.isItemBusy(entity.id) || providerOptions.length === 0);

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
  });

  function setSearchValues(values: Record<string, string>) {
    searchValuesBySchema = { ...searchValuesBySchema, [activeSearchFormKey]: values };
  }

  async function handleRescan() {
    if (!activeProvider || rescanning) return;
    rescanning = true;
    store.error = null;
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

  async function handleSearch() {
    if (!activeProvider || !canSubmitSearch) return;
    searching = true;
    store.error = null;
    try {
      // Manual searches always come back as candidates: a stored external id must not
      // re-lock the entity onto the match the user is here to change.
      const result = await store.identifyEntity(entity, activeProvider.id, {
        title: pluginSearchCompatibilityTitle(activeSearchFields, searchValues, entity.title),
        fields: submittedSearchValues,
        requireChoice: true,
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

  <!-- Manual search panel -->
  <section class="surface-panel relative z-20 overflow-visible">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Search class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Query</span>
      <span class="font-mono text-[0.7rem] text-text-muted">refine and re-search</span>
      <div class="flex-1"></div>
      <button
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.72rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary disabled:opacity-40"
        disabled={rescanning}
        onclick={handleRescan}
      >
        <RefreshCw class={cn("h-3 w-3", rescanning && "animate-spin")} />
        Rescan
      </button>
    </header>
    <div class="identify-query-form flex flex-col gap-3 p-3.5">
      {#if providerOptions.length > 0}
        <div class="grid min-w-0 grid-cols-1 gap-2 sm:grid-cols-[minmax(12rem,32rem)_auto] sm:items-end">
          <div class="identify-query-field identify-query-provider flex min-w-0 flex-col gap-1.5">
            <span class="font-mono text-[0.72rem] text-text-muted">Provider</span>
            <IdentifyProviderSelect
              providers={providerOptions}
              selectedId={activeProviderId}
              onChange={(providerId) => (selectedProviderId = providerId)}
              compact
            />
          </div>
          <button
            type="button"
            class="inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:border-border-accent hover:text-text-accent disabled:cursor-not-allowed disabled:opacity-40 sm:w-24"
            disabled={seekDisabled}
            onclick={handleSeek}
          >
            {#if seeking}
              <Loader2 class="h-3.5 w-3.5 animate-spin" />
            {:else}
              <ScanSearch class="h-3.5 w-3.5" />
            {/if}
            Seek
          </button>
        </div>
      {/if}
      <PluginSearchForm
        fields={activeSearchFields}
        values={searchValues}
        onValuesChange={setSearchValues}
        onSubmit={() => void handleSearch()}
        onClear={() => setSearchValues(Object.fromEntries(activeSearchFields.map((field) => [field.key, ""])))}
        loading={searching}
        disabled={seeking}
        submitDisabled={!canSubmitSearch}
      />
    </div>
  </section>

  <!-- Candidates list -->
  <section class="surface-panel relative z-0 overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <span class="text-kicker text-text-accent">Candidates</span>
      <span class="font-mono text-[0.7rem] text-text-muted">{localCandidates.length} found</span>
      {#if checkingCandidateTitle}
        <span class="hidden font-mono text-[0.7rem] text-text-muted md:inline">
          Match found. Identifying related items; this may take a while.
        </span>
      {/if}
    </header>
    <div class="p-3.5">
      <PluginCandidateList
        candidates={localCandidates}
        entityKind={entity.kind}
        activeCandidateKey={checkingCandidateKey}
        disabled={store.isItemBusy(entity.id)}
        onActivate={(candidate, candidateKey) => void pickCandidate(candidate, candidateKey)}
        onPreview={openCandidatePreview}
      />
    </div>
  </section>

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

<style>
  .identify-query-provider :global(.provider-select) {
    width: 100%;
  }
</style>
