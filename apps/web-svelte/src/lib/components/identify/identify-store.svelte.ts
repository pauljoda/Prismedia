import { goto } from "$app/navigation";
import { createContext } from "$lib/utils/context";
import {
  addIdentifyQueueItem,
  applyIdentifyQueueItem,
  closeBulkIdentifySession,
  deleteIdentifyQueueItem,
  fetchIdentifyEntity,
  fetchIdentifyQueue,
  fetchIdentifyQueueItem,
  searchIdentifyQueueItem,
} from "$lib/api/identify-client";
import { fetchPluginProviders } from "$lib/api/plugins";
import type {
  EntityMetadataProposal,
  EntitySearchCandidate,
  IdentifyBulkSession,
  IdentifyQueueItem as ApiIdentifyQueueItem,
  IdentifyQueueState,
  PluginProvider,
} from "$lib/api/identify-types";
import type { EntityCard, EntityDetailCard } from "$lib/api/entities";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
import { requestMainScrollTop } from "$lib/stores/main-scroll";

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
  bulkSession = $state<IdentifyBulkSession | null>(null);
  bulkStarting = $state(false);
  returnEntityId = $state<string | null>(null);
  reviewRootProposalId = $state<string | null>(null);
  reviewCascadeSelections = $state<Record<string, boolean>>({});
  reviewFieldSelections = $state<Record<string, Record<string, boolean>>>({});
  reviewImageSelections = $state<Record<string, Record<string, string | null>>>({});
  reviewTagSelections = $state<Record<string, Record<string, boolean>>>({});
  reviewDetailsByEntityId = $state<Record<string, EntityDetailCard | null>>({});
  reviewDetailLoadingByEntityId = $state<Record<string, boolean>>({});

  constructor(getHideNsfw: () => boolean = () => false) {
    this.#getHideNsfw = getHideNsfw;
  }

  supportedKinds = $derived.by((): IdentifyKindInfo[] => {
    const kindMap = new Map<string, IdentifyKindInfo>();
    for (const provider of this.providers) {
      if (!provider.installed || !provider.enabled) continue;
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
    return this.providers.filter(
      (provider) =>
        provider.installed &&
        provider.enabled &&
        provider.missingAuthKeys.length === 0 &&
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

  async queueEntity(entity: EntityCard, providerId?: string | null) {
    const [item, detail] = await Promise.all([
      addIdentifyQueueItem(entity.id),
      fetchEntityDetail(entity.id),
    ]);
    const mapped = queueItemFromApi(item, entity, detail);
    this.#upsertQueueItem(mapped);
    return await this.#autoIdentifyQueueItem(mapped, providerId) ?? mapped;
  }

  async identifyEntity(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null },
  ) {
    this.identifyingId = entity.id;
    this.error = null;
    this.#setIdentifyingProvider(providerId, 0, 1, identifyQueryHasMatch(query) ? "matched" : "searching");
    try {
      return await this.#searchWithTitleFallback(entity, providerId, query);
    } catch (err) {
      this.error = readError(err);
      this.#markQueueError(entity.id, readError(err));
      return null;
    } finally {
      this.#clearIdentifyingStatus();
    }
  }

  async identifyWithCandidate(entity: EntityCard, providerId: string, candidate: EntitySearchCandidate) {
    return this.identifyEntity(entity, providerId, { externalIds: candidate.externalIds });
  }

  async backToSearch(entity: EntityCard, providerId?: string | null) {
    const queued = this.queue.find((item) => item.entityId === entity.id);
    const selectedProvider = providerId ?? queued?.provider ?? this.providersForKind(entity.kind)[0]?.id;
    if (!selectedProvider) {
      this.error = `No enabled provider supports ${entity.kind}.`;
      return null;
    }

    return this.identifyEntity(entity, selectedProvider, { title: entity.title, requireChoice: true });
  }

  async applyProposal(
    entity: EntityCard,
    proposal: EntityMetadataProposal,
    selectedFields: string[],
    selectedImages?: Record<string, string | null>,
    options: { navigateNext?: boolean } = {},
  ) {
    this.applying = true;
    this.error = null;
    try {
      const item = await applyIdentifyQueueItem(entity.id, proposal, selectedFields, selectedImages);
      this.#removeActiveQueueItem(item.entityId);
      if (options.navigateNext) {
        const next = this.nextQueueItem(item.entityId);
        if (next) {
          this.reviewQueueItem(next);
          return;
        }
      }

      if (this.returnEntityId) {
        const href = await resolveEntityHrefById(this.returnEntityId);
        if (href) {
          void goto(href);
          return;
        }
      }

      this.message = `${proposal.patch.title ?? entity.title} identified`;
      this.navigateToDashboard();
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.applying = false;
    }
  }

  async deleteQueueItem(entityId: string) {
    this.error = null;
    try {
      const item = await deleteIdentifyQueueItem(entityId);
      this.#removeActiveQueueItem(item.entityId);
      if (this.returnEntityId) {
        const href = await resolveEntityHrefById(this.returnEntityId);
        if (href) {
          void goto(href);
          return;
        }
      }
      this.navigateToDashboard();
    } catch (err) {
      this.error = readError(err);
    }
  }

  async startBulk(providerId: string, entities: EntityCard[]) {
    this.bulkStarting = true;
    this.error = null;
    try {
      for (const entity of entities) {
        await this.queueEntity(entity, providerId);
      }
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.bulkStarting = false;
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

  destroy() {
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

  async #autoIdentifyQueueItem(item: IdentifyQueueItem, providerId?: string | null) {
    if (item.state !== "search" || item.candidates.length > 0) {
      return item;
    }

    const selectedProvider = providerId ?? item.provider;
    if (selectedProvider) return this.identifyEntity(item.entity, selectedProvider);

    return this.#identifyEntityWithAvailableProviders(item);
  }

  async #identifyEntityWithAvailableProviders(item: IdentifyQueueItem) {
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
          const result = await this.#searchWithTitleFallback(item.entity, provider.id);
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
  ) {
    const mapped = await this.#searchAndResolve(entity, providerId, query);
    if (shouldFallbackToTitleSearch(entity, query, mapped)) {
      return await this.#searchAndResolve(entity, providerId, { title: entity.title });
    }

    return mapped;
  }

  async #searchAndResolve(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; url?: string | null; externalIds?: Record<string, string> | null; requireChoice?: boolean | null },
  ) {
    const item = await searchIdentifyQueueItem(entity.id, providerId, query);
    const detail = this.queue.find((queued) => queued.entityId === entity.id)?.detail ?? await fetchEntityDetail(entity.id);
    const mapped = queueItemFromApi(item, entity, detail);
    this.#upsertQueueItem(mapped);
    this.reviewResolvedQueueItem(mapped);
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
    hoverKind: fallback?.hoverKind ?? "none",
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
    hoverKind: "none",
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
    video: "Videos",
    "video-series": "Series",
    "video-season": "Seasons",
    book: "Books",
    "book-volume": "Volumes",
    "book-chapter": "Chapters",
    "audio-library": "Audio",
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
