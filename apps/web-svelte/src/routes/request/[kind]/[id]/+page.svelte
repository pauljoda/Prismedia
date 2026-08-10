<script lang="ts">
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { ChevronLeft, Loader2, RefreshCw, Send } from "@lucide/svelte";
  import { Button, Select } from "@prismedia/ui-svelte";
  import { ENTITY_KIND, PROBLEM_CODE, REQUEST_COMMIT_OUTCOME, REQUEST_REVIEW_SELECTION } from "$lib/api/generated/codes";
  import type {
    MonitorPresetCode,
    RequestMediaKindCode,
  } from "$lib/api/generated/codes";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { ApiError } from "$lib/api/orval-fetch";
  import { commitReviewedRequest, fetchRequestReview, reviewRequest } from "$lib/api/requests";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import {
    buildRootReviewApplyPayload,
    defaultFieldSelectionForReview,
    defaultImageSelectionForReview,
    entityKindLabel,
    mergeProgressiveReviewSelectionDefaults,
    proposalHasField,
    relationshipProposals,
    reviewDiffFieldKeys,
    structuralChildProposals,
  } from "$lib/components/identify-review";
  import MetadataProposalReview from "$lib/components/review/MetadataProposalReview.svelte";
  import ProposalReviewSummary from "$lib/components/review/ProposalReviewSummary.svelte";
  import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";
  import { resolveEntityHref } from "$lib/entities/entity-codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    DEFAULT_MONITOR_PRESET,
    MONITOR_PRESET_CUSTOM,
    MONITOR_PRESET_OPTIONS,
    presetForSelection,
    resolvePresetSelection,
    type MonitorPresetSelectValue,
  } from "$lib/requests/monitor-presets";
  import { requestKindInfo } from "$lib/requests/request-helpers";
  import {
    deriveRequestReviewSelection,
    requestReviewTargetForExternalId,
  } from "$lib/requests/request-review-selection";
  import {
    proposalImageUrl,
    proposalTitle,
    selectedProposalImageUrl,
    tagRelationshipForTitle,
  } from "$lib/components/identify/identify-review-helpers";
  import type { RequestReviewResponse } from "$lib/api/generated/model";

  interface ReviewLoadInput {
    kind: RequestMediaKindCode;
    pluginId: string | null;
    namespace: string | null;
    value: string;
    hideNsfw: boolean;
  }

  const params = $derived(page.params as { kind: RequestMediaKindCode; id: string });
  const pluginQuery = $derived(page.url.searchParams.get("plugin"));
  const namespaceQuery = $derived(page.url.searchParams.get("namespace"));
  /** Query string of the originating search page, chained through so Back returns to live results. */
  const backQuery = $derived(page.url.searchParams.get("back"));
  const backHref = $derived(backQuery ? `/request?${backQuery}` : "/request");
  const nsfw = useNsfw();

  let review = $state.raw<RequestReviewResponse | null>(null);
  let selectedProposalIds = $state<string[]>([]);
  let targetLibraryRootId = $state<string | null>(null);
  let profileId = $state<string | null>(null);
  let chosenPreset = $state<MonitorPresetCode>(DEFAULT_MONITOR_PRESET);
  let selectionCustomized = $state(false);
  let loading = $state(true);
  let submitting = $state(false);
  let error = $state<string | null>(null);
  let enrichmentError = $state<string | null>(null);
  let reviewChanged = $state(false);
  let proposalPath = $state<string[]>([]);
  let selectedFieldsByProposal = $state<Record<string, Record<string, boolean>>>({});
  let selectedImagesByProposal = $state<Record<string, Record<string, string | null>>>({});
  let selectedTagsByProposal = $state<Record<string, Record<string, boolean>>>({});
  let selectedCascade = $state<Record<string, boolean>>({});

  const proposal = $derived(review?.proposal as EntityMetadataProposal | undefined);
  const activeProposal = $derived(proposal ? proposalAtPath(proposal, proposalPath) ?? proposal : undefined);
  const activeParent = $derived(proposal && proposalPath.length > 1
    ? proposalAtPath(proposal, proposalPath.slice(0, -1)) ?? proposal
    : proposal);
  const enrichmentRunning = $derived(review?.enrichment?.running === true);
  const pendingProposalIds = $derived(new Set(review?.enrichment?.pendingProposalIds ?? []));
  const activeSelectedFields = $derived(
    activeProposal ? selectedFieldsByProposal[activeProposal.proposalId] ?? {} : {},
  );
  const activeSelectedImages = $derived(
    activeProposal ? selectedImagesByProposal[activeProposal.proposalId] ?? {} : {},
  );
  const activeSelectedTags = $derived(
    activeProposal ? selectedTagsByProposal[activeProposal.proposalId] ?? {} : {},
  );
  const activeSelectableProposalIds = $derived.by(() => {
    if (!activeProposal || !proposal || !selection) return [];
    return activeProposal.proposalId === proposal.proposalId
      ? selection.selectableIds
      : structuralChildProposals(activeProposal).map((child) => child.proposalId);
  });
  const activeSelectedProposalIds = $derived.by(() => {
    if (!activeProposal || !proposal) return [];
    return activeProposal.proposalId === proposal.proposalId
      ? selectedProposalIds
      : activeSelectableProposalIds.filter((proposalId) => selectedCascade[proposalId] !== false);
  });
  const activeChildrenTitle = $derived.by(() => {
    const firstChild = activeProposal ? structuralChildProposals(activeProposal).at(0) : null;
    return firstChild ? entityKindLabel(firstChild.targetKind) : childrenTitle;
  });
  const activeTitle = $derived(activeProposal ? proposalTitle(activeProposal) : "Request");
  const activeImageShape = $derived.by(() => {
    if (!activeProposal) return "portrait" as const;
    const shape = aspectRatioForKind(activeProposal.targetKind);
    return shape === "square" ? "square" as const : shape === "wide" ? "wide" as const : "portrait" as const;
  });
  const activePosterUrl = $derived.by(() => {
    if (!activeProposal) return null;
    return selectedProposalImageUrl(
      activeProposal,
      ["poster", "thumbnail", "cover", "backdrop"],
      activeSelectedImages,
      activeProposal.proposalId,
      { getReviewImageSelections: (proposalId) => selectedImagesByProposal[proposalId] },
    ) ?? proposalImageUrl(activeProposal, ["poster", "cover", "thumbnail", "backdrop"]);
  });
  const selection = $derived(review ? deriveRequestReviewSelection(review) : null);
  const kindInfo = $derived(review ? requestKindInfo(review.kind) : null);
  const childNoun = $derived(kindInfo?.childNoun ?? "item");
  const childrenTitle = $derived(`${capitalize(childNoun)}s`);
  const selectsChildren = $derived(selection?.mode === REQUEST_REVIEW_SELECTION.directChildren);
  const requestableSelection = $derived(
    selectsChildren ? selectedProposalIds : (selection?.initialRootSelection ?? []),
  );
  const hasRequestIntent = $derived(
    requestableSelection.length > 0 || (selectsChildren && !selectionCustomized),
  );
  const presetDisplay = $derived<MonitorPresetSelectValue>(
    selectsChildren && selectionCustomized
      ? MONITOR_PRESET_CUSTOM
      : chosenPreset,
  );
  const presetOptions = $derived([
    ...MONITOR_PRESET_OPTIONS.map((option) => ({ value: option.value, label: option.label })),
    ...(presetDisplay === MONITOR_PRESET_CUSTOM
      ? [{ value: MONITOR_PRESET_CUSTOM, label: "Custom", disabled: true }]
      : []),
  ]);

  let loadedKey = $state("");
  $effect(() => {
    const input = currentReviewInput();
    const key = JSON.stringify(input);
    if (key === loadedKey) return;
    loadedKey = key;
    void initialize(key, input);
  });

  function currentReviewInput(): ReviewLoadInput {
    return {
      kind: params.kind,
      pluginId: pluginQuery,
      namespace: namespaceQuery,
      value: params.id,
      hideNsfw: nsfw.mode !== "show",
    };
  }

  async function initialize(key: string, input: ReviewLoadInput) {
    loading = true;
    review = null;
    error = null;
    enrichmentError = null;
    reviewChanged = false;
    proposalPath = [];
    selectedFieldsByProposal = {};
    selectedImagesByProposal = {};
    selectedTagsByProposal = {};
    selectedCascade = {};
    selectedProposalIds = [];
    chosenPreset = DEFAULT_MONITOR_PRESET;
    targetLibraryRootId = null;
    profileId = null;

    try {
      if (!input.pluginId?.trim() || !input.namespace?.trim()) {
        throw new Error("This review link is missing its plugin identity. Return to search and choose the result again.");
      }

      const response = await reviewRequest({
        kind: input.kind,
        pluginId: input.pluginId,
        externalIdentity: {
          namespace: input.namespace,
          value: input.value,
        },
        hideNsfw: input.hideNsfw,
      });
      if (key !== loadedKey) return;

      const nextSelection = deriveRequestReviewSelection(response);
      review = response;
      const rootProposal = response.proposal as EntityMetadataProposal;
      mergeMetadataSelection(rootProposal, null);
      proposalPath = [rootProposal.proposalId];
      selectionCustomized = false;
      const initialIds = nextSelection.mode === REQUEST_REVIEW_SELECTION.directChildren
        ? resolvePresetSelection(chosenPreset, nextSelection.presetChildren)
        : nextSelection.initialRootSelection;
      selectedProposalIds = initialIds;
      const picked = new Set(initialIds);
      selectedCascade = {
        ...selectedCascade,
        ...Object.fromEntries(nextSelection.selectableIds.map((proposalId) => [proposalId, picked.has(proposalId)])),
      };
      if (response.enrichment?.running) {
        void pollEnrichment(key, response.enrichment.reviewId);
      }
    } catch (err) {
      if (key !== loadedKey) return;
      error = err instanceof Error ? err.message : "Failed to load request review";
    } finally {
      if (key === loadedKey) loading = false;
    }
  }

  function applyPreset(value: string) {
    if (value === MONITOR_PRESET_CUSTOM || !selection) return;
    chosenPreset = value as MonitorPresetCode;
    selectionCustomized = false;
    setSelectedChildren(resolvePresetSelection(chosenPreset, selection.presetChildren));
  }

  function setSelectedChildren(ids: string[]) {
    selectedProposalIds = ids;
    if (!selection) return;
    const picked = new Set(ids);
    selectedCascade = {
      ...selectedCascade,
      ...Object.fromEntries(selection.selectableIds.map((proposalId) => [proposalId, picked.has(proposalId)])),
    };
  }

  function toggleProposal(proposalId: string, selected: boolean) {
    if (!selection?.selectableIds.includes(proposalId)) return;
    selectionCustomized = true;
    selectedProposalIds = selected
      ? Array.from(new Set([...selectedProposalIds, proposalId]))
      : selectedProposalIds.filter((id) => id !== proposalId);
    selectedCascade = { ...selectedCascade, [proposalId]: selected };
  }

  function mergeMetadataSelection(
    root: EntityMetadataProposal,
    previousRoot: EntityMetadataProposal | null,
  ) {
    const merged = mergeProgressiveReviewSelectionDefaults(previousRoot, root, {
      selectedFieldsByProposal,
      selectedImagesByProposal,
      selectedTagsByProposal,
      selectedCascade,
    });
    selectedFieldsByProposal = merged.selectedFieldsByProposal;
    selectedImagesByProposal = merged.selectedImagesByProposal;
    selectedTagsByProposal = merged.selectedTagsByProposal;
    selectedCascade = merged.selectedCascade;
  }

  async function pollEnrichment(key: string, reviewId: string) {
    while (key === loadedKey) {
      try {
        const response = await fetchRequestReview(reviewId);
        if (key !== loadedKey || response.enrichment?.reviewId !== reviewId) return;
        const previousRoot = proposal ?? null;
        review = response;
        mergeMetadataSelection(response.proposal as EntityMetadataProposal, previousRoot);
        enrichmentError = response.enrichment.error;
        if (!response.enrichment.running) return;
      } catch (err) {
        if (key === loadedKey) {
          enrichmentError = err instanceof Error ? err.message : "Failed to refresh request details";
        }
        await new Promise((resolveRetry) => setTimeout(resolveRetry, 1_500));
        continue;
      }
      await new Promise((resolvePoll) => setTimeout(resolvePoll, 750));
    }
  }

  function setMetadataField(field: string, selected: boolean) {
    if (!activeProposal) return;
    selectedFieldsByProposal = {
      ...selectedFieldsByProposal,
      [activeProposal.proposalId]: { ...activeSelectedFields, [field]: selected },
    };
  }

  function setAllMetadataFields(selected: boolean) {
    if (!activeProposal) return;
    selectedFieldsByProposal = {
      ...selectedFieldsByProposal,
      [activeProposal.proposalId]: {
        ...activeSelectedFields,
        ...Object.fromEntries(
          reviewDiffFieldKeys.map((field) => [field, selected && proposalHasField(activeProposal, field)]),
        ),
      },
    };
  }

  function setMetadataImage(kind: string, url: string | null) {
    if (!activeProposal) return;
    selectedImagesByProposal = {
      ...selectedImagesByProposal,
      [activeProposal.proposalId]: { ...activeSelectedImages, [kind]: url },
    };
  }

  function setMetadataTag(tag: string, selected: boolean) {
    if (!activeProposal) return;
    selectedTagsByProposal = {
      ...selectedTagsByProposal,
      [activeProposal.proposalId]: { ...activeSelectedTags, [tag]: selected },
    };
    const relationship = tagRelationshipForTitle(tag, relationshipProposals(activeProposal));
    if (relationship) {
      selectedCascade = { ...selectedCascade, [relationship.proposalId]: selected };
    }
  }

  function setMetadataProposal(result: EntityMetadataProposal, selected: boolean) {
    selectedCascade = { ...selectedCascade, [result.proposalId]: selected };
    if (result.targetKind === ENTITY_KIND.tag) setMetadataTag(proposalTitle(result), selected);
  }

  function openProposal(nextProposal: EntityMetadataProposal) {
    proposalPath = [...proposalPath, nextProposal.proposalId];
  }

  function setActiveProposalSelected(proposalId: string, selected: boolean) {
    if (!activeProposal || !proposal) return;
    if (activeProposal.proposalId === proposal.proposalId) {
      toggleProposal(proposalId, selected);
      return;
    }
    if (!activeSelectableProposalIds.includes(proposalId)) return;
    selectedCascade = { ...selectedCascade, [proposalId]: selected };
  }

  function closeProposal() {
    if (proposalPath.length > 1) {
      proposalPath = proposalPath.slice(0, -1);
    }
  }

  async function requestSelection() {
    if (enrichmentRunning || !review || !proposal || !selection || !kindInfo?.committable) return;
    const selectedIds = selection.mode === REQUEST_REVIEW_SELECTION.directChildren
      ? selectedProposalIds.filter((id) => selection.selectableIds.includes(id))
      : selection.initialRootSelection;
    if (selectedIds.length === 0 && (!selectsChildren || selectionCustomized)) {
      error = selection.mode === REQUEST_REVIEW_SELECTION.directChildren
        ? `Select at least one ${childNoun} to request.`
        : "This proposal is not requestable.";
      return;
    }

    submitting = true;
    error = null;
    reviewChanged = false;
    try {
      const rootSelectedFields = selectedFieldsByProposal[proposal.proposalId]
        ?? defaultFieldSelectionForReview(proposal);
      const rootSelectedImages = selectedImagesByProposal[proposal.proposalId]
        ?? defaultImageSelectionForReview(proposal);
      const reviewedPayload = buildRootReviewApplyPayload(proposal, {
        selectedFields: rootSelectedFields,
        selectedImages: rootSelectedImages,
        selectedTags: selectedTagsByProposal[proposal.proposalId] ?? {},
        selectedCascade,
        selectedFieldsByProposal,
        selectedImagesByProposal,
        selectedTagsByProposal,
      });
      const response = await commitReviewedRequest(
        {
          kind: review.kind,
          pluginId: review.pluginId,
          rootExternalIdentity: review.externalIdentity,
          proposalRevision: review.revision,
          selectedProposalIds: selectedIds,
          targetLibraryRootId,
          profileId,
          review,
          proposal: reviewedPayload.proposal as RequestReviewResponse["proposal"],
          selectedFields: reviewedPayload.selectedFields,
          selectedImages: reviewedPayload.selectedImages,
          ...(selection.mode === REQUEST_REVIEW_SELECTION.directChildren ? { preset: chosenPreset } : {}),
        },
        nsfw.mode !== "show",
      );

      const requested = response.items.filter((item) => item.outcome === REQUEST_COMMIT_OUTCOME.requested);
      if (response.containerEntityId) {
        await goto(resolve((resolveEntityHref(review.entityKind, response.containerEntityId) ?? "/request") as "/"));
        return;
      }
      if (requested.length === 0) {
        const alreadyOwned = response.items.filter(
          (item) => item.outcome === REQUEST_COMMIT_OUTCOME.alreadyOwned,
        ).length;
        error = response.items.length > 0 && alreadyOwned === response.items.length
          ? "Already in your library — nothing to request."
          : "Already requested — the existing requests are still searching.";
        return;
      }

      const single = requested.length === 1 ? requested[0] : null;
      const target = single ? requestReviewTargetForExternalId(review, single.externalId) : null;
      const singleHref = single?.entityId
        ? resolveEntityHref(target?.entityKind ?? review.entityKind, single.entityId)
        : null;
      await goto(resolve((singleHref ?? "/request") as "/"));
    } catch (err) {
      if (err instanceof ApiError && err.problemCode === PROBLEM_CODE.requestProposalChanged) {
        reviewChanged = true;
        error = "This proposal changed after you reviewed it. Reload the review and confirm your selection again.";
      } else {
        error = err instanceof Error ? err.message : "Request failed";
      }
    } finally {
      submitting = false;
    }
  }

  function reloadReview() {
    const input = currentReviewInput();
    const key = JSON.stringify(input);
    loadedKey = key;
    void initialize(key, input);
  }

  function capitalize(value: string): string {
    return value ? `${value.charAt(0).toUpperCase()}${value.slice(1)}` : value;
  }

  function proposalAtPath(
    root: EntityMetadataProposal,
    path: string[],
  ): EntityMetadataProposal | null {
    if (path.length === 0 || path[0] !== root.proposalId) return null;
    let current = root;
    for (const proposalId of path.slice(1)) {
      const next = [...structuralChildProposals(current), ...relationshipProposals(current)]
        .find((candidate) => candidate.proposalId === proposalId);
      if (!next) return null;
      current = next;
    }
    return current;
  }

  function identifyingStatus(node: EntityMetadataProposal): string | null {
    return pendingProposalIds.has(node.proposalId) ? "Identifying…" : null;
  }
