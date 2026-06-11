import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import { goto } from "$app/navigation";
import { createContext } from "$lib/utils/context";
import {
  addIdentifyQueueItem,
  applyIdentifyQueueItem,
  closeBulkIdentifySession,
  deleteIdentifyQueueItem,
  fetchIdentifyEntity,
  fetchIdentifyApplyProgress,
  fetchIdentifyQueue,
  fetchIdentifyQueueItem,
  searchIdentifyQueueItem,
} from "$lib/api/identify-client";
import { fetchPluginProviders } from "$lib/api/plugins";
import { IDENTIFY_APPLY_STATE } from "$lib/api/generated/codes";
import type {
  EntityMetadataProposal,
  EntitySearchCandidate,
  IdentifyApplyProgress,
  IdentifyBulkSession,
  IdentifyQueueItem as ApiIdentifyQueueItem,
  IdentifyQueueState,
  PluginProvider,
} from "$lib/api/identify-types";
import type { EntityCard, EntityDetailCard } from "$lib/api/entities";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
import { requestMainScrollTop } from "$lib/stores/main-scroll";
import {
  buildRootReviewApplyPayload,
  defaultFieldSelectionForReview,
  defaultImageSelectionForReview,
} from "$lib/components/identify-review";

/**
 * Status of one local structural child in the review grid, derived from the streamed proposal and
 * whether the background cascade is still running. "matched" means the child resolved into the
 * proposal; "loading" means the cascade is still walking; "none" means the cascade finished without
 * a match for it.
 */
export type ChildIdentifyStatus = "matched" | "loading" | "none";

/** Options shared by the auto-identify path that controls whether resolving a queue item also navigates the review view. */
interface IdentifyResolveOptions {
  /** When false, the resolved item is upserted into the queue without navigating to its review view. Defaults to true. */
  navigate?: boolean;
}

interface IdentifyRejectOptions {
  /** When true, move directly to the next reviewable queue item after rejecting this one. */
  navigateNext?: boolean;
}

export type IdentifyView =
  | { kind: "dashboard" }
  | { kind: "kind-tab"; entityKind: string }
  | { kind: "review-choice"; entity: EntityCard; candidates: EntitySearchCandidate[] }
  | { kind: "review-parent"; entity: EntityCard; proposal: EntityMetadataProposal; detail?: EntityDetailCard | null }
  | {
      kind: "review-child";
      entity: EntityCard;
      proposal: EntityMetadataProposal;
      parentProposal: EntityMetadataProposal;
      ancestors: EntityMetadataProposal[];
    };

export interface IdentifyQueueItem {
  id: string;
  entityId: string;
  entityKind: string;
  title: string;
  isNsfw: boolean;
  state: IdentifyQueueState;
  provider?: string | null;
  action: string;
  candidates: EntitySearchCandidate[];
  proposal?: EntityMetadataProposal | null;
  errorMessage?: string | null;
  /** True while a background cascade is still streaming this item's child tree into the proposal. */
  cascadeRunning: boolean;
  entity: EntityCard;
  detail?: EntityDetailCard | null;
  completedAt?: string | null;
}

export interface IdentifyKindInfo {
  kind: string;
  label: string;
  total: number;
  unidentified: number;
  pending: number;
  hasProvider: boolean;
}

const identifyStoreContext = createContext<IdentifyStore>("IdentifyStore");
export const setIdentifyStore = identifyStoreContext.provide;
export const useIdentifyStore = identifyStoreContext.use;
const MIN_APPLY_PROGRESS_VISIBLE_MS = 650;

export class IdentifyStore {
  #getHideNsfw: () => boolean;
  #queueHideNsfw: boolean | null = null;

  view = $state<IdentifyView>({ kind: "dashboard" });
  providers = $state<PluginProvider[]>([]);
  queue = $state<IdentifyQueueItem[]>([]);
  loading = $state(true);
  error = $state<string | null>(null);
  message = $state<string | null>(null);
  identifyingId = $state<string | null>(null);
  identifyingProviderId = $state<string | null>(null);
  identifyingProviderName = $state<string | null>(null);
  identifyingProviderIndex = $state<number | null>(null);
  identifyingProviderTotal = $state<number | null>(null);
  identifyingPhase = $state<"searching" | "matched">("searching");
  applying = $state(false);
  applyProgress = $state<IdentifyApplyProgress | null>(null);
  bulkSession = $state<IdentifyBulkSession | null>(null);
  bulkStarting = $state(false);
  bulkSearching = $state(false);
  bulkSearchDone = $state(0);
  bulkSearchTotal = $state(0);
  bulkAccepting = $state(false);
  bulkAcceptDone = $state(0);
  bulkAcceptTotal = $state(0);
  returnEntityId = $state<string | null>(null);
  reviewRootProposalId = $state<string | null>(null);
  reviewCascadeSelections = $state<Record<string, boolean>>({});
  reviewFieldSelections = $state<Record<string, Record<string, boolean>>>({});
  reviewImageSelections = $state<Record<string, Record<string, string | null>>>({});
  reviewTagSelections = $state<Record<string, Record<string, boolean>>>({});
  reviewDetailsByEntityId = $state<Record<string, EntityDetailCard | null>>({});
  reviewDetailLoadingByEntityId = $state<Record<string, boolean>>({});
  #stopApplyProgressPolling: (() => void) | null = null;
  #cascadePollEntityId: string | null = null;
  #stopCascadePoll: (() => void) | null = null;

  constructor(getHideNsfw: () => boolean = () => false) {
    this.#getHideNsfw = getHideNsfw;
  }

