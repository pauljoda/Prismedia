import { getContext, setContext } from "svelte";
import { goto } from "$app/navigation";
import {
  fetchIdentifyEntities,
  fetchIdentifyEntity,
  fetchPluginProviders,
  identifyEntity,
  applyIdentifyProposal,
  startBulkIdentify,
  fetchBulkIdentifySession,
  closeBulkIdentifySession,
  type EntityMetadataProposal,
  type EntitySearchCandidate,
  type IdentifyBulkSession,
  type PluginProvider,
} from "$lib/api/identify";
import type { EntityCard } from "$lib/api/prismedia";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

export type IdentifyView =
  | { kind: "dashboard" }
  | { kind: "kind-tab"; entityKind: string }
  | { kind: "review-choice"; entity: EntityCard; candidates: EntitySearchCandidate[] }
  | { kind: "review-parent"; entity: EntityCard; proposal: EntityMetadataProposal }
  | { kind: "review-child"; entity: EntityCard; proposal: EntityMetadataProposal; parentProposal: EntityMetadataProposal };

export interface IdentifyQueueItem {
  entityId: string;
  entityKind: string;
  title: string;
  state: "not-searched" | "pending-choice" | "pending-review" | "complete" | "error";
  provider?: string;
  proposal?: EntityMetadataProposal;
  errorMessage?: string;
  entity: EntityCard;
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
  returnPath = $state<string | null>(null);

