import { render, screen } from "@testing-library/svelte";
import { flushSync } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import type { EntityCard } from "$lib/api/entities";
import {
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  IDENTIFY_ACTION,
  IDENTIFY_QUEUE_STATE,
  THUMBNAIL_HOVER_KIND,
} from "$lib/api/generated/codes";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import type {
  IdentifyApplyingReview,
  IdentifyQueueItem,
  IdentifyView,
} from "$lib/components/identify/identify-store.svelte";
import IdentifyEntityPage from "./+page.svelte";

/**
 * A reactive stand-in for the identify store: the state the route and the review surface read,
 * declared as runes so the page re-renders as a test drives it through an apply.
 */
class FakeIdentifyStore {
  queue = $state<IdentifyQueueItem[]>([]);
  view = $state<IdentifyView>({ kind: "dashboard" });
  applying = $state(false);
  applyingReview = $state<IdentifyApplyingReview | null>(null);
  loading = $state(false);
  error = $state<string | null>(null);
  message = null;
  applyProgress = null;
  providers = [];
  reviewCascadeSelections = {};
  reviewFieldSelections = {};
  reviewImageSelections = {};
  reviewTagSelections = {};

  get applyingEntityId(): string | null {
    return this.applyingReview?.entity.id ?? null;
  }

  seedEntity = vi.fn(async () => null);
  providersForKind = vi.fn(() => []);
  itemSearchStatus = vi.fn(() => null);
  isItemBusy = vi.fn(() => false);
  nextQueueItem = vi.fn(() => null);
  beginProposalReview = vi.fn();
  getReviewDetailForProposal = vi.fn();
  getReviewFieldSelections = vi.fn();
  getReviewImageSelections = vi.fn();
  getReviewTagSelections = vi.fn();
  isReviewProposalSelected = vi.fn(() => true);
  navigateTo = vi.fn();
  navigateToDashboard = vi.fn();
  reviewDetailEntityIdForProposal = vi.fn(() => null);
  setReviewFieldSelections = vi.fn();
  setReviewImageSelections = vi.fn();
  setReviewProposalSelected = vi.fn();
  setReviewTagSelections = vi.fn();
  setReviewTagSelected = vi.fn();
  ensureReviewDetailForProposal = vi.fn();
  deleteQueueItem = vi.fn();
  rejectQueueItem = vi.fn();
  applyProposal = vi.fn();
  cascadeRunning = vi.fn(() => false);
  ensureCascadePoll = vi.fn();
  stopCascadePoll = vi.fn();
}

let store: FakeIdentifyStore;

vi.mock("$lib/components/identify/identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

vi.mock("$lib/stores/app-chrome.svelte", () => ({
  useAppChrome: () => ({ setBreadcrumbs: () => () => {} }),
}));

describe("identify entity route", () => {
  beforeEach(() => {
    store = new FakeIdentifyStore();
    page.params.entityId = "video-1";
  });

  afterEach(() => {
    delete page.params.entityId;
  });

  it("keeps one review mounted while the accepted item passes through done and leaves the queue", () => {
    const movie = entity("video-1");
    const shown = proposal("tmdb:movie:123");
    const item = queueItem(movie, shown);
    store.queue = [item];

    const { container } = render(IdentifyEntityPage);
    const actions = () => container.querySelector('[data-testid="identify-proposal-actions"]');
    const review = actions();
    expect(review).not.toBeNull();
    expect(screen.queryByRole("spinbutton", { name: "Year" })).not.toBeInTheDocument();

    // Accept: the store holds the review as shown while the server marks the item applying.
    store.applying = true;
    store.applyingReview = { entity: movie, proposal: shown, detail: null };
    store.queue = [{ ...item, state: IDENTIFY_QUEUE_STATE.applying }];
    flushSync();
    expect(actions()).toBe(review);

    // The apply poll reports done before the page moves on.
    store.queue = [{ ...item, state: IDENTIFY_QUEUE_STATE.done }];
    flushSync();
    expect(actions()).toBe(review);
    expect(screen.queryByRole("spinbutton", { name: "Year" })).not.toBeInTheDocument();

    // The queue poll no longer lists the finished item at all.
    store.queue = [];
    flushSync();
    expect(actions()).toBe(review);
    expect(screen.queryByText("Preparing identify review")).not.toBeInTheDocument();
  });

  it("does not hold a review that belongs to another entity", () => {
    const other = entity("video-2");
    store.applying = true;
    store.applyingReview = { entity: other, proposal: proposal("tmdb:movie:999"), detail: null };
    store.queue = [];

    const { container } = render(IdentifyEntityPage);

    expect(container.querySelector('[data-testid="identify-proposal-actions"]')).toBeNull();
    expect(screen.getByText("Preparing identify review")).toBeInTheDocument();
  });
});

function entity(id: string): EntityCard {
  return {
    id,
    kind: ENTITY_KIND.video,
    title: "Friendship",
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
    isNsfw: false,
    isOrganized: false,
  };
}

function proposal(proposalId: string): EntityMetadataProposal {
  return {
    proposalId,
    provider: EXTERNAL_ID_PROVIDER.tmdb,
    targetKind: ENTITY_KIND.video,
    confidence: 1,
    matchReason: "test",
    patch: {
      title: "Friendship",
      description: null,
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: [],
    children: [],
    relationships: [],
    candidates: [],
    targetEntityId: null,
  };
}

function queueItem(card: EntityCard, reviewed: EntityMetadataProposal): IdentifyQueueItem {
  return {
    id: `queue-${card.id}`,
    entityId: card.id,
    entityKind: card.kind,
    title: card.title,
    isNsfw: false,
    state: IDENTIFY_QUEUE_STATE.proposal,
    provider: EXTERNAL_ID_PROVIDER.tmdb,
    action: IDENTIFY_ACTION.search,
    candidates: [],
    proposal: reviewed,
    cascadeRunning: false,
    entity: card,
    detail: null,
  };
}