  supportedKinds = $derived.by((): IdentifyKindInfo[] => {
    const kindMap = new Map<string, IdentifyKindInfo>();
    const hideNsfw = this.#getHideNsfw();
    for (const provider of this.providers) {
      if (!provider.installed || !provider.enabled) continue;
      if (hideNsfw && provider.isNsfw) continue;
      for (const support of provider.supports) {
        if (!kindMap.has(support.entityKind)) {
          kindMap.set(support.entityKind, {
            kind: support.entityKind,
            label: entityKindLabel(support.entityKind),
            total: 0,
            unidentified: 0,
            pending: 0,
            hasProvider: true,
          });
        }
      }
    }
    for (const item of this.queue) {
      const info = kindMap.get(item.entityKind);
      if (info) info.pending++;
    }
    return [...kindMap.values()].sort((a, b) => a.label.localeCompare(b.label));
  });

  activeKindTab = $derived.by((): string | null => {
    if (this.view.kind === "kind-tab") return this.view.entityKind;
    return null;
  });

  identifyingStatus = $derived.by((): string | null => {
    if (!this.identifyingId) return null;
    const providerName = this.identifyingProviderName ?? this.identifyingProviderId;
    if (this.identifyingPhase === "matched") {
      return "Match found. Identifying related items; this may take a while.";
    }
    if (!providerName) return "Searching identify providers";
    const pluginLabel = providerName.toLowerCase().includes("plugin")
      ? providerName
      : `${providerName} Plugin`;
    const progress = this.identifyingProviderIndex !== null &&
      this.identifyingProviderTotal !== null &&
      this.identifyingProviderTotal > 1
        ? ` (${this.identifyingProviderIndex + 1}/${this.identifyingProviderTotal})`
        : "";
    return `Searching with ${pluginLabel}${progress}`;
  });

  providersForKind(kind: string): PluginProvider[] {
    // Hide NSFW providers (including every Stash scraper) while browsing in SFW mode so they are
    // never offered as identify sources.
    const hideNsfw = this.#getHideNsfw();
    return this.providers.filter(
      (provider) =>
        provider.installed &&
        provider.enabled &&
        provider.missingAuthKeys.length === 0 &&
        (!hideNsfw || !provider.isNsfw) &&
        provider.supports.some((support) => support.entityKind === kind),
    );
  }