  #pollTimer: ReturnType<typeof setTimeout> | null = null;

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
      if (info) {
        info.pending++;
      }
    }
    return [...kindMap.values()].sort((a, b) => a.label.localeCompare(b.label));
  });

  activeKindTab = $derived.by((): string | null => {
    if (this.view.kind === "kind-tab") return this.view.entityKind;
    return null;
  });

  providersForKind(kind: string): PluginProvider[] {
    return this.providers.filter(
      (p) => p.installed && p.enabled && p.supports.some((s) => s.entityKind === kind),
    );
  }

  async loadInitial() {
    this.loading = true;
    this.error = null;
    try {
      this.providers = await fetchPluginProviders();
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.loading = false;
    }
  }

  async seedEntity(entityId: string, returnPath: string | null) {
    this.returnPath = returnPath;
    this.loading = true;
    this.error = null;
    try {
      this.providers = await fetchPluginProviders();
      const detail = await fetchIdentifyEntity(entityId);
      const entity: EntityCard = {
        id: detail.id,
        kind: detail.kind,
        title: detail.title,
        parentEntityId: detail.parentEntityId ?? null,
        sortOrder: detail.sortOrder ?? null,
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
      this.addToQueue(entity, "not-searched");
      this.navigateToKind(entity.kind);
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.loading = false;
    }
  }

  navigateTo(view: IdentifyView) {
    this.view = view;
    this.error = null;
    this.message = null;
  }

  navigateToDashboard() {
    this.navigateTo({ kind: "dashboard" });
  }

  navigateToKind(entityKind: string) {
    this.navigateTo({ kind: "kind-tab", entityKind });
  }

  async identifyEntity(entity: EntityCard, providerId: string, query?: { externalIds?: Record<string, string> }) {
    this.identifyingId = entity.id;
    this.error = null;
    try {
      const result = await identifyEntity(entity.id, providerId, query);
      if (!result.patch) {
        this.error = "Provider returned an incomplete proposal (no metadata).";
        this.#markQueueError(entity.id, "Provider returned no metadata");
        return;
      }
      const candidates = result.candidates ?? [];
      if (candidates.length > 1 && !query) {
        this.navigateTo({ kind: "review-choice", entity, candidates });
      } else {
        this.navigateTo({ kind: "review-parent", entity, proposal: result });
        this.addToQueue(entity, "pending-review", providerId, result);
      }
    } catch (err) {
      this.error = readError(err);
      this.#markQueueError(entity.id, readError(err));
    } finally {
      this.identifyingId = null;
    }
  }

  async identifyWithCandidate(entity: EntityCard, providerId: string, candidate: EntitySearchCandidate) {
    this.identifyingId = entity.id;
    this.error = null;
    try {
      const result = await identifyEntity(entity.id, providerId, { externalIds: candidate.externalIds });
      if (!result.patch) {
        this.error = "Provider returned an incomplete proposal (no metadata).";
        return;
      }
      this.navigateTo({ kind: "review-parent", entity, proposal: result });
      this.addToQueue(entity, "pending-review", providerId, result);
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.identifyingId = null;
    }
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
      await applyIdentifyProposal(entity.id, proposal, selectedFields, selectedImages);
      this.removeFromQueue(entity.id);
      if (this.returnPath && this.queue.length === 0) {
        void goto(this.returnPath);
        return;
      }
      this.message = `${proposal.patch.title ?? entity.title} identified`;
      this.navigateToDashboard();
    } catch (err) {
      this.error = readError(err);
    } finally {
      this.applying = false;
    }
  }

  async startBulk(providerId: string, entities: EntityCard[]) {
    this.bulkStarting = true;
    this.error = null;
    const providerName = this.providers.find((p) => p.id === providerId)?.name;
    for (const entity of entities) {
      this.addToQueue(entity, "not-searched", providerName);
    }
    try {
      this.bulkSession = await startBulkIdentify(providerId, entities.map((e) => e.id));
      this.#schedulePoll();
    } catch (err) {
      this.error = readError(err);
      for (const entity of entities) {
        this.removeFromQueue(entity.id);
      }
    } finally {
      this.bulkStarting = false;
    }
  }

  async closeBulk() {
    if (!this.bulkSession) return;
    const id = this.bulkSession.id;
    this.bulkSession = null;
    if (this.#pollTimer) clearTimeout(this.#pollTimer);
    this.#pollTimer = null;
    await closeBulkIdentifySession(id).catch(() => undefined);
  }

  addToQueue(entity: EntityCard, state: IdentifyQueueItem["state"], provider?: string, proposal?: EntityMetadataProposal) {
    const existing = this.queue.find((q) => q.entityId === entity.id);
    if (existing) {
      existing.state = state;
      existing.provider = provider;
      existing.proposal = proposal;
    } else {
      this.queue = [
        ...this.queue,
        {
          entityId: entity.id,
          entityKind: entity.kind,
          title: entity.title,
          state,
          provider,
          proposal,
          entity,
        },
      ];
    }
  }

  removeFromQueue(entityId: string) {
    this.queue = this.queue.filter((q) => q.entityId !== entityId);
  }

  resumeNext() {
    const next = this.queue.find(
      (q) => q.state === "pending-review" || q.state === "pending-choice" || q.state === "not-searched",
    );
    if (next) this.reviewQueueItem(next);
  }

  reviewQueueItem(item: IdentifyQueueItem) {
    if (item.state === "pending-review" && item.proposal) {
      this.navigateTo({ kind: "review-parent", entity: item.entity, proposal: item.proposal });
    } else if (item.state === "pending-choice" && item.proposal?.candidates) {
      this.navigateTo({ kind: "review-choice", entity: item.entity, candidates: item.proposal.candidates });
    } else if (item.state === "not-searched" || item.state === "error") {
      const provider = this.providersForKind(item.entityKind)[0];
      if (provider) {
        item.state = "not-searched";
        item.errorMessage = undefined;
        void this.identifyEntity(item.entity, provider.id);
      }
    }
  }

  destroy() {
    if (this.#pollTimer) clearTimeout(this.#pollTimer);
  }

  #markQueueError(entityId: string, message: string) {
    const item = this.queue.find((q) => q.entityId === entityId);
    if (item) {
      item.state = "error";
      item.errorMessage = message;
    }
  }

  #schedulePoll() {
    if (!this.bulkSession || this.bulkSession.status === "completed") return;
    this.#pollTimer = setTimeout(async () => {
      if (!this.bulkSession) return;
      try {
        this.bulkSession = await fetchBulkIdentifySession(this.bulkSession.id);
        for (const result of this.bulkSession.results) {
          const existing = this.queue.find((q) => q.entityId === result.entityId);
          if (!existing) continue;
          if (result.response.ok && result.response.result?.patch) {
            existing.state = "pending-review";
            existing.proposal = result.response.result;
          } else if (result.response.ok && !result.response.result?.patch) {
            existing.state = "error";
            existing.errorMessage = "Provider returned no metadata";
          } else if (!result.response.ok) {
            existing.state = "error";
            existing.errorMessage = result.response.error ?? "Identification failed";
          }
        }
      } catch (err) {
        this.error = readError(err);
        return;
      }
      this.#schedulePoll();
    }, 1200);
  }
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
