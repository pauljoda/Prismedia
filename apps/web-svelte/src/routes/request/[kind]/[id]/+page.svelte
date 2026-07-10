<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { ChevronLeft, Loader2, RefreshCw, Send } from "@lucide/svelte";
  import { Button, Select } from "@prismedia/ui-svelte";
  import { REQUEST_COMMIT_OUTCOME, REQUEST_REVIEW_SELECTION } from "$lib/api/generated/codes";
  import type {
    MonitorPresetCode,
    RequestMediaKindCode,
  } from "$lib/api/generated/codes";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { ApiError } from "$lib/api/orval-fetch";
  import { commitReviewedRequest, reviewRequest } from "$lib/api/requests";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import ProposalReviewSummary from "$lib/components/review/ProposalReviewSummary.svelte";
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
  import { proposalTitle } from "$lib/components/identify/identify-review-helpers";
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
  let reviewChanged = $state(false);

  const proposal = $derived(review?.proposal as EntityMetadataProposal | undefined);
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
    reviewChanged = false;
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
      selectionCustomized = false;
      selectedProposalIds = nextSelection.mode === REQUEST_REVIEW_SELECTION.directChildren
        ? resolvePresetSelection(chosenPreset, nextSelection.presetChildren)
        : nextSelection.initialRootSelection;
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
    selectedProposalIds = resolvePresetSelection(chosenPreset, selection.presetChildren);
  }

  function toggleProposal(proposalId: string, selected: boolean) {
    if (!selection?.selectableIds.includes(proposalId)) return;
    selectionCustomized = true;
    selectedProposalIds = selected
      ? Array.from(new Set([...selectedProposalIds, proposalId]))
      : selectedProposalIds.filter((id) => id !== proposalId);
  }

  async function requestSelection() {
    if (!review || !selection || !kindInfo?.committable) return;
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
      const response = await commitReviewedRequest(
        {
          kind: review.kind,
          pluginId: review.pluginId,
          rootExternalIdentity: review.externalIdentity,
          proposalRevision: review.revision,
          selectedProposalIds: selectedIds,
          targetLibraryRootId,
          profileId,
          ...(selection.mode === REQUEST_REVIEW_SELECTION.directChildren ? { preset: chosenPreset } : {}),
        },
        nsfw.mode !== "show",
      );

      const requested = response.items.filter((item) => item.outcome === REQUEST_COMMIT_OUTCOME.requested);
      if (requested.length === 0) {
        if (response.containerEntityId) {
          await goto(resolveEntityHref(review.entityKind, response.containerEntityId) ?? "/request");
          return;
        }
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
      await goto(singleHref ?? "/request");
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
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
</script>

<svelte:head><title>{proposal ? proposalTitle(proposal) : "Request"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <a
    href={backHref}
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
    <ProposalReviewSummary
      {proposal}
      selectedIds={selectedProposalIds}
      selectableIds={selection.selectableIds}
      onSelectedChange={toggleProposal}
      {childrenTitle}
      subtitle={`${review.externalIdentity.namespace}:${review.externalIdentity.value}`}
    />

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
              disabled={submitting || !hasRequestIntent}
              title={!hasRequestIntent
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

      {#if error}
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-xs border border-error/30 bg-error/5 p-3">
          <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
          {#if reviewChanged}
            <Button type="button" variant="secondary" size="sm" class="gap-1.5" onclick={reloadReview}>
              <RefreshCw class="h-3.5 w-3.5" />
              Reload review
            </Button>
          {/if}
        </div>
      {/if}
    </section>
  {/if}
</div>