  async loadInitial() {
    this.loading = true;
    this.error = null;
    try {
      const [providers, queue] = await Promise.all([
        fetchPluginProviders(),
        fetchIdentifyQueue(false, this.#getHideNsfw()),
      ]);
      this.providers = providers;
      this.queue = queue.map((item) => queueItemFromApi(item));
      this.#queueHideNsfw = this.#getHideNsfw();
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.loading = false;
    }
  }

  async syncNsfwVisibility(hideNsfw: boolean) {
    if (this.#queueHideNsfw === null) return;
    if (this.#queueHideNsfw === hideNsfw) return;
    await this.refreshQueueForVisibility(hideNsfw);
  }

  async refreshQueueForVisibility(hideNsfw = this.#getHideNsfw()) {
    const queue = await fetchIdentifyQueue(false, hideNsfw);
    this.queue = queue.map((item) => {
      const existing = this.queue.find((queued) => queued.entityId === item.entityId);
      return queueItemFromApi(item, existing?.entity, existing?.detail);
    });
    this.#queueHideNsfw = hideNsfw;
    this.#leaveHiddenReviewIfNeeded();
  }

  async enterDashboardRoute() {
    this.navigateTo({ kind: "dashboard" });
    await this.loadInitial();
  }

  async seedEntity(
    entityId: string,
    returnEntityId: string | null,
    options: { openExistingOnly?: boolean } = {},
  ) {
    this.returnEntityId = returnEntityId;
    this.loading = true;
    this.error = null;
    try {
      const [providers, item, detail] = await Promise.all([
        fetchPluginProviders(),
        options.openExistingOnly
          ? fetchIdentifyQueueItem(entityId).catch(async () => addIdentifyQueueItem(entityId))
          : addIdentifyQueueItem(entityId),
        fetchEntityDetail(entityId),
      ]);
      const hideNsfw = this.#getHideNsfw();
      const queue = await fetchIdentifyQueue(false, hideNsfw);
      this.providers = providers;
      this.queue = queue.map((queued) => queueItemFromApi(queued));
      this.#queueHideNsfw = hideNsfw;
      if (hideNsfw && item.isNsfw) {
        this.navigateToDashboard();
        return null;
      }
      const mapped = queueItemFromApi(item, undefined, detail);
      this.#upsertQueueItem(mapped);
      if (options.openExistingOnly && isActiveQueueState(mapped.state)) {
        this.reviewResolvedQueueItem(mapped);
        return mapped;
      }
      return await this.#autoIdentifyQueueItem(mapped) ?? mapped;
    } catch (err) {
      this.error = readError(err);
      return null;
    } finally {
      this.loading = false;
    }
  }

  async refreshQueueItem(entityId: string) {
    const [item, detail] = await Promise.all([
      addIdentifyQueueItem(entityId),
      fetchEntityDetail(entityId),
    ]);
    const mapped = queueItemFromApi(item, undefined, detail);
    this.#upsertQueueItem(mapped);
    return mapped;
  }

  navigateTo(view: IdentifyView) {
    this.view = view;
    this.error = null;
    this.message = null;
    if (shouldResetScrollForView(view)) requestMainScrollTop();
  }

  navigateToDashboard() {
    this.navigateTo({ kind: "dashboard" });
    void goto("/identify");
  }

  navigateToKind(entityKind: string) {
    this.navigateTo({ kind: "kind-tab", entityKind });
  }

  async queueEntity(entity: EntityCard, providerId?: string | null, options: IdentifyResolveOptions = {}) {
    const [item, detail] = await Promise.all([
      addIdentifyQueueItem(entity.id),
      fetchEntityDetail(entity.id),
    ]);
    const mapped = queueItemFromApi(item, entity, detail);
    this.#upsertQueueItem(mapped);
    return await this.#autoIdentifyQueueItem(mapped, providerId, options) ?? mapped;
  }

  async identifyEntity(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null },
    options: IdentifyResolveOptions = {},
  ) {
    this.identifyingId = entity.id;
    this.error = null;
    this.#setIdentifyingProvider(providerId, 0, 1, identifyQueryHasMatch(query) ? "matched" : "searching");
    try {
      return await this.#searchWithTitleFallback(entity, providerId, query, options);
    } catch (err) {
      this.error = readError(err);
      this.#markQueueError(entity.id, readError(err));
      return null;
    } finally {
      this.#clearIdentifyingStatus();
    }
  }

  async identifyWithCandidate(
    entity: EntityCard,
    providerId: string,
    candidate: EntitySearchCandidate,
    options: IdentifyResolveOptions = {},
  ) {
    return this.identifyEntity(entity, providerId, { externalIds: candidate.externalIds }, options);
  }

  async backToSearch(entity: EntityCard, providerId?: string | null) {
    const queued = this.queue.find((item) => item.entityId === entity.id);
    const selectedProvider = providerId ?? queued?.provider ?? this.providersForKind(entity.kind)[0]?.id;
    if (!selectedProvider) {
      this.error = `No enabled provider supports ${entity.kind}.`;
      return null;
    }

    // Returning to search abandons the current proposal, so drop any in-progress child
    // identification: stop polling the abandoned cascade so re-picking starts cleanly.
    this.stopCascadePoll();
    return this.identifyEntity(entity, selectedProvider, { title: entity.title, requireChoice: true });
  }

  async applyProposal(
    entity: EntityCard,
    proposal: EntityMetadataProposal,
    selectedFields: string[],
    selectedImages?: Record<string, string | null>,
    options: { navigateNext?: boolean } = {},
  ) {
    const progressId = createOperationId();
    const progressStartedAt = nowMs();
    let afterApply: (() => void | Promise<void>) | null = null;
    this.applying = true;
    this.applyProgress = initialApplyProgress(progressId, entity, proposal, selectedFields);
    this.error = null;
    this.#stopApplyProgressPolling?.();
    this.#stopApplyProgressPolling = this.#pollApplyProgress(entity.id, progressId);
    try {
      const item = await applyIdentifyQueueItem(entity.id, proposal, selectedFields, selectedImages, { progressId });
      this.#removeActiveQueueItem(item.entityId);
      if (options.navigateNext) {
        const next = this.nextQueueItem(item.entityId);
        if (next) {
          afterApply = () => this.reviewQueueItem(next);
        }
      }

      if (!afterApply && this.returnEntityId) {
        const href = await resolveEntityHrefById(this.returnEntityId);
        if (href) {
          afterApply = () => goto(href);
        }
      }

      if (!afterApply) {
        this.message = `${proposal.patch.title ?? entity.title} identified`;
        afterApply = () => this.navigateToDashboard();
      }
    } catch (err) {
      this.error = readError(err);
    } finally {
      await waitForMinimumApplyProgress(progressStartedAt);
      this.#stopApplyProgressPolling?.();
      this.#stopApplyProgressPolling = null;
      this.applying = false;
      this.applyProgress = null;
    }

    if (!this.error && afterApply) {
      try {
        await afterApply();
      } catch (err) {
        // The apply already succeeded; a failed navigation must not strand the
        // review on a half-removed item. Surface it and fall back to the dashboard.
        this.error = readError(err);
        this.navigateToDashboard();
      }
    }
  }

  async rejectQueueItem(entityId: string, options: IdentifyRejectOptions = {}) {
    let afterReject: (() => void | Promise<void>) | null = null;
    this.error = null;
    try {
      const item = await deleteIdentifyQueueItem(entityId);
      this.#removeActiveQueueItem(item.entityId);
      if (this.#cascadePollEntityId === item.entityId) {
        this.stopCascadePoll();
      }

      if (options.navigateNext) {
        const next = this.nextQueueItem(item.entityId);
        if (next) {
          afterReject = () => this.reviewQueueItem(next);
        }
      }

      if (!afterReject && this.returnEntityId) {
        const href = await resolveEntityHrefById(this.returnEntityId);
        if (href) {
          afterReject = () => goto(href);
        }
      }

      afterReject ??= () => this.navigateToDashboard();
    } catch (err) {
      this.error = readError(err);
    }

    if (!this.error && afterReject) {
      try {
        await afterReject();
      } catch (err) {
        this.error = readError(err);
        this.navigateToDashboard();
      }
    }
  }

  async deleteQueueItem(entityId: string) {
    await this.rejectQueueItem(entityId);
  }

  /**
   * Queues a batch for identify in two phases so the originating tab is never
   * held open while matches resolve:
   *
   * 1. Every selected entity is added to the queue up front (a fast insert with
   *    no provider lookup), each surfacing in the queue as it lands, and the
   *    user is dropped back on the dashboard immediately.
   * 2. The queued items are then searched one at a time on the dashboard. Items
   *    with a confident match become reviewable proposals; anything ambiguous
   *    stays in `search` for the user to pick a candidate — nothing is
   *    auto-selected on their behalf.
   */
  async startBulk(providerId: string, entities: EntityCard[]) {
    this.bulkStarting = true;
    this.error = null;
    const pending: IdentifyQueueItem[] = [];
    try {
      for (const entity of entities) {
        const queued = await this.#addEntityToQueue(entity, providerId);
        if (queued) pending.push(queued);
      }
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.bulkStarting = false;
    }

    // Hand the user back to the dashboard now that everything is queued, then
    // run the searches there so the queue fills in without pinning the tab.
    this.navigateToDashboard();
    await this.#processQueueSearches(pending, providerId);
  }

  /**
   * Adds a single entity to the queue without running a provider search. The
   * passed card is reused for the queue row so the dashboard can render it
   * immediately, and the chosen provider is recorded as a hint for the later
   * search pass.
   */
  async #addEntityToQueue(entity: EntityCard, providerId?: string | null): Promise<IdentifyQueueItem | null> {
    const item = await addIdentifyQueueItem(entity.id);
    const mapped = queueItemFromApi(item, entity);
    mapped.provider = item.provider ?? providerId ?? null;
    this.#upsertQueueItem(mapped);
    return mapped;
  }

  /**
   * Runs the provider search for each freshly queued item in turn, leaving the
   * result in whatever state the provider returned (proposal or candidate
   * search). Items that were already resolved or removed are skipped.
   */
  async #processQueueSearches(items: IdentifyQueueItem[], providerId?: string | null) {
    const searchable = items.filter((item) => item.state === "search" && item.candidates.length === 0);
    if (searchable.length === 0) return;
    this.bulkSearching = true;
    this.bulkSearchDone = 0;
    this.bulkSearchTotal = searchable.length;
    this.error = null;
    try {
      for (const item of searchable) {
        const current = this.queue.find((queued) => queued.entityId === item.entityId);
        if (!current || current.state !== "search" || current.candidates.length > 0) {
          this.bulkSearchDone++;
          continue;
        }
        try {
          await this.#runQueueSearch(current, providerId);
        } catch (err) {
          this.error = readError(err);
        }
        this.bulkSearchDone++;
      }
    } finally {
      this.bulkSearching = false;
    }
  }

