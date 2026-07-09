import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import { goto } from "$app/navigation";
import { createContext } from "$lib/utils/context";
import {
  addIdentifyQueueItem,
  applyIdentifyQueueItem,
  deleteIdentifyQueueItem,
  fetchIdentifyEntity,
  fetchIdentifyApplyProgress,
  fetchIdentifyQueue,
  fetchIdentifyQueueItem,
  requestIdentifySearch,
  resolveIdentifyQueueCandidate,
  startBulkIdentify,
} from "$lib/api/identify-client";
import { fetchPluginProviders } from "$lib/api/plugins";
import { IDENTIFY_APPLY_STATE, IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
import type {
  EntityMetadataProposal,
  EntitySearchCandidate,
  IdentifyApplyProgress,
  IdentifyQuery,
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

interface IdentifyRejectOptions {
  /** When true, move directly to the next reviewable queue item after rejecting this one. */
  navigateNext?: boolean;
}

interface IdentifyResultWaitOptions {
  /** How long to wait between queue refreshes while one provider search is running. */
  pollMs?: number;
  /** Maximum time to wait for one provider search before returning the latest item, if any. */
  timeoutMs?: number;
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
const QUEUE_POLL_INITIAL_MS = 300;
const QUEUE_POLL_INTERVAL_MS = 1_000;
const SEARCH_RESULT_POLL_MS = 750;
const SEARCH_RESULT_TIMEOUT_MS = 120_000;

export class IdentifyStore {
  #getHideNsfw: () => boolean;
  #queueHideNsfw: boolean | null = null;

  view = $state<IdentifyView>({ kind: "dashboard" });
  providers = $state<PluginProvider[]>([]);
  queue = $state<IdentifyQueueItem[]>([]);
  loading = $state(true);
  error = $state<string | null>(null);
  message = $state<string | null>(null);
  applying = $state(false);
  applyProgress = $state<IdentifyApplyProgress | null>(null);
  bulkStarting = $state(false);
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
  #stopQueuePolling: (() => void) | null = null;

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

  /** Items waiting for their requested search job to start. */
  queuedCount = $derived(this.queue.filter((item) => item.state === IDENTIFY_QUEUE_STATE.queued).length);

  /** Items whose search job is actively querying a provider. */
  searchingCount = $derived(this.queue.filter((item) => item.state === IDENTIFY_QUEUE_STATE.searching).length);

  /** Items holding a result (candidates or proposal) waiting for the user. */
  reviewableCount = $derived(this.queue.filter((item) =>
    (item.state === IDENTIFY_QUEUE_STATE.proposal && Boolean(item.proposal)) ||
    (item.state === IDENTIFY_QUEUE_STATE.search && item.candidates.length > 0)).length);

  /** Whether the entity's queue item is waiting on or running a requested search. */
  isItemBusy(entityId: string): boolean {
    const item = this.queue.find((queued) => queued.entityId === entityId);
    return item?.state === IDENTIFY_QUEUE_STATE.queued || item?.state === IDENTIFY_QUEUE_STATE.searching;
  }

  /** Short status line for an item's in-flight search, or null when none is running. */
  itemSearchStatus(entityId: string): string | null {
    const item = this.queue.find((queued) => queued.entityId === entityId);
    if (item?.state === IDENTIFY_QUEUE_STATE.queued) return "Queued for search";
    if (item?.state === IDENTIFY_QUEUE_STATE.searching) {
      const provider = this.providers.find((candidate) => candidate.id === item.provider);
      return `Searching with ${provider?.name ?? item.provider ?? "providers"}`;
    }
    return null;
  }

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
      this.ensureQueuePolling();
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
    this.#mergeQueueFromApi(queue, hideNsfw);
  }

  async enterDashboardRoute() {
    this.navigateTo({ kind: "dashboard" });
    await this.loadInitial();
  }

  async seedEntity(entityId: string, returnEntityId: string | null) {
    this.returnEntityId = returnEntityId;
    this.loading = true;
    this.error = null;
    try {
      // Opening an item never starts a search; it renders whatever state the server holds.
      // Searches are requested explicitly (identify button, candidate pick, manual query).
      const [providers, item, detail] = await Promise.all([
        fetchPluginProviders(),
        fetchIdentifyQueueItem(entityId).catch(async () => addIdentifyQueueItem(entityId)),
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
      if (isActiveQueueState(mapped.state)) {
        this.reviewResolvedQueueItem(mapped);
      }
      this.ensureQueuePolling();
      return mapped;
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

  /**
   * Requests a provider search for the entity. The search runs as a background job on the
   * server; the returned item is already in the queued state and this store's polling renders
   * its progress (queued → searching → candidates/proposal/error).
   */
  async identifyEntity(
    entity: EntityCard,
    providerId: string | null,
    query?: IdentifyQuery,
  ) {
    this.error = null;
    try {
      const item = await requestIdentifySearch(entity.id, providerId, query, this.#getHideNsfw());
      const existing = this.queue.find((queued) => queued.entityId === entity.id);
      const detail = existing?.detail ?? await fetchEntityDetail(entity.id);
      const mapped = queueItemFromApi(item, entity, detail);
      this.#upsertQueueItem(mapped);
      this.ensureQueuePolling();
      return mapped;
    } catch (err) {
      this.error = readError(err);
      this.#markQueueError(entity.id, readError(err));
      return null;
    }
  }

  async waitForIdentifyResult(
    entityId: string,
    providerId: string,
    options: IdentifyResultWaitOptions = {},
  ): Promise<IdentifyQueueItem | null> {
    const pollMs = options.pollMs ?? SEARCH_RESULT_POLL_MS;
    const timeoutMs = options.timeoutMs ?? SEARCH_RESULT_TIMEOUT_MS;
    const deadline = Date.now() + timeoutMs;

    while (Date.now() <= deadline) {
      const current = this.queue.find((item) => item.entityId === entityId && item.provider === providerId);
      if (current && isIdentifyResultItem(current)) {
        return current;
      }

      await wait(pollMs);
      const hideNsfw = this.#getHideNsfw();
      const queue = await fetchIdentifyQueue(false, hideNsfw);
      this.#mergeQueueFromApi(queue, hideNsfw);
      this.#syncLiveReviewProposal();
    }

    return this.queue.find((item) => item.entityId === entityId && item.provider === providerId) ?? null;
  }

  async identifyWithCandidate(
    entity: EntityCard,
    providerId: string,
    candidate: EntitySearchCandidate,
  ) {
    this.error = null;
    try {
      const existing = this.queue.find((queued) => queued.entityId === entity.id);
      const item = await resolveIdentifyQueueCandidate(entity.id, providerId, candidate, this.#getHideNsfw());
      const detail = existing?.detail ?? await fetchEntityDetail(entity.id);
      const mapped = queueItemFromApi(item, entity, detail);
      this.#upsertQueueItem(mapped);
      this.ensureQueuePolling();
      return mapped;
    } catch (err) {
      this.error = readError(err);
      return null;
    }
  }

  async backToSearch(entity: EntityCard, providerId?: string | null) {
    const queued = this.queue.find((item) => item.entityId === entity.id);
    const selectedProvider = providerId ?? queued?.provider ?? this.providersForKind(entity.kind)[0]?.id;
    if (!selectedProvider) {
      this.error = `No enabled provider supports ${entity.kind}.`;
      return null;
    }

    // Returning to search abandons the current proposal; the server cancels the abandoned
    // cascade when it stamps the new search request.
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
   * Requests identify searches for a batch in one call: the server creates each item in the
   * queued state and enqueues one identify-search job per entity, so progress is durable and
   * one slow provider call can never stall the rest of the batch. Local rows are shown
   * optimistically and replaced by the server's queue on the next refresh.
   */
  async startBulk(providerId: string | null, entities: EntityCard[]) {
    this.bulkStarting = true;
    this.error = null;
    try {
      for (const entity of entities) {
        this.addToQueue(entity, IDENTIFY_QUEUE_STATE.queued, providerId ?? undefined);
      }

      await startBulkIdentify(providerId, entities.map((entity) => entity.id), null, this.#getHideNsfw());
      await this.refreshQueueForVisibility();
      this.ensureQueuePolling();
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.bulkStarting = false;
    }

    this.navigateToDashboard();
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

  // ── Live queue polling ──────────────────────────────────────────────────
  // One loop renders every server-side transition: requested searches moving through
  // queued → searching → result, and cascades streaming children onto an open review.
  // It runs only while something is actually in flight and stops itself when idle.

  /** Whether the entity's background cascade is still resolving its child tree. */
  cascadeRunning(entityId: string): boolean {
    return this.queue.find((item) => item.entityId === entityId)?.cascadeRunning ?? false;
  }

  /** Starts the live queue poll if anything is in flight. Idempotent; the loop stops itself when idle. */
  ensureQueuePolling() {
    if (this.#stopQueuePolling) return;
    if (!this.#queueHasLiveWork()) return;
    this.#stopQueuePolling = this.#pollQueue();
  }

  #queueHasLiveWork(): boolean {
    return this.queue.some((item) =>
      item.state === IDENTIFY_QUEUE_STATE.queued ||
      item.state === IDENTIFY_QUEUE_STATE.searching ||
      item.cascadeRunning);
  }

  #pollQueue(): () => void {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const controller = new AbortController();

    const tick = async () => {
      if (stopped) return;
      try {
        const hideNsfw = this.#getHideNsfw();
        const queue = await fetchIdentifyQueue(false, hideNsfw, { signal: controller.signal });
        if (stopped) return;
        this.#mergeQueueFromApi(queue, hideNsfw);
        this.#syncLiveReviewProposal();

        if (!this.#queueHasLiveWork()) {
          this.#stopQueuePolling = null;
          stopped = true;
          return;
        }
      } catch (err) {
        if (stopped || (err instanceof Error && err.name === "AbortError")) return;
      }

      if (!stopped) {
        timer = setTimeout(tick, QUEUE_POLL_INTERVAL_MS);
      }
    };

    timer = setTimeout(tick, QUEUE_POLL_INITIAL_MS);
    return () => {
      stopped = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }

  /** Keeps an open parent review in sync as its cascade streams children onto the proposal. */
  #syncLiveReviewProposal() {
    const view = this.view;
    if (view.kind !== "review-parent") return;
    const item = this.queue.find((queued) => queued.entityId === view.entity.id);
    if (item?.proposal) {
      this.view = { ...view, proposal: item.proposal };
    }
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
    this.#stopQueuePolling?.();
    this.#stopQueuePolling = null;
  }

  #mergeQueueFromApi(queue: ApiIdentifyQueueItem[], hideNsfw = this.#getHideNsfw()) {
    this.queue = queue.map((item) => {
      const existing = this.queue.find((queued) => queued.entityId === item.entityId);
      return queueItemFromApi(item, existing?.entity, existing?.detail);
    });
    this.#queueHideNsfw = hideNsfw;
    this.#leaveHiddenReviewIfNeeded();
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

function isIdentifyResultItem(item: IdentifyQueueItem): boolean {
  if (item.state === IDENTIFY_QUEUE_STATE.proposal && item.proposal) return true;
  if (item.state === IDENTIFY_QUEUE_STATE.search && item.candidates.length > 0) return true;
  return item.state === IDENTIFY_QUEUE_STATE.error;
}

function isActiveQueueState(state: IdentifyQueueState): boolean {
  return state !== "done" && state !== "deleted";
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
    await wait(remaining);
  }
}

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
