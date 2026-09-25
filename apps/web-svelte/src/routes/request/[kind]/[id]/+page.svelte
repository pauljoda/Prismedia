<script lang="ts">
  import { dev } from "$app/environment";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { ChevronLeft, Clock3, ExternalLink, Loader2, RefreshCw, Send } from "@lucide/svelte";
  import { Button, Checkbox, Select } from "@prismedia/ui-svelte";
  import { BOOK_RENDITION, ENTITY_KIND, EXTERNAL_ID_PROVIDER, PROBLEM_CODE, REQUEST_COMMIT_OUTCOME, REQUEST_MEDIA_KIND, REQUEST_REVIEW_SELECTION } from "$lib/api/generated/codes";
  import type {
    BookRenditionCode,
    MonitorPresetCode,
    RequestMediaKindCode,
  } from "$lib/api/generated/codes";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { ApiError } from "$lib/api/orval-fetch";
  import { commitReviewedRequest, fetchRequestReview, reviewRequest } from "$lib/api/requests";
  import { saveReviewedManagedRequest, type ManagedRequestChoice } from "$lib/api/reviewed-managed-requests";
  import { ManagedRequestRejectedError } from "$lib/api/managed-requests";
  import ManagerRequestOptions from "$lib/components/integrations/ManagerRequestOptions.svelte";
  import BookManagerRequest from "$lib/components/books/BookManagerRequest.svelte";
  import type { ManagedRequestOwnership } from "$lib/components/integrations/ManagerRequestOptions.svelte";
  import { reviewManagerTitle } from "$lib/api/managed-discovery";
  import { fetchConnections } from "$lib/api/connections";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import {
    buildRootReviewApplyPayload,
    defaultFieldSelectionForReview,
    defaultImageSelectionForReview,
    entityKindLabel,
    mergeProgressiveReviewSelectionDefaults,
    proposalHasField,
    relationshipProposals,
    reviewBaseFieldKeys,
    structuralChildProposals,
  } from "$lib/components/identify-review";
  import MetadataProposalReview from "$lib/components/review/MetadataProposalReview.svelte";
  import ProposalReviewLayout from "$lib/components/review/ProposalReviewLayout.svelte";
  import ProposalReviewSummary from "$lib/components/review/ProposalReviewSummary.svelte";
  import { aspectRatioForKind } from "$lib/entities/entity-thumbnail";
  import { displayNameForEntityKind, resolveEntityHref } from "$lib/entities/entity-codes";
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
  import type { CommitReviewedManagedRequestInput, ConnectionResponse, RequestReviewResponse, ReviewedRequestCommitRequest } from "$lib/api/generated/model";
  import { useSession } from "$lib/stores/session.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  import { createUuid } from "$lib/utils/uuid";
  import { supportsBookManager } from "$lib/requests/book-manager-connection";
  interface ReviewLoadInput {
    kind: RequestMediaKindCode;
    pluginId: string | null;
    connectionId: string | null;
    namespace: string | null;
    value: string;
    hideNsfw: boolean;
  }

  const params = $derived(page.params as { kind: RequestMediaKindCode; id: string });
  const pluginQuery = $derived(page.url.searchParams.get("plugin"));
  const connectionQuery = $derived(page.url.searchParams.get("connection"));
  const namespaceQuery = $derived(page.url.searchParams.get("namespace"));
  /** Query string of the originating search page, chained through so Back returns to live results. */
  const backQuery = $derived(page.url.searchParams.get("back"));
  /** Concept (dev only): `?layout=preview` renders the preview-and-decide review layout with the same controls. */
  const previewLayout = $derived(dev && page.url.searchParams.get("layout") === "preview");
  const backHref = $derived(backQuery ? `/request?${backQuery}` : "/request");
  const nsfw = useNsfw();
  const session = useSession();
  const appChrome = useAppChrome();

  let review = $state.raw<RequestReviewResponse | null>(null);
  let selectedProposalIds = $state<string[]>([]);
  let targetLibraryRootId = $state<string | null>(null);
  let profileId = $state<string | null>(null);
  let selectedBookRenditions = $state<BookRenditionCode[]>([]);
  let ebookTargetLibraryRootId = $state<string | null>(null);
  let ebookProfileId = $state<string | null>(null);
  let audiobookTargetLibraryRootId = $state<string | null>(null);
  let audiobookProfileId = $state<string | null>(null);
  let bookResultHref = $state<string | null>(null);
  let bookManagerAvailable = $state(false);
  let bookManagerSelected = $state(false);
  let chosenPreset = $state<MonitorPresetCode>(DEFAULT_MONITOR_PRESET);
  let selectionCustomized = $state(false);
  let loading = $state(true);
  let submitting = $state(false);
  let managerSelected = $state(false);
  let managerRefreshToken = $state(0);
  let managerChoice = $state<ManagedRequestChoice | null>(null);
  let managerOwnership = $state<ManagedRequestOwnership | null>(null);
  let pendingManagerCommit = $state<{ connectionId: string; input: CommitReviewedManagedRequestInput } | null>(null);
  let managerConnection = $state<ConnectionResponse | null>(null);
  let managerRevision = $state<number | string | null>(null);
  let error = $state<string | null>(null);
  let enrichmentError = $state<string | null>(null);
  let reviewChanged = $state(false);
  let ownershipConflictRefresh = $state(false);
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
  const ebookKindInfo = requestKindInfo(REQUEST_MEDIA_KIND.book);
  const audiobookKindInfo = requestKindInfo(REQUEST_MEDIA_KIND.audiobook);
  const canChooseBookRenditions = $derived(
    review?.entityKind === ENTITY_KIND.book && selection?.mode === REQUEST_REVIEW_SELECTION.root,
  );
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
  const managedSeriesSelected = $derived(
    managerSelected && review?.entityKind === ENTITY_KIND.videoSeries,
  );
  const managerExpansionCount = $derived(Number(managerChoice?.review.expansion?.newTargetCount ?? 0));
  const managedSeriesTargetCount = $derived(
    managerChoice?.review.expansion
      ? Number(managerChoice.review.expansion.newTargetCount)
      : (managerChoice?.review.work.targets?.length ?? 0),
  );
  const presetOptions = $derived([
    ...MONITOR_PRESET_OPTIONS.map((option) => ({ value: option.value, label: option.label })),
    ...(presetDisplay === MONITOR_PRESET_CUSTOM
      ? [{ value: MONITOR_PRESET_CUSTOM, label: "Custom", disabled: true }]
      : []),
  ]);

  $effect(() => appChrome.setBreadcrumbs([
    { label: "Request", href: "/request" },
    ...(managerConnection ? [{ label: managerConnection.name, href: backHref }] : [{ label: "Search", href: backHref }]),
    { label: activeTitle },
  ]));

  let loadedKey = $state("");
  $effect(() => {
    if (!session.canRequestContent) {
      loadedKey = "";
      loading = false;
      return;
    }
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
      connectionId: connectionQuery,
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
    ownershipConflictRefresh = false;
    proposalPath = [];
    selectedFieldsByProposal = {};
    selectedImagesByProposal = {};
    selectedTagsByProposal = {};
    selectedCascade = {};
    selectedProposalIds = [];
    chosenPreset = DEFAULT_MONITOR_PRESET;
    targetLibraryRootId = null;
    profileId = null;
    selectedBookRenditions = [];
    ebookTargetLibraryRootId = null;
    ebookProfileId = null;
    audiobookTargetLibraryRootId = null;
    audiobookProfileId = null;
    bookResultHref = null;
    bookManagerAvailable = false;
    bookManagerSelected = false;
    managerSelected = Boolean(input.connectionId);
    managerChoice = null;
    managerOwnership = null;
    pendingManagerCommit = null;
    managerConnection = null;
    managerRevision = null;

    try {
      if ((!input.pluginId?.trim() && !input.connectionId?.trim()) || !input.namespace?.trim()) {
        throw new Error("This review link is missing its plugin identity. Return to search and choose the result again.");
      }

      const externalIdentity = { namespace: input.namespace, value: input.value };
      let response: RequestReviewResponse;
      if (input.connectionId) {
        if (!session.isAdmin) throw new Error("An administrator must review requests through connected applications.");
        const entityKind = requestKindInfo(input.kind)?.entityKind;
        if (!entityKind) throw new Error("This media type cannot be requested through this source.");
        const [result, connections] = await Promise.all([
          reviewManagerTitle(input.connectionId, { entityKind, externalIdentity }),
          fetchConnections(),
        ]);
        if (key !== loadedKey) return;
        const connection = connections.find(item => item.id === input.connectionId);
        if (!connection) throw new Error("This connection is no longer available. Return to Request and choose a source.");
        managerConnection = connection;
        managerRevision = result.connectionRevision;
        response = result.review;
      } else {
        response = await reviewRequest({ kind: input.kind, pluginId: input.pluginId!, externalIdentity, hideNsfw: input.hideNsfw });
      }
      if (key !== loadedKey) return;

      const nextSelection = deriveRequestReviewSelection(response);
      review = response;
      if (response.entityKind === ENTITY_KIND.book) {
        selectedBookRenditions = [response.kind === REQUEST_MEDIA_KIND.audiobook
          ? BOOK_RENDITION.audiobook : BOOK_RENDITION.ebook];
        if (session.isAdmin && response.proposal.patch?.externalIds?.[EXTERNAL_ID_PROVIDER.openLibraryWork]) {
          void fetchConnections().then(connections => {
            if (key !== loadedKey) return;
            bookManagerAvailable = connections.some(supportsBookManager);
          }).catch(() => { if (key === loadedKey) bookManagerAvailable = false; });
        }
      }
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
    if (submitting || pendingManagerCommit) return;
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
    if (submitting || pendingManagerCommit) return;
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
    if (submitting || pendingManagerCommit) return;
    if (!activeProposal) return;
    selectedFieldsByProposal = {
      ...selectedFieldsByProposal,
      [activeProposal.proposalId]: { ...activeSelectedFields, [field]: selected },
    };
  }

  function setAllMetadataFields(selected: boolean) {
    if (submitting || pendingManagerCommit) return;
    if (!activeProposal) return;
    selectedFieldsByProposal = {
      ...selectedFieldsByProposal,
      [activeProposal.proposalId]: {
        ...activeSelectedFields,
        ...Object.fromEntries(
          reviewBaseFieldKeys(activeProposal).map((field) => [field, selected && proposalHasField(activeProposal, field)]),
        ),
      },
    };
  }

  function setMetadataImage(kind: string, url: string | null) {
    if (submitting || pendingManagerCommit) return;
    if (!activeProposal) return;
    selectedImagesByProposal = {
      ...selectedImagesByProposal,
      [activeProposal.proposalId]: { ...activeSelectedImages, [kind]: url },
    };
  }

  function setMetadataTag(tag: string, selected: boolean) {
    if (submitting || pendingManagerCommit) return;
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
    if (submitting || pendingManagerCommit) return;
    selectedCascade = { ...selectedCascade, [result.proposalId]: selected };
    if (result.targetKind === ENTITY_KIND.tag) setMetadataTag(proposalTitle(result), selected);
  }

  function openProposal(nextProposal: EntityMetadataProposal) {
    proposalPath = [...proposalPath, nextProposal.proposalId];
  }

  function setActiveProposalSelected(proposalId: string, selected: boolean) {
    if (submitting || pendingManagerCommit) return;
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

  function reviewedCommitPayload(): ReviewedRequestCommitRequest {
    if (!review || !proposal || !selection) throw new Error("Load the review before continuing");
    const selectedIds = selection.mode === REQUEST_REVIEW_SELECTION.directChildren
      ? selectedProposalIds.filter(id => selection.selectableIds.includes(id)) : selection.initialRootSelection;
    const rootSelectedFields = selectedFieldsByProposal[proposal.proposalId] ?? defaultFieldSelectionForReview(proposal);
    const rootSelectedImages = selectedImagesByProposal[proposal.proposalId] ?? defaultImageSelectionForReview(proposal);
    const reviewedPayload = buildRootReviewApplyPayload(proposal, {
      selectedFields: rootSelectedFields, selectedImages: rootSelectedImages,
      selectedTags: selectedTagsByProposal[proposal.proposalId] ?? {}, selectedCascade,
      selectedFieldsByProposal, selectedImagesByProposal, selectedTagsByProposal,
    });
    return {
      kind: review.kind, pluginId: review.pluginId, rootExternalIdentity: review.externalIdentity,
      proposalRevision: review.revision, selectedProposalIds: selectedIds, targetLibraryRootId, profileId, review,
      proposal: reviewedPayload.proposal as RequestReviewResponse["proposal"],
      selectedFields: reviewedPayload.selectedFields, selectedImages: reviewedPayload.selectedImages,
      ...(canChooseBookRenditions ? { bookRenditions: selectedBookRenditions.map((rendition) => ({
        rendition,
        targetLibraryRootId: rendition === BOOK_RENDITION.ebook ? ebookTargetLibraryRootId : audiobookTargetLibraryRootId,
        profileId: rendition === BOOK_RENDITION.ebook ? ebookProfileId : audiobookProfileId,
      })) } : {}),
      ...(selection.mode === REQUEST_REVIEW_SELECTION.directChildren ? { preset: chosenPreset } : {}),
    };
  }

  function setBookRendition(rendition: BookRenditionCode, selected: boolean) {
    if (!selected && selectedBookRenditions.length === 1) return;
    selectedBookRenditions = selected
      ? [BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook].filter(
          (candidate) => candidate === rendition || selectedBookRenditions.includes(candidate),
        )
      : selectedBookRenditions.filter((candidate) => candidate !== rendition);
  }

  const managerReviewPayload = $derived.by(() => {
    if (!review || !proposal || !selection || enrichmentRunning || !hasRequestIntent) return null;
    try {
      const payload = reviewedCommitPayload();
      return review.entityKind === ENTITY_KIND.videoSeries ? withFiniteEpisodeSelection(payload) : payload;
    } catch { return null; }
  });

  function withFiniteEpisodeSelection(payload: ReviewedRequestCommitRequest): ReviewedRequestCommitRequest {
    const episodeIds: string[] = [];
    const visit = (node: EntityMetadataProposal) => {
      if (node.targetKind === ENTITY_KIND.videoEpisode) episodeIds.push(node.proposalId);
      for (const child of node.children ?? []) visit(child);
    };
    visit(payload.proposal as EntityMetadataProposal);
    if (episodeIds.length === 0) throw new Error("Select at least one episode before choosing external fulfillment");
    return { ...payload, selectedProposalIds: episodeIds };
  }

  async function requestSelection() {
    if (submitting || enrichmentRunning || !review || !proposal || !selection || !kindInfo?.committable) return;
    if (managerSelected && !managerChoice && !pendingManagerCommit && !managerOwnership) return;
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
      if (managerSelected || pendingManagerCommit) {
        if (!pendingManagerCommit && managerChoice) {
          pendingManagerCommit = {
            connectionId: managerChoice.connectionId,
            input: {
              operationId: createUuid(),
              expectedConnectionRevision: managerChoice.review.connectionRevision,
              libraryRootId: managerChoice.review.mount.libraryRootId,
              profileId: managerChoice.profileId,
              monitored: managerChoice.monitored,
              search: managerChoice.search,
              request: managerChoice.review.request,
              managerDiscoveryRevision: managerChoice.review.managerDiscoveryRevision,
            },
          };
        }
        if (pendingManagerCommit) {
          const result = await saveReviewedManagedRequest(pendingManagerCommit.connectionId, pendingManagerCommit.input);
          await goto(resolve((resolveEntityHref(review.entityKind, result.entityId) ?? "/request") as "/"));
          return;
        }
        if (managerOwnership) {
          await goto(resolve((resolveEntityHref(review.entityKind, managerOwnership.entityId) ?? "/request") as "/"));
          return;
        }
        return;
      }
      const response = await commitReviewedRequest(reviewedCommitPayload(), nsfw.mode !== "show");

      if (canChooseBookRenditions && !response.bookRenditions) {
        const bookId = response.items.find((item) => item.entityId)?.entityId;
        bookResultHref = bookId ? resolveEntityHref(ENTITY_KIND.book, bookId) ?? null : null;
        error = "The server did not confirm both format outcomes. One format may have started; review the Book before retrying.";
        return;
      }

      if (canChooseBookRenditions && response.bookRenditions) {
        const bookId = response.bookRenditions.find((result) => result.item?.entityId)?.item?.entityId;
        bookResultHref = bookId ? resolveEntityHref(ENTITY_KIND.book, bookId) ?? null : null;
        const failures = response.bookRenditions.filter((result) => result.error);
        if (failures.length > 0) {
          error = failures.map((result) =>
            `${result.rendition === BOOK_RENDITION.audiobook ? "Audiobook" : "Ebook"}: ${result.error}`,
          ).join(" ");
          return;
        }
        if (bookResultHref) {
          await goto(resolve(bookResultHref as "/"));
          return;
        }
      }

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
      if (err instanceof ManagedRequestRejectedError) {
        pendingManagerCommit = null;
        managerChoice = null;
        if (err.problemCode === PROBLEM_CODE.fulfillmentOwnershipConflict) {
          ownershipConflictRefresh = true;
        }
        managerRefreshToken++;
      }
      if ((err instanceof ApiError || err instanceof ManagedRequestRejectedError) && err.problemCode === PROBLEM_CODE.requestProposalChanged) {
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

{#if !session.canRequestContent}
  <StatePlaceholder
    icon={Send}
    title="Request access required"
    description="Ask an administrator to allow content requests for your account."
  />
{:else}
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

    {#snippet reviewStructure()}
    <ProposalReviewSummary
      proposal={activeProposal ?? proposal}
      selectedIds={activeSelectedProposalIds}
      selectableIds={activeSelectableProposalIds}
      onSelectedChange={setActiveProposalSelected}
      onActivate={openProposal}
      childrenTitle={activeChildrenTitle}
      subtitle={review ? `${review.externalIdentity.namespace}:${review.externalIdentity.value}` : null}
      showOverview={false}
      showRelationships={false}
      statusLabel={identifyingStatus}
    />
    {/snippet}

    {#if previewLayout}
    <ProposalReviewLayout
      proposal={activeProposal ?? proposal}
      title={activeTitle}
      posterUrl={activePosterUrl}
      imageShape={activeImageShape}
      selectedFields={activeSelectedFields}
      selectedImages={activeSelectedImages}
      selectedTags={activeSelectedTags}
      onFieldChange={setMetadataField}
      onAllFields={setAllMetadataFields}
      onImageChange={setMetadataImage}
      onTagChange={setMetadataTag}
      onProposalSelected={setMetadataProposal}
      isProposalSelected={(proposalId) => selectedCascade[proposalId] !== false}
      imageSelectionsForProposal={(proposalId) => selectedImagesByProposal[proposalId]}
      onActivate={openProposal}
      statusLabel={identifyingStatus}
      structure={reviewStructure}
      sidebar={requestOptions}
      disabled={submitting || !!pendingManagerCommit}
    />
    {:else}
    <div class="min-w-0 space-y-4">
    <MetadataProposalReview
      proposal={activeProposal ?? proposal}
      title={activeTitle}
      subtitle={review ? `${review.externalIdentity.namespace}:${review.externalIdentity.value}` : null}
      kindLabel={displayNameForEntityKind((activeProposal ?? proposal).targetKind)}
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
      sidebar={requestOptions}
      disabled={submitting || !!pendingManagerCommit}
      structure={reviewStructure}
    />
    </div>
    {/if}

    {#snippet requestOptions()}
    <section class="space-y-3 rounded-sm border border-border-accent bg-surface-1 p-4">
      <div>
        <h3 class="flex items-center gap-1.5 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">
          {#if managerOwnership}<Clock3 class="h-3.5 w-3.5 text-text-muted" />{:else}<Send class="h-3.5 w-3.5 text-text-accent" />{/if}
          {managerOwnership
            ? "Request status"
            : managerExpansionCount > 0
            ? `Request ${managerExpansionCount} more episode${managerExpansionCount === 1 ? "" : "s"}`
            : managedSeriesSelected
            ? "Request selected episodes"
            : selectsChildren
              ? `Request ${childNoun}s`
              : `Request this ${kindInfo?.label.toLowerCase() ?? "item"}`}
        </h3>
        {#if selectsChildren}
          <p class="mt-1 text-[0.78rem] leading-relaxed text-text-muted">
            {#if managedSeriesSelected}
              Choose seasons and episodes in the metadata review. Only the present selected episodes are sent to the manager; future episodes are not included.
            {:else}
              Select the {childNoun}s above. Prismedia will create and monitor each chosen item through
              the same reviewed plugin proposal.
            {/if}
          </p>
        {/if}
      </div>

      {#if selectsChildren && !managedSeriesSelected}
        <label class="flex max-w-64 flex-col gap-1">
          <span class="font-mono text-[0.66rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">Monitor</span>
          <Select
            options={presetOptions}
            value={presetDisplay}
            size="sm"
            onchange={applyPreset}
            disabled={submitting || !!pendingManagerCommit}
          />
        </label>
      {/if}

      {#if session.isAdmin && review
        && (review.entityKind === ENTITY_KIND.movie && review.externalIdentity.namespace === EXTERNAL_ID_PROVIDER.tmdb
          || review.entityKind === ENTITY_KIND.videoSeries
            && (review.externalIdentity.namespace === EXTERNAL_ID_PROVIDER.tmdb || review.externalIdentity.namespace === EXTERNAL_ID_PROVIDER.tvdb))}
        <ManagerRequestOptions entityKind={review.entityKind} fixedConnection={managerConnection}
          request={reviewChanged ? null : managerReviewPayload} managerDiscoveryRevision={managerRevision} refreshToken={managerRefreshToken}
          disabled={submitting || !!pendingManagerCommit || enrichmentRunning || !hasRequestIntent}
          onChange={(choice, active, ownership) => {
            managerChoice = choice;
            managerSelected = active;
            managerOwnership = ownership;
            if (ownership && ownershipConflictRefresh) {
              error = null;
              ownershipConflictRefresh = false;
            }
          }} />
      {/if}

      {#if canChooseBookRenditions && bookManagerAvailable && !managerSelected}
        <label class="flex items-center gap-2 text-sm text-text-secondary">
          <Checkbox checked={bookManagerSelected} disabled={submitting}
            onchange={checked => bookManagerSelected = checked} />
          Use a connected Book manager
        </label>
        {#if bookManagerSelected}
          {#if managerReviewPayload}
            <BookManagerRequest request={{ ...managerReviewPayload, bookRenditions: null }}
              title={activeTitle} hasEbook={false} hasAudiobook={false}
              acquisitions={[]} monitors={[]} managedRenditions={[]}
              onCompleted={id => goto(resolve(`/books/${id}` as "/"))} />
          {/if}
        {/if}
      {/if}

      {#if canChooseBookRenditions && !managerSelected && !bookManagerSelected}
        <div class="space-y-2" aria-label="Book formats to request">
          <p class="font-mono text-[0.66rem] font-semibold uppercase tracking-[0.04em] text-text-secondary">Formats</p>
          <label class="flex items-center gap-2 text-sm text-text-secondary">
            <Checkbox checked={selectedBookRenditions.includes(BOOK_RENDITION.ebook)}
              disabled={submitting} onchange={(checked) => setBookRendition(BOOK_RENDITION.ebook, checked)} />
            Ebook
          </label>
          <label class="flex items-center gap-2 text-sm text-text-secondary">
            <Checkbox checked={selectedBookRenditions.includes(BOOK_RENDITION.audiobook)}
              disabled={submitting} onchange={(checked) => setBookRendition(BOOK_RENDITION.audiobook, checked)} />
            Audiobook
          </label>
        </div>
        {#if selectedBookRenditions.includes(BOOK_RENDITION.ebook) && ebookKindInfo}
          <div class="space-y-2"><p class="text-xs text-text-muted">Ebook destination and profile</p>
            <RequestTargetOptions kindInfo={ebookKindInfo} bind:targetLibraryRootId={ebookTargetLibraryRootId} bind:profileId={ebookProfileId} stacked />
          </div>
        {/if}
        {#if selectedBookRenditions.includes(BOOK_RENDITION.audiobook) && audiobookKindInfo}
          <div class="space-y-2"><p class="text-xs text-text-muted">Audiobook destination and profile</p>
            <RequestTargetOptions kindInfo={audiobookKindInfo} bind:targetLibraryRootId={audiobookTargetLibraryRootId} bind:profileId={audiobookProfileId} stacked />
          </div>
        {/if}
      {:else if kindInfo && !managerSelected && !bookManagerSelected}
        <RequestTargetOptions {kindInfo} bind:targetLibraryRootId bind:profileId stacked />
      {/if}
      {#if !bookManagerSelected}
        <Button type="button" variant="primary" class="w-full gap-2"
          disabled={submitting || reviewChanged || enrichmentRunning || !hasRequestIntent || (managerSelected && !managerChoice && !pendingManagerCommit && !managerOwnership)}
          onclick={() => void requestSelection()}>
          {#if submitting}<Loader2 class="h-4 w-4 animate-spin" />{:else if managerOwnership && !pendingManagerCommit}<ExternalLink class="h-4 w-4" />{:else}<Send class="h-4 w-4" />{/if}
          {submitting ? (managerOwnership && !pendingManagerCommit ? "Opening…" : "Requesting…") : pendingManagerCommit ? "Retry request" : managerOwnership ? "Open in library" : managedSeriesSelected && managedSeriesTargetCount > 0
            ? `Request ${managedSeriesTargetCount}${managerChoice?.review.expansion ? " more" : ""} episode${managedSeriesTargetCount === 1 ? "" : "s"}` : canChooseBookRenditions ? `Request ${selectedBookRenditions.length === 2 ? "both formats" : selectedBookRenditions[0] === BOOK_RENDITION.audiobook ? "audiobook" : "ebook"}` : managedSeriesSelected ? "Request selected episodes" : selectsChildren && selectedProposalIds.length > 0
              ? `Request ${selectedProposalIds.length} ${childNoun}${selectedProposalIds.length === 1 ? "" : "s"}` : "Request"}
        </Button>
      {/if}
      {#if pendingManagerCommit && !submitting}
        <p class="text-sm text-text-muted">Acceptance could not be confirmed. Retry checks this same request safely.</p>
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
      {#if bookResultHref}
        <a href={resolve(bookResultHref as "/")} class="text-sm text-text-primary underline">Open book and review each format</a>
      {/if}
    </section>
    {/snippet}
  {/if}
</div>
{/if}