  /** Searches a queued item without navigating, leaving ambiguous results in `search` for review. */
  async #runQueueSearch(item: IdentifyQueueItem, providerId?: string | null) {
    const selectedProvider = providerId ?? item.provider;
    return selectedProvider
      ? await this.identifyEntity(item.entity, selectedProvider, undefined, { navigate: false })
      : await this.#identifyEntityWithAvailableProviders(item, { navigate: false });
  }

  /**
   * Accepts a queued proposal with the default (accept-everything) selections and applies it,
   * mirroring what the review screen would submit when nothing is deselected.
   *
   * @returns true when the item had a proposal that was applied, false when it was skipped.
   */
  async acceptQueueProposal(item: IdentifyQueueItem): Promise<boolean> {
    // Skip while the cascade is still streaming children — the proposal is partial until then, and the
    // backend rejects an apply with a live cascade marker. Mirrors the review screen's Accept gate.
    if (item.state !== "proposal" || !item.proposal || item.cascadeRunning) return false;
    const payload = buildDefaultApplyPayload(item.proposal);
    const applied = await applyIdentifyQueueItem(
      item.entityId,
      payload.proposal,
      payload.selectedFields,
      payload.selectedImages,
    );
    this.#removeActiveQueueItem(applied.entityId);
    return true;
  }

  /**
   * Accepts every selected queue item that already has a proposal, applying them sequentially.
   * Items without a ready proposal are ignored so the high-level "accept everything" flow stays safe.
   */
  async acceptQueueProposals(items: IdentifyQueueItem[]) {
    const acceptable = items.filter((item) => item.state === "proposal" && item.proposal && !item.cascadeRunning);
    if (acceptable.length === 0) return;
    this.bulkAccepting = true;
    this.bulkAcceptDone = 0;
    this.bulkAcceptTotal = acceptable.length;
    this.error = null;
    try {
      for (const item of acceptable) {
        try {
          await this.acceptQueueProposal(item);
        } catch (err) {
          this.error = readError(err);
        }
        this.bulkAcceptDone++;
      }
    } finally {
      this.bulkAccepting = false;
    }
  }

  async closeBulk() {
    if (!this.bulkSession) return;
    const id = this.bulkSession.id;
    this.bulkSession = null;
    await closeBulkIdentifySession(id).catch(() => undefined);
  }

  addToQueue(
    entity: EntityCard,
    state: IdentifyQueueItem["state"],
    provider?: string,
    proposal?: EntityMetadataProposal,
  ) {
    this.#upsertQueueItem({
      id: entity.id,
      entityId: entity.id,
      entityKind: entity.kind,
      title: entity.title,
      isNsfw: entity.isNsfw,
      state,
      provider,
      action: "search",
      candidates: [],
      proposal,
      cascadeRunning: false,
      entity,
    });
  }

  removeFromQueue(entityId: string) {
    this.#removeActiveQueueItem(entityId);
  }

  resumeNext() {
    const next = this.queue.find((item) => item.state === "proposal" || item.state === "search" || item.state === "error");
    if (next) this.reviewQueueItem(next);
  }

  reviewQueueItem(item: IdentifyQueueItem) {
    void goto(`/identify/${item.entityId}`);
  }

  reviewResolvedQueueItem(item: IdentifyQueueItem) {
    if (item.state === "proposal" && item.proposal) {
      this.beginProposalReview(item.proposal);
      this.navigateTo({ kind: "review-parent", entity: item.entity, proposal: item.proposal, detail: item.detail });
    } else if (item.state === "search" && item.candidates.length > 0) {
      this.navigateTo({ kind: "review-choice", entity: item.entity, candidates: item.candidates });
    }
  }

  beginProposalReview(proposal: EntityMetadataProposal) {
    if (this.reviewRootProposalId === proposal.proposalId) return;
    this.reviewRootProposalId = proposal.proposalId;
    this.reviewCascadeSelections = {};
    this.reviewFieldSelections = {};
    this.reviewImageSelections = {};
    this.reviewTagSelections = {};
  }

  isReviewProposalSelected(proposalId: string): boolean {
    return this.reviewCascadeSelections[proposalId] !== false;
  }

  setReviewProposalSelected(proposalId: string, selected: boolean) {
    if (selected) {
      const next = { ...this.reviewCascadeSelections };
      delete next[proposalId];
      this.reviewCascadeSelections = next;
      return;
    }

    this.reviewCascadeSelections = {
      ...this.reviewCascadeSelections,
      [proposalId]: false,
    };
  }

  // ── Streamed child identify ─────────────────────────────────────────────
  // Child identification runs as a backend cascade job (enqueued when a candidate is picked). It
  // streams the growing proposal onto the queue item; the review polls the item so children fill in.
  // The grid derives each child's status from the proposal + whether the cascade is still running.

  /** Whether the entity's background cascade is still resolving its child tree. */
  cascadeRunning(entityId: string): boolean {
    return this.queue.find((item) => item.entityId === entityId)?.cascadeRunning ?? false;
  }

  /** Starts polling the entity's queue item while its cascade streams children in. Idempotent. */
  ensureCascadePoll(entityId: string) {
    if (this.#cascadePollEntityId === entityId) return;
    if (!this.cascadeRunning(entityId)) return;
    this.stopCascadePoll();
    this.#cascadePollEntityId = entityId;
    this.#stopCascadePoll = this.#pollCascade(entityId);
  }

  stopCascadePoll() {
    this.#stopCascadePoll?.();
    this.#stopCascadePoll = null;
    this.#cascadePollEntityId = null;
  }

  #pollCascade(entityId: string): () => void {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const controller = new AbortController();

    const tick = async () => {
      if (stopped) return;
      try {
        const apiItem = await fetchIdentifyQueueItem(entityId, { signal: controller.signal });
        if (stopped) return;
        const existing = this.queue.find((queued) => queued.entityId === entityId);
        const merged = queueItemFromApi(apiItem, existing?.entity, existing?.detail);
        this.#upsertQueueItem(merged);

        // Keep the live parent review in sync as children stream in.
        const view = this.view;
        if (view.kind === "review-parent" && view.entity.id === entityId && merged.proposal) {
          this.view = { ...view, proposal: merged.proposal };
        }

        if (!merged.cascadeRunning) {
          this.stopCascadePoll();
          return;
        }
      } catch (err) {
        if (stopped || (err instanceof Error && err.name === "AbortError")) return;
      }

      if (!stopped) {
        timer = setTimeout(tick, 700);
      }
    };

    timer = setTimeout(tick, 300);
    return () => {
      stopped = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }

  /** Returns the currently-persisted identify proposal for an entity (used to reopen a live review). */
  liveProposalFor(entityId: string): EntityMetadataProposal | null {
    return this.queue.find((item) => item.entityId === entityId)?.proposal ?? null;
  }

  nextQueueItem(currentEntityId: string): IdentifyQueueItem | null {
    return this.queue.find((item) =>
      item.entityId !== currentEntityId &&
      (item.state === "proposal" || item.state === "search" || item.state === "error"),
    ) ?? null;
  }

  getReviewDetail(entityId: string): EntityDetailCard | null {
    if (Object.hasOwn(this.reviewDetailsByEntityId, entityId)) {
      return this.reviewDetailsByEntityId[entityId] ?? null;
    }

    return this.queue.find((item) => item.entityId === entityId)?.detail ?? null;
  }

  async ensureReviewDetail(entityId: string): Promise<EntityDetailCard | null> {
    if (Object.hasOwn(this.reviewDetailsByEntityId, entityId)) {
      return this.reviewDetailsByEntityId[entityId] ?? null;
    }

    const queuedDetail = this.queue.find((item) => item.entityId === entityId)?.detail;
    if (queuedDetail) {
      this.reviewDetailsByEntityId = {
        ...this.reviewDetailsByEntityId,
        [entityId]: queuedDetail,
      };
      return queuedDetail;
    }

    if (this.reviewDetailLoadingByEntityId[entityId]) {
      return null;
    }

    this.reviewDetailLoadingByEntityId = {
      ...this.reviewDetailLoadingByEntityId,
      [entityId]: true,
    };
    try {
      const detail = await fetchEntityDetail(entityId);
      this.reviewDetailsByEntityId = {
        ...this.reviewDetailsByEntityId,
        [entityId]: detail,
      };
      return detail;
    } finally {
      const next = { ...this.reviewDetailLoadingByEntityId };
      delete next[entityId];
      this.reviewDetailLoadingByEntityId = next;
    }
  }

  reviewDetailEntityIdForProposal(scopeEntityId: string, proposal: EntityMetadataProposal): string | null {
    if (proposal.targetEntityId) return proposal.targetEntityId;
    return relationshipEntityIdForProposal(this.getReviewDetail(scopeEntityId), proposal);
  }

  getReviewDetailForProposal(scopeEntityId: string, proposal: EntityMetadataProposal): EntityDetailCard | null {
    const entityId = this.reviewDetailEntityIdForProposal(scopeEntityId, proposal);
    return entityId ? this.getReviewDetail(entityId) : null;
  }

  async ensureReviewDetailForProposal(
    scopeEntityId: string,
    proposal: EntityMetadataProposal,
  ): Promise<EntityDetailCard | null> {
    if (proposal.targetEntityId) {
      return this.ensureReviewDetail(proposal.targetEntityId);
    }

    let entityId = this.reviewDetailEntityIdForProposal(scopeEntityId, proposal);
    if (!entityId && !this.getReviewDetail(scopeEntityId)) {
      await this.ensureReviewDetail(scopeEntityId);
      entityId = this.reviewDetailEntityIdForProposal(scopeEntityId, proposal);
    }

    return entityId ? this.ensureReviewDetail(entityId) : null;
  }

  getReviewFieldSelections(proposalId: string): Record<string, boolean> | null {
    const selected = this.reviewFieldSelections[proposalId];
    return selected ? { ...selected } : null;
  }

  setReviewFieldSelections(proposalId: string, selectedFields: Record<string, boolean>) {
    this.reviewFieldSelections = {
      ...this.reviewFieldSelections,
      [proposalId]: { ...selectedFields },
    };
  }

  setReviewFieldSelected(proposalId: string, field: string, selected: boolean) {
    this.setReviewFieldSelections(proposalId, {
      ...(this.reviewFieldSelections[proposalId] ?? {}),
      [field]: selected,
    });
  }

  getReviewImageSelections(proposalId: string): Record<string, string | null> | null {
    const selected = this.reviewImageSelections[proposalId];
    return selected ? { ...selected } : null;
  }

  setReviewImageSelections(proposalId: string, selectedImages: Record<string, string | null>) {
    this.reviewImageSelections = {
      ...this.reviewImageSelections,
      [proposalId]: { ...selectedImages },
    };
  }

  setReviewImageSelected(proposalId: string, kind: string, url: string | null) {
    this.setReviewImageSelections(proposalId, {
      ...(this.reviewImageSelections[proposalId] ?? {}),
      [kind]: url,
    });
  }

  getReviewTagSelections(proposalId: string): Record<string, boolean> | null {
    const selected = this.reviewTagSelections[proposalId];
    return selected ? { ...selected } : null;
  }

  setReviewTagSelections(proposalId: string, selectedTags: Record<string, boolean>) {
    this.reviewTagSelections = {
      ...this.reviewTagSelections,
      [proposalId]: { ...selectedTags },
    };
  }

  setReviewTagSelected(proposalId: string, tag: string, selected: boolean) {
    this.setReviewTagSelections(proposalId, {
      ...(this.reviewTagSelections[proposalId] ?? {}),
      [tag]: selected,
    });
  }

  #pollApplyProgress(entityId: string, progressId: string): () => void {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const controller = new AbortController();

    const tick = async () => {
      if (stopped) return;
      try {
        const progress = await fetchIdentifyApplyProgress(entityId, progressId, { signal: controller.signal });
        if (!stopped) this.applyProgress = progress;
      } catch (err) {
        if (!stopped && !(err instanceof Error && err.name === "AbortError")) {
          timer = setTimeout(tick, 600);
        }
        return;
      }

      if (!stopped) {
        timer = setTimeout(tick, this.applyProgress?.state === IDENTIFY_APPLY_STATE.running ? 400 : 800);
      }
    };

    timer = setTimeout(tick, 120);
    return () => {
      stopped = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }

  destroy() {
    this.#stopApplyProgressPolling?.();
    this.#stopApplyProgressPolling = null;
    this.stopCascadePoll();
  }

  #upsertQueueItem(item: IdentifyQueueItem) {
    const index = this.queue.findIndex((queued) => queued.entityId === item.entityId);
    if (index >= 0) {
      this.queue[index] = item;
      return;
    }

    this.queue = [...this.queue, item];
  }

  #removeActiveQueueItem(entityId: string) {
    this.queue = this.queue.filter((item) => item.entityId !== entityId);
  }

  #markQueueError(entityId: string, message: string) {
    const item = this.queue.find((queued) => queued.entityId === entityId);
    if (item) {
      item.state = "error";
      item.errorMessage = message;
    }
  }

  /**
   * Runs the provider search for a freshly queued item, leaving the result in
   * whatever state the provider returned. A confident match becomes a reviewable
   * proposal; an ambiguous result stays in `search` so the user picks the
   * candidate themselves rather than having one chosen for them.
   */
  async #autoIdentifyQueueItem(
    item: IdentifyQueueItem,
    providerId?: string | null,
    options: IdentifyResolveOptions = {},
  ) {
    if (item.state !== "search" || item.candidates.length > 0) {
      return item;
    }

    const selectedProvider = providerId ?? item.provider;
    return selectedProvider
      ? await this.identifyEntity(item.entity, selectedProvider, undefined, options)
      : await this.#identifyEntityWithAvailableProviders(item, options);
  }

  async #identifyEntityWithAvailableProviders(item: IdentifyQueueItem, options: IdentifyResolveOptions = {}) {
    const providers = this.providersForKind(item.entityKind);
    if (providers.length === 0) return item;

    this.identifyingId = item.entity.id;
    this.error = null;
    let lastResult: IdentifyQueueItem | null = null;
    let lastError: string | null = null;
    try {
      for (const [index, provider] of providers.entries()) {
        this.#setIdentifyingProvider(provider.id, index, providers.length);
        try {
          const result = await this.#searchWithTitleFallback(item.entity, provider.id, undefined, options);
          lastResult = result;
          if (isResolvedIdentifyResult(result)) return result;
        } catch (err) {
          lastError = readError(err);
        }
      }

      if (lastResult) return lastResult;
      if (lastError) {
        this.error = lastError;
        this.#markQueueError(item.entity.id, lastError);
      }
      return item;
    } finally {
      this.#clearIdentifyingStatus();
    }
  }

  async #searchWithTitleFallback(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null },
    options: IdentifyResolveOptions = {},
  ) {
    const mapped = await this.#searchAndResolve(entity, providerId, query, options);
    if (shouldFallbackToTitleSearch(entity, query, mapped)) {
      return await this.#searchAndResolve(entity, providerId, { title: entity.title }, options);
    }

    return mapped;
  }

  async #searchAndResolve(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null },
    options: IdentifyResolveOptions = {},
  ) {
    const item = await searchIdentifyQueueItem(entity.id, providerId, query);
    const detail = this.queue.find((queued) => queued.entityId === entity.id)?.detail ?? await fetchEntityDetail(entity.id);
    const mapped = queueItemFromApi(item, entity, detail);
    this.#upsertQueueItem(mapped);
    if (options.navigate !== false) this.reviewResolvedQueueItem(mapped);
    return mapped;
  }

  #setIdentifyingProvider(
    providerId: string,
    index: number,
    total: number,
    phase: "searching" | "matched" = "searching",
  ) {
    const provider = this.providers.find((candidate) => candidate.id === providerId);
    this.identifyingProviderId = providerId;
    this.identifyingProviderName = provider?.name ?? providerId;
    this.identifyingProviderIndex = index;
    this.identifyingProviderTotal = total;
    this.identifyingPhase = phase;
  }

  #clearIdentifyingStatus() {
    this.identifyingId = null;
    this.identifyingProviderId = null;
    this.identifyingProviderName = null;
    this.identifyingProviderIndex = null;
    this.identifyingProviderTotal = null;
    this.identifyingPhase = "searching";
  }

  #leaveHiddenReviewIfNeeded() {
    const entityId = this.view.kind === "review-parent" || this.view.kind === "review-child" || this.view.kind === "review-choice"
      ? this.view.entity.id
      : null;
    if (!entityId) return;
    if (this.queue.some((item) => item.entityId === entityId)) return;
    this.navigateToDashboard();
  }
}