</script>

<svelte:head><title>{activeProposal ? proposalTitle(activeProposal) : "Request"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <a
    href={resolve(backHref as "/")}
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-4 w-4" />
    Back to search
  </a>

  {#if loading}
    <div class="surface-panel flex min-h-48 items-center justify-center p-6 text-text-muted" aria-label="Loading request review">
      <Loader2 class="h-5 w-5 animate-spin" />
    </div>
  {:else if error && !review}
    <div class="surface-panel p-6 text-[0.82rem] leading-relaxed text-error-text">{error}</div>
  {:else if review && proposal && selection}
    {#if proposalPath.length > 1 && activeParent}
      <Button
        type="button"
        variant="secondary"
        size="sm"
        class="gap-1.5"
        aria-label={`Back to ${proposalTitle(activeParent)}`}
        onclick={closeProposal}
      >
        <ChevronLeft class="h-3.5 w-3.5" />
        {proposalTitle(activeParent)}
      </Button>
    {/if}

    {@render requestOptions()}

    <MetadataProposalReview
      proposal={activeProposal ?? proposal}
      title={activeTitle}
      subtitle={`${review.externalIdentity.namespace}:${review.externalIdentity.value}`}
      kindLabel={(activeProposal ?? proposal).targetKind}
      posterUrl={activePosterUrl}
      imageShape={activeImageShape}
      selectedFields={activeSelectedFields}
      selectedImages={activeSelectedImages}
      selectedTags={activeSelectedTags}
      currentValue={() => ""}
      onFieldChange={setMetadataField}
      onAllFields={setAllMetadataFields}
      onImageChange={setMetadataImage}
      onTagChange={setMetadataTag}
      onProposalSelected={setMetadataProposal}
      isProposalSelected={(proposalId) => selectedCascade[proposalId] !== false}
      imageSelectionsForProposal={(proposalId) => selectedImagesByProposal[proposalId]}
      onActivate={openProposal}
      statusLabel={identifyingStatus}
    />

    <ProposalReviewSummary
      proposal={activeProposal ?? proposal}
      selectedIds={activeSelectedProposalIds}
      selectableIds={activeSelectableProposalIds}
      onSelectedChange={setActiveProposalSelected}
      onActivate={openProposal}
      childrenTitle={activeChildrenTitle}
      subtitle={`${review.externalIdentity.namespace}:${review.externalIdentity.value}`}
      showOverview={false}
      showRelationships={false}
      statusLabel={identifyingStatus}
    />

    {@render requestOptions()}

    {#snippet requestOptions()}
    <section class="space-y-3 rounded-sm border border-border-accent bg-surface-1 p-4" aria-label="Request options">
      <div>
        <h3 class="flex items-center gap-1.5 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">
          <Send class="h-3.5 w-3.5 text-text-accent" />
          {selectsChildren ? `Request ${childNoun}s` : `Request this ${kindInfo?.label.toLowerCase() ?? "item"}`}
        </h3>
        {#if selectsChildren}
          <p class="mt-1 text-[0.78rem] leading-relaxed text-text-muted">
            Select the {childNoun}s above. Prismedia will create and monitor each chosen item through
            the same reviewed plugin proposal.
          </p>
        {/if}
      </div>

      {#if selectsChildren}
        <label class="flex max-w-64 flex-col gap-1">
          <span class="font-mono text-[0.66rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">Monitor</span>
          <Select
            options={presetOptions}
            value={presetDisplay}
            size="sm"
            onchange={applyPreset}
          />
        </label>
      {/if}

      {#if kindInfo}
        <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId>
          {#snippet actions()}
            <Button
              type="button"
              variant="primary"
              class="shrink-0 gap-2"
              disabled={submitting || enrichmentRunning || !hasRequestIntent}
              title={enrichmentRunning
                ? "Identifying children and relationships"
                : !hasRequestIntent
                ? selectsChildren
                  ? `Select ${childNoun}s to request`
                  : "This proposal is not requestable"
                : undefined}
              onclick={() => void requestSelection()}
            >
              {#if submitting}
                <Loader2 class="h-4 w-4 animate-spin" />
              {:else}
                <Send class="h-4 w-4" />
              {/if}
              {submitting
                ? "Requesting…"
                : selectsChildren
                  ? selectedProposalIds.length === 0
                    ? "Request"
                    : `Request ${selectedProposalIds.length} ${childNoun}${selectedProposalIds.length === 1 ? "" : "s"}`
                  : "Request"}
            </Button>
          {/snippet}
        </RequestTargetOptions>
      {/if}

      {#if enrichmentRunning}
        <p class="flex items-center gap-1.5 font-mono text-[0.7rem] text-text-muted" aria-live="polite">
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
          Identifying children and relationships… Request unlocks when finished.
        </p>
      {/if}

      {#if error || enrichmentError}
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-xs border border-error/30 bg-error/5 p-3">
          <p class="text-[0.75rem] leading-relaxed text-error-text">{error ?? enrichmentError}</p>
          {#if reviewChanged}
            <Button type="button" variant="secondary" size="sm" class="gap-1.5" onclick={reloadReview}>
              <RefreshCw class="h-3.5 w-3.5" />
              Reload review
            </Button>
          {/if}
        </div>
      {/if}
    </section>
    {/snippet}
  {/if}
</div>
