import type { EntityMetadataUpdateRequest } from "$lib/api/entity-mutations";
import {
  updateEntityFlags,
  updateEntityMetadata,
  updateEntityRating,
} from "$lib/api/entity-mutations";
import {
  toggleOptimisticEntityFlag,
  updateOptimisticEntityRating,
  type EntityDetailStateTarget,
  type EntityFlagPersist,
  type EntityRatingPersist,
} from "$lib/entities/entity-detail-state";
import type { NsfwMode } from "$lib/nsfw/cookie";
import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
import { useNsfw } from "$lib/nsfw/store.svelte";
import { useAppChrome, type AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
import { untrack } from "svelte";
import type { EntityDetailPageLoadState } from "./EntityDetailPageState.svelte";

export interface EntityDetailPageEntity extends EntityDetailStateTarget {
  kind: string;
  title: string;
}

export interface EntityDetailPageLoadContext {
  nsfwMode: NsfwMode;
  signal: AbortSignal;
}

export interface EntityDetailPageMutations {
  flags: EntityFlagPersist;
  metadata: (id: string, request: EntityMetadataUpdateRequest) => Promise<unknown>;
  rating: EntityRatingPersist;
}

export interface EntityDetailPageOptions<T extends EntityDetailPageEntity> {
  breadcrumbs: (entity: T) => AppBreadcrumb[];
  load: (context: EntityDetailPageLoadContext) => Promise<T>;
  loadKey: () => string;
  mutations?: Partial<EntityDetailPageMutations>;
  reloadOnNsfwChange?: boolean;
}

interface ReloadOptions {
  nsfwMode?: NsfwMode;
  showLoading?: boolean;
}

const defaultMutations: EntityDetailPageMutations = {
  flags: updateEntityFlags,
  metadata: updateEntityMetadata,
  rating: updateEntityRating,
};

/**
 * Owns the lifecycle and shared mutations for one entity detail route.
 * Route modules remain responsible only for entity-specific hydration and presentation.
 */
export class EntityDetailPageController<T extends EntityDetailPageEntity> {
  entity = $state.raw<T | null>(null);
  errorMessage = $state<string | null>(null);
  loadState = $state<EntityDetailPageLoadState>("loading");
  ratingBusy = $state(false);

  private activeAbortController: AbortController | null = null;
  private loadGeneration = 0;
  private readonly mutations: EntityDetailPageMutations;

  constructor(
    private readonly loadEntity: (context: EntityDetailPageLoadContext) => Promise<T>,
    private readonly getNsfwMode: () => NsfwMode,
    mutations?: Partial<EntityDetailPageMutations>,
  ) {
    this.mutations = { ...defaultMutations, ...mutations };
  }

  /** Reloads the route entity while ignoring stale or cancelled responses. */
  reload = async (options: ReloadOptions = {}): Promise<void> => {
    const showLoading = options.showLoading ?? this.entity === null;
    const nsfwMode = options.nsfwMode ?? this.getNsfwMode();
    const generation = ++this.loadGeneration;

    this.activeAbortController?.abort();
    const abortController = new AbortController();
    this.activeAbortController = abortController;
    this.errorMessage = null;
    if (showLoading) this.loadState = "loading";

    try {
      const entity = await this.loadEntity({ nsfwMode, signal: abortController.signal });
      if (generation !== this.loadGeneration || abortController.signal.aborted) return;
      this.entity = entity;
      this.loadState = "ready";
    } catch (error) {
      if (generation !== this.loadGeneration || abortController.signal.aborted) return;
      if (redirectHiddenEntityNotFound(error, nsfwMode)) return;
      this.errorMessage = error instanceof Error ? error.message : String(error);
      if (showLoading || this.entity === null) this.loadState = "error";
    } finally {
      if (generation === this.loadGeneration) this.activeAbortController = null;
    }
  };

  /** Retries a failed initial load with the shared loading state. */
  retry = (): Promise<void> => this.reload({ showLoading: true });

  /** Applies and persists a rating while preventing overlapping rating writes. */
  changeRating = async (value: number | null): Promise<void> => {
    const entity = this.entity;
    if (!entity || this.ratingBusy) return;

    this.ratingBusy = true;
    try {
      await updateOptimisticEntityRating(
        entity,
        value,
        (next) => this.setEntityIfCurrent(entity.id, next),
        this.mutations.rating,
      );
    } finally {
      this.ratingBusy = false;
    }
  };

  /** Toggles the entity's favorite flag optimistically. */
  toggleFavorite = (): Promise<void> => this.toggleFlag("isFavorite");

  /** Toggles the entity's organized flag optimistically. */
  toggleOrganized = (): Promise<void> => this.toggleFlag("isOrganized");

  /** Persists shared metadata through the root entity route, then refreshes detail data. */
  saveMetadata = async (request: EntityMetadataUpdateRequest): Promise<void> => {
    const entity = this.entity;
    if (!entity) return;

    await this.mutations.metadata(entity.id, request);
    if (this.entity?.id === entity.id) await this.reload({ showLoading: false });
  };

  /** Cancels in-flight route work when the page is destroyed. */
  dispose = (): void => {
    this.loadGeneration += 1;
    this.activeAbortController?.abort();
    this.activeAbortController = null;
  };

  private toggleFlag = async (flag: "isFavorite" | "isOrganized"): Promise<void> => {
    const entity = this.entity;
    if (!entity) return;
    await toggleOptimisticEntityFlag(
      entity,
      flag,
      (next) => this.setEntityIfCurrent(entity.id, next),
      this.mutations.flags,
    );
  };

  private setEntityIfCurrent(entityId: string, next: T): void {
    if (this.entity?.id === entityId) this.entity = next;
  }
}

/**
 * Connects an entity detail controller to the page's NSFW and application-chrome contexts.
 */
export function useEntityDetailPage<T extends EntityDetailPageEntity>(
  options: EntityDetailPageOptions<T>,
): EntityDetailPageController<T> {
  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const controller = new EntityDetailPageController(options.load, () => nsfw.mode, options.mutations);
  let initialized = false;
  let previousKey = "";
  let previousNsfwMode = nsfw.mode;

  $effect(() => {
    const key = options.loadKey();
    const nsfwMode = nsfw.mode;
    const keyChanged = initialized && key !== previousKey;
    const nsfwChanged = initialized && nsfwMode !== previousNsfwMode;

    const reloadForNsfw = options.reloadOnNsfwChange !== false && nsfwChanged;
    if (initialized && !keyChanged && !reloadForNsfw) return;

    const showLoading = !initialized || keyChanged;
    initialized = true;
    previousKey = key;
    previousNsfwMode = nsfwMode;
    untrack(() => void controller.reload({ nsfwMode, showLoading }));
  });

  $effect(() => {
    const entity = controller.entity;
    if (!entity) return;
    return appChrome.setBreadcrumbs(options.breadcrumbs(entity));
  });

  $effect(() => controller.dispose);

  return controller;
}