/**
 * Builds the apply payload for accepting a proposal wholesale, selecting every available field,
 * the default artwork for each kind, and all proposed tags — the same result as opening the review
 * screen and accepting without changing anything.
 */
function buildDefaultApplyPayload(proposal: EntityMetadataProposal) {
  return buildRootReviewApplyPayload(proposal, {
    selectedFields: defaultFieldSelectionForReview(proposal),
    selectedImages: defaultImageSelectionForReview(proposal),
    selectedTags: Object.fromEntries((proposal.patch?.tags ?? []).map((tag) => [tag, true])),
  });
}

async function fetchEntityDetail(entityId: string): Promise<EntityDetailCard | null> {
  try {
    return await fetchIdentifyEntity(entityId);
  } catch {
    return null;
  }
}

function queueItemFromApi(
  item: ApiIdentifyQueueItem,
  entity?: EntityCard,
  detail?: EntityDetailCard | null,
): IdentifyQueueItem {
  const fallback = entity ?? entityCardFromQueueItem(item);
  const card = detail ? entityThumbnailFromDetail(detail, fallback) : fallback;
  return {
    id: item.id,
    entityId: item.entityId,
    entityKind: item.entityKind,
    title: item.title,
    isNsfw: item.isNsfw,
    state: item.state,
    provider: item.provider,
    action: item.action,
    candidates: item.candidates ?? [],
    proposal: item.proposal ?? null,
    errorMessage: item.error ?? null,
    cascadeRunning: item.cascadeRunning ?? false,
    entity: card,
    detail: detail ?? null,
    completedAt: item.completedAt ?? null,
  };
}

