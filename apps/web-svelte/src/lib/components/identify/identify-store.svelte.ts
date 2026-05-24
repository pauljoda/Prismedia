import { getContext, setContext } from "svelte";
import { goto } from "$app/navigation";
import {
  addIdentifyQueueItem,
  applyIdentifyQueueItem,
  closeBulkIdentifySession,
  deleteIdentifyQueueItem,
  fetchIdentifyQueue,
  fetchPluginProviders,
  searchIdentifyQueueItem,
  type EntityMetadataProposal,
  type EntitySearchCandidate,
  type IdentifyBulkSession,
  type IdentifyQueueItem as ApiIdentifyQueueItem,
  type IdentifyQueueState,
  type PluginProvider,
} from "$lib/api/identify";
import type { EntityCard } from "$lib/api/prismedia";
import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";

export type IdentifyView =
  | { kind: "dashboard" }
  | { kind: "kind-tab"; entityKind: string }
  | { kind: "review-choice"; entity: EntityCard; candidates: EntitySearchCandidate[] }
  | { kind: "review-parent"; entity: EntityCard; proposal: EntityMetadataProposal }
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
  state: IdentifyQueueState;
  provider?: string | null;
  action: string;
  candidates: EntitySearchCandidate[];
  proposal?: EntityMetadataProposal | null;
  errorMessage?: string | null;
  entity: EntityCard;
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

const CONTEXT_KEY = Symbol("identify-store");

export function setIdentifyStore(store: IdentifyStore): void {
  setContext(CONTEXT_KEY, store);
}

export function useIdentifyStore(): IdentifyStore {
  return getContext<IdentifyStore>(CONTEXT_KEY);
}

export class IdentifyStore {
  view = $state<IdentifyView>({ kind: "dashboard" });
  providers = $state<PluginProvider[]>([]);
  queue = $state<IdentifyQueueItem[]>([]);
  loading = $state(true);
  error = $state<string | null>(null);
  message = $state<string | null>(null);
  identifyingId = $state<string | null>(null);
  applying = $state(false);
  bulkSession = $state<IdentifyBulkSession | null>(null);
  bulkStarting = $state(false);
  returnEntityId = $state<string | null>(null);

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
        fetchIdentifyQueue(),
      ]);
      this.providers = providers;
      this.queue = queue.map((item) => queueItemFromApi(item));
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.loading = false;
    }
  }

  async seedEntity(entityId: string, returnEntityId: string | null) {
    this.returnEntityId = returnEntityId;
    this.loading = true;
    this.error = null;
    try {
      const [providers, item] = await Promise.all([
        fetchPluginProviders(),
        addIdentifyQueueItem(entityId),
      ]);
      this.providers = providers;
      this.#upsertQueueItem(queueItemFromApi(item));
      return item;
    } catch (err) {
      this.error = readError(err);
      return null;
    } finally {
      this.loading = false;
    }
  }

  async refreshQueueItem(entityId: string) {
    const item = await addIdentifyQueueItem(entityId);
    const mapped = queueItemFromApi(item);
    this.#upsertQueueItem(mapped);
    return mapped;
  }

  navigateTo(view: IdentifyView) {
    this.view = view;
    this.error = null;
    this.message = null;
  }

  navigateToDashboard() {
    this.navigateTo({ kind: "dashboard" });
    void goto("/identify");
  }

  navigateToKind(entityKind: string) {
    this.navigateTo({ kind: "kind-tab", entityKind });
  }

  async queueEntity(entity: EntityCard) {
    const item = await addIdentifyQueueItem(entity.id);
    const mapped = queueItemFromApi(item, entity);
    this.#upsertQueueItem(mapped);
    return mapped;
  }

  async identifyEntity(
    entity: EntityCard,
    providerId: string,
    query?: { title?: string | null; externalIds?: Record<string, string> | null },
  ) {
    this.identifyingId = entity.id;
    this.error = null;
    try {
      const item = await searchIdentifyQueueItem(entity.id, providerId, query);
      const mapped = queueItemFromApi(item, entity);
      this.#upsertQueueItem(mapped);
      this.reviewResolvedQueueItem(mapped);
      return mapped;
    } catch (err) {
      this.error = readError(err);
      this.#markQueueError(entity.id, readError(err));
      return null;
    } finally {
      this.identifyingId = null;
    }
  }

  async identifyWithCandidate(entity: EntityCard, providerId: string, candidate: EntitySearchCandidate) {
    return this.identifyEntity(entity, providerId, { externalIds: candidate.externalIds });
  }

  async applyProposal(
    entity: EntityCard,
    proposal: EntityMetadataProposal,
    selectedFields: string[],
    selectedImages?: Record<string, string | null>,
  ) {
    this.applying = true;
    this.error = null;
    try {
      const item = await applyIdentifyQueueItem(entity.id, proposal, selectedFields, selectedImages);
      this.#removeActiveQueueItem(item.entityId);
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
        const queued = await this.queueEntity(entity);
        if (queued.state === "search" && queued.candidates.length === 0) {
          await this.identifyEntity(entity, providerId);
        }
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
      this.navigateTo({ kind: "review-parent", entity: item.entity, proposal: item.proposal });
    } else if (item.state === "search" && item.candidates.length > 0) {
      this.navigateTo({ kind: "review-choice", entity: item.entity, candidates: item.candidates });
    }
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
}

function queueItemFromApi(item: ApiIdentifyQueueItem, entity?: EntityCard): IdentifyQueueItem {
  const card = entity ?? entityCardFromQueueItem(item);
  return {
    id: item.id,
    entityId: item.entityId,
    entityKind: item.entityKind,
    title: item.title,
    state: item.state,
    provider: item.provider,
    action: item.action,
    candidates: item.candidates ?? [],
    proposal: item.proposal ?? null,
    errorMessage: item.error ?? null,
    entity: card,
    completedAt: item.completedAt ?? null,
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
    isNsfw: false,
    isOrganized: false,
  };
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