function entityThumbnailFromDetail(detail: EntityDetailCard, fallback?: EntityCard): EntityCard {
  const card = entityCardToThumbnailCard(detail);
  return {
    id: detail.id,
    kind: detail.kind,
    title: detail.title,
    parentEntityId: detail.parentEntityId,
    sortOrder: detail.sortOrder,
    coverUrl: card.cover?.src ?? fallback?.coverUrl ?? null,
    coverThumbUrl: null,
    hoverKind: fallback?.hoverKind ?? THUMBNAIL_HOVER_KIND.none,
    hoverUrl: fallback?.hoverUrl ?? null,
    hoverImages: fallback?.hoverImages ?? [],
    meta: card.meta ?? fallback?.meta ?? [],
    rating: fallback?.rating ?? null,
    isFavorite: fallback?.isFavorite ?? false,
    isNsfw: fallback?.isNsfw ?? false,
    isOrganized: fallback?.isOrganized ?? false,
  };
}

function entityCardFromQueueItem(item: ApiIdentifyQueueItem): EntityCard {
  return {
    id: item.entityId,
    kind: item.entityKind,
    title: item.title,
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: THUMBNAIL_HOVER_KIND.none,
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: item.isNsfw,
    isOrganized: false,
  };
}

function shouldResetScrollForView(view: IdentifyView): boolean {
  return view.kind === "review-parent" || view.kind === "review-child";
}

function isActiveQueueState(state: IdentifyQueueState): boolean {
  return state !== "done" && state !== "deleted";
}

function shouldFallbackToTitleSearch(
  entity: EntityCard,
  query: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null } | undefined,
  item: IdentifyQueueItem,
): boolean {
  if (item.state !== "error") return false;
  if (!entity.title.trim()) return false;
  if (query?.title || query?.url || query?.externalIds || query?.requireChoice) return false;
  return true;
}

function identifyQueryHasMatch(
  query: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null } | undefined,
): boolean {
  return Boolean(query?.url || query?.externalIds && Object.keys(query.externalIds).length > 0);
}

function isResolvedIdentifyResult(item: IdentifyQueueItem): boolean {
  return (item.state === "proposal" && Boolean(item.proposal)) ||
    (item.state === "search" && item.candidates.length > 0);
}

function createOperationId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function nowMs(): number {
  return globalThis.performance?.now?.() ?? Date.now();
}

async function waitForMinimumApplyProgress(startedAt: number): Promise<void> {
  const remaining = MIN_APPLY_PROGRESS_VISIBLE_MS - (nowMs() - startedAt);
  if (remaining > 0) {
    await new Promise<void>((resolve) => setTimeout(resolve, remaining));
  }
}

function initialApplyProgress(
  id: string,
  entity: EntityCard,
  proposal: EntityMetadataProposal,
  selectedFields: string[],
): IdentifyApplyProgress {
  const title = proposalTitleForProgress(proposal) || entity.title;
  return {
    id,
    entityId: entity.id,
    state: IDENTIFY_APPLY_STATE.running,
    currentIndex: 0,
    total: countApplyProgressSteps(proposal, selectedFields),
    // Optimistic progress is for the root entity being applied, so its real kind
    // is the entity's own kind. (The proposal target vocabulary can carry
    // non-entity tokens like "video-episode", which this typed field must not.)
    currentKind: entity.kind,
    currentTitle: title,
    currentPath: [title],
    error: null,
    updatedAt: new Date().toISOString(),
  };
}

function countApplyProgressSteps(
  proposal: EntityMetadataProposal,
  selectedFields: string[],
): number {
  const selected = new Set(selectedFields.map((field) => field.toLowerCase()));
  let count = 1;
  if (selected.has("credits") || selected.has("studio") || selected.has("tags")) {
    count += relationshipProgressSteps(proposal);
  }

  count += structuralProgressChildren(proposal).reduce(
    (total, child) => total + countStructuralApplyProgressSteps(child),
    0,
  );
  return Math.max(count, 1);
}

function countStructuralApplyProgressSteps(proposal: EntityMetadataProposal): number {
  let count = 1;
  if (proposal.patch.credits.length > 0 || Boolean(proposal.patch.studio?.trim()) || proposal.patch.tags.length > 0) {
    count += relationshipProgressSteps(proposal);
  }

  count += structuralProgressChildren(proposal).reduce(
    (total, child) => total + countStructuralApplyProgressSteps(child),
    0,
  );
  return count;
}

function relationshipProgressSteps(proposal: EntityMetadataProposal): number {
  return new Set(
    (proposal.relationships ?? [])
      .filter((child) => isRelationshipProgressKind(child.targetKind))
      .map((child) => child.proposalId),
  ).size;
}

function structuralProgressChildren(proposal: EntityMetadataProposal): EntityMetadataProposal[] {
  return (proposal.children ?? []).filter((child) => !isRelationshipProgressKind(child.targetKind));
}

function isRelationshipProgressKind(kind: string): boolean {
  return kind === "person" || kind === "studio" || kind === "tag";
}

function proposalTitleForProgress(proposal: EntityMetadataProposal): string {
  return proposal.patch.title?.trim() ?? "";
}

function relationshipEntityIdForProposal(
  detail: EntityDetailCard | null | undefined,
  proposal: EntityMetadataProposal,
): string | null {
  const title = proposal.patch.title?.trim();
  if (!title) return null;

  return (detail?.relationships ?? [])
    .filter((group) => relationshipGroupMatchesKind(group.kind, group.code, proposal.targetKind))
    .flatMap((group) => group.entities)
    .find((entity) => entity.title.localeCompare(title, undefined, { sensitivity: "accent" }) === 0)
    ?.id ?? null;
}

function relationshipGroupMatchesKind(kind: string, code: string | null | undefined, targetKind: string): boolean {
  const normalizedKind = kind.toLowerCase();
  const normalizedCode = code?.toLowerCase() ?? "";
  const normalizedTarget = targetKind.toLowerCase();
  if (normalizedKind === normalizedTarget || normalizedCode === normalizedTarget) return true;

  if (normalizedTarget === "person") {
    return normalizedKind === "cast" || normalizedCode === "cast" ||
      normalizedKind === "credits" || normalizedCode === "credits";
  }

  if (normalizedTarget === "tag") {
    return normalizedKind === "tags" || normalizedCode === "tags";
  }

  if (normalizedTarget === "studio") {
    return normalizedKind === "studios" || normalizedCode === "studios";
  }

  return false;
}

function readError(err: unknown): string {
  if (!(err instanceof Error)) return "Request failed";
  try {
    const parsed = JSON.parse(err.message) as { message?: string; detail?: string };
    return parsed.message ?? parsed.detail ?? err.message;
  } catch {
    return err.message;
  }
}

function entityKindLabel(kind: string): string {
  const map: Record<string, string> = {
    movie: "Movies",
    video: "Videos",
    "video-series": "Series",
    "video-season": "Seasons",
    book: "Books",
    "book-volume": "Volumes",
    "book-chapter": "Chapters",
    "music-artist": "Artists",
    "audio-library": "Albums",
    "audio-track": "Tracks",
    gallery: "Galleries",
    image: "Images",
    person: "People",
    studio: "Studios",
    collection: "Collections",
    tag: "Tags",
  };
  return map[kind] ?? kind;
}
