<script lang="ts">
  import type { Component } from "svelte";
  import { onMount, untrack } from "svelte";
  import { Plus } from "@lucide/svelte";
  import { goto } from "$app/navigation";
  import {
    getImagesCapability,
    getRatingValue,
    isNsfw as hasNsfwFlag,
    withRatingCapability,
  } from "$lib/api/capabilities";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    lightboxEntityFromCard,
    type UniversalLightboxEntity,
  } from "$lib/components/universal-lightbox-media";
  import EntityGrid from "./EntityGrid.svelte";
  import NameInputDialog from "./NameInputDialog.svelte";
  import ConfirmDialog from "./ConfirmDialog.svelte";
  import { EntityIndexPageState } from "./entity-index-page.svelte.ts";
  import type {
    EntityGridBulkAction,
    EntityGridRequest,
    EntityGridServerQuery,
  } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { updateEntityRating } from "$lib/api/entity-mutations";
  import {
    createTaxonomyEntity,
    deleteTaxonomyEntity,
    isManageableTaxonomyKind,
  } from "$lib/api/taxonomy";
  import { bulkDeleteMediaEntities, isDeletableMediaKind } from "$lib/api/entity-deletion";
  import { fetchImage, fetchVideo, type ImageDetail, type VideoDetail } from "$lib/api/media";
  import type { EntityCard } from "$lib/api/entities";

  interface Props {
    actionHref?: string;
    actionIcon?: Component;
    actionLabel?: string;
    emptyMessage: string;
    emptyTitle: string;
    enableFeedView?: boolean;
    enableLightbox?: boolean;
    errorMessage?: string;
    icon: Component;
    initialMediaWall?: boolean;
    kind: string;
    lightboxTitle?: string;
    /**
     * Server filter parameters that always apply, regardless of the grid's filter
     * controls. Used by constrained sub-views such as Comics/eBooks to lock the
     * book type/format. Spread after the user's filters so the lock always wins.
     */
    lockedServerQuery?: Partial<EntityGridServerQuery>;
    /** Hide the book type/format filter chips (for routes that already lock those filters). */
    lockBookFilters?: boolean;
    prefsKey: string;
    resolveHref?: (item: EntityCard) => string | undefined;
    title: string;
    /**
     * Singular noun for the entity, used in the add button and delete confirmation (e.g. "tag").
     * Required to expose create/delete management for the user-managed taxonomy kinds.
     */
    itemNoun?: string;
  }

  type UniversalLightboxComponent = typeof import("$lib/components/UniversalLightbox.svelte").default;
  type ImageLightboxDetailsComponent = typeof import("$lib/components/ImageLightboxDetails.svelte").default;

  let {
    actionHref,
    actionIcon: ActionIcon,
    actionLabel,
    emptyMessage,
    emptyTitle,
    enableFeedView = false,
    enableLightbox = false,
    errorMessage,
    icon: Icon,
    initialMediaWall = false,
    kind,
    lockedServerQuery,
    lockBookFilters = false,
    prefsKey,
    resolveHref,
    title,
    lightboxTitle = title,
    itemNoun,
  }: Props = $props();

  const nsfw = useNsfw();
  // Create/delete management is offered only for the user-managed taxonomy kinds and when the
  // page provides a singular noun for the labels.
  const canManage = $derived(isManageableTaxonomyKind(kind) && Boolean(itemNoun));
  // File-backed media kinds get a permanent delete-with-files bulk action instead.
  const canDeleteMedia = $derived(!canManage && isDeletableMediaKind(kind));
  const noun = $derived(itemNoun ?? "item");

  let createOpen = $state(false);
  let confirmDeleteOpen = $state(false);
  let pendingDeleteIds = $state<string[]>([]);

  const bulkActions = $derived<EntityGridBulkAction[]>(
    canManage || canDeleteMedia
      ? [
          {
            id: "delete",
            label: canManage ? `Delete ${noun}` : "Delete files",
            tone: "danger",
            isAvailable: canDeleteMedia
              ? (selectedIds) => selectedIds.length > 0 && selectedIds.every((id) =>
                  page.cards.some((card) => card.entity.id === id && card.hasSourceMedia))
              : undefined,
            onRun: (selectedIds) => {
              pendingDeleteIds = selectedIds;
              confirmDeleteOpen = true;
            },
          },
        ]
      : [],
  );

  async function handleCreate(name: string) {
    if (!isManageableTaxonomyKind(kind)) return;
    const { id } = await createTaxonomyEntity(kind, { title: name, isNsfw: false });
    const href = resolveEntityHref(kind, id);
    if (href) {
      await goto(href);
    } else {
      await page.loadInitial();
    }
  }

  async function handleConfirmDelete() {
    const ids = pendingDeleteIds;
    let failureMessage: string | null = null;
    if (isManageableTaxonomyKind(kind)) {
      await Promise.all(ids.map((id) => deleteTaxonomyEntity(kind, id)));
    } else if (isDeletableMediaKind(kind)) {
      const result = await bulkDeleteMediaEntities(ids, true);
      if (result.failures.length > 0) {
        failureMessage = result.failures.map((failure) => failure.message).join(" · ");
      }
    } else {
      return;
    }
    pendingDeleteIds = [];
    await page.loadInitial();
    if (failureMessage) {
      throw new Error(failureMessage);
    }
  }
  const page = new EntityIndexPageState({
    getKind: () => kind,
    getHideNsfw: () => nsfw.mode === "off",
    resolveHref: (item) => resolveHref?.(item),
    // Static per-route literal; capture once at construction.
    lockedServerQuery: untrack(() => lockedServerQuery),
  });

  let lastNsfwMode = $state(nsfw.mode);
  let lightboxCards = $state.raw<EntityThumbnailCard[]>([]);
  let lightboxIndex = $state(0);
  let hydratedLightboxEntities = $state.raw<Record<string, UniversalLightboxEntity>>({});
  let ImageLightboxDetailsLazy = $state<ImageLightboxDetailsComponent | null>(null);
  let UniversalLightboxLazy = $state<UniversalLightboxComponent | null>(null);
  let remoteTotalCount = $derived(page.totalCount);
  const lightboxEntities = $derived(
    lightboxCards.map((card) => hydratedLightboxEntities[card.entity.id] ?? lightboxEntityFromCard(card)),
  );
  let lightboxHydrationInFlight = $state.raw<string[]>([]);
  let lightboxComponentLoad: Promise<void> | null = null;

  onMount(() => {
    void page.ensureLoaded();
  });

  $effect(() => {
    if (nsfw.mode !== lastNsfwMode) {
      lastNsfwMode = nsfw.mode;
      void page.loadInitial();
    }
  });

  $effect(() => {
    if (!enableLightbox || lightboxCards.length === 0) return;
    const currentCard = lightboxCards[lightboxIndex];
    if (!currentCard || (currentCard.entity.kind !== "image" && currentCard.entity.kind !== "video")) return;
    if (hydratedLightboxEntities[currentCard.entity.id]) return;
    void hydrateLightboxEntity(currentCard.entity.id);
  });

  function handleRequestChange(request: EntityGridRequest) {
    page.setQuery(request.query ?? "");
    page.setServerQuery(request.server);
  }

  function handleCardActivate(card: EntityThumbnailCard, visibleCards: EntityThumbnailCard[]) {
    if (!enableLightbox) return;

    void ensureLightboxComponents();

    const nextCards = visibleCards.length > 0 ? visibleCards : [card];
    const nextIndex = nextCards.findIndex((candidate) => candidate.entity.id === card.entity.id);

    lightboxCards = nextCards;
    lightboxIndex = Math.max(0, nextIndex);
  }

  async function ensureLightboxComponents() {
    if (UniversalLightboxLazy && ImageLightboxDetailsLazy) return;
    lightboxComponentLoad ??= Promise.all([
      import("$lib/components/UniversalLightbox.svelte"),
      import("$lib/components/ImageLightboxDetails.svelte"),
    ]).then(([lightbox, details]) => {
      UniversalLightboxLazy = lightbox.default;
      ImageLightboxDetailsLazy = details.default;
    });

    await lightboxComponentLoad;
  }

  function closeLightbox() {
    lightboxCards = [];
    lightboxIndex = 0;
    hydratedLightboxEntities = {};
  }

  function updateLightboxCardRating(entityId: string, rating: number | null) {
    lightboxCards = lightboxCards.map((card) =>
      card.entity.id === entityId
        ? {
            ...card,
            entity: {
              ...card.entity,
              capabilities: withRatingCapability(card.entity.capabilities, rating),
            },
          }
        : card,
    );

    const hydrated = hydratedLightboxEntities[entityId];
    if (hydrated) {
      hydratedLightboxEntities = {
        ...hydratedLightboxEntities,
        [entityId]: {
          ...hydrated,
          capabilities: withRatingCapability(hydrated.capabilities, rating),
          rating,
        },
      };
    }
  }

  async function handleLightboxRatingChange(entityId: string, rating: number | null) {
    updateLightboxCardRating(entityId, rating);
    await updateEntityRating(entityId, rating);
  }

  function lightboxEntityFromImageDetail(image: ImageDetail): UniversalLightboxEntity {
    const rating = getRatingValue(image.capabilities);
    return {
      id: image.id,
      kind: image.kind,
      title: image.title,
      capabilities: image.capabilities,
      coverUrl: getImagesCapability(image.capabilities)?.coverUrl ?? null,
      isNsfw: hasNsfwFlag(image.capabilities),
      rating: rating > 0 ? rating : null,
    };
  }

  function lightboxEntityFromVideoDetail(video: VideoDetail): UniversalLightboxEntity {
    const rating = getRatingValue(video.capabilities);
    return {
      id: video.id,
      kind: video.kind,
      title: video.title,
      capabilities: video.capabilities,
      coverUrl: getImagesCapability(video.capabilities)?.coverUrl ?? null,
      isNsfw: hasNsfwFlag(video.capabilities),
      rating: rating > 0 ? rating : null,
    };
  }

  async function hydrateLightboxEntity(entityId: string) {
    if (lightboxHydrationInFlight.includes(entityId)) return;
    const currentCard = lightboxCards[lightboxIndex];
    const kind = currentCard?.entity.kind;
    if (kind !== "image" && kind !== "video") return;

    lightboxHydrationInFlight = [...lightboxHydrationInFlight, entityId];
    try {
      const entity = kind === "video"
        ? lightboxEntityFromVideoDetail(await fetchVideo(entityId))
        : lightboxEntityFromImageDetail(await fetchImage(entityId));
      hydratedLightboxEntities = {
        ...hydratedLightboxEntities,
        [entityId]: entity,
      };
    } finally {
      lightboxHydrationInFlight = lightboxHydrationInFlight.filter((id) => id !== entityId);
    }
  }
</script>

<svelte:head>
  <title>{title} · Prismedia</title>
</svelte:head>

<section class="space-y-5">
  <header class="page-head">
    <div class="page-head-meta">
      <h1 class="page-head-title">
        <Icon class="h-5 w-5 text-text-accent page-head-icon" />
        {title}
      </h1>
    </div>

    {#if actionHref && actionLabel}
      <a
        href={actionHref}
        class="page-head-action"
      >
        {#if ActionIcon}
          <ActionIcon class="h-4 w-4" />
        {/if}
        <span>{actionLabel}</span>
      </a>
    {:else if canManage}
      <button type="button" class="page-head-action" onclick={() => (createOpen = true)}>
        <Plus class="h-4 w-4" />
        <span>Add {noun}</span>
      </button>
    {/if}
  </header>

  {#if page.loadState === "error"}
    <div class="surface-card-sharp flex items-center justify-between gap-4 border-error-500/50 p-4">
      <p class="text-sm text-text-muted">{page.errorMessage ?? errorMessage ?? `Failed to load ${title.toLowerCase()}.`}</p>
      <button
        type="button"
        class="surface-well px-3 py-1 text-body-sm text-text-muted transition-colors hover:text-text-primary"
        onclick={() => void page.loadInitial()}
      >
        Retry
      </button>
    </div>
  {:else}
    <EntityGrid
      cards={page.cards}
      loading={page.loadState === "loading"}
      entityKind={kind}
      {prefsKey}
      {lockBookFilters}
      {enableFeedView}
      {emptyTitle}
      {emptyMessage}
      {initialMediaWall}
      initialSortBy={isManageableTaxonomyKind(kind) ? "references" : "added"}
      initialSortDir="desc"
      initialPageSize={page.pageSize}
      hasMore={page.nextCursor !== null}
      loadingMore={page.loadingMore}
      loadMoreError={page.loadMoreError}
      {remoteTotalCount}
      bulkActions={canManage || canDeleteMedia ? bulkActions : undefined}
      onCardActivate={enableLightbox ? handleCardActivate : undefined}
      onPageSizeChange={(size) => page.setPageSize(size)}
      onLoadMore={() => page.loadMore()}
      onRequestChange={handleRequestChange}
    />
  {/if}
</section>

{#if canManage}
  <NameInputDialog
    open={createOpen}
    title={`New ${noun}`}
    placeholder={`${noun.charAt(0).toUpperCase()}${noun.slice(1)} name`}
    confirmLabel="Create"
    onConfirm={handleCreate}
    onClose={() => (createOpen = false)}
  />
  <ConfirmDialog
    open={confirmDeleteOpen}
    title={`Delete ${pendingDeleteIds.length === 1 ? noun : `${pendingDeleteIds.length} ${noun}s`}?`}
    message={`This removes ${pendingDeleteIds.length === 1 ? `the ${noun}` : `these ${noun}s`} and detaches ${pendingDeleteIds.length === 1 ? "it" : "them"} from any media. This cannot be undone.`}
    confirmLabel="Delete"
    danger
    onConfirm={handleConfirmDelete}
    onClose={() => (confirmDeleteOpen = false)}
  />
{:else if canDeleteMedia}
  <ConfirmDialog
    open={confirmDeleteOpen}
    title={`Delete the files for ${pendingDeleteIds.length === 1 ? "this item" : `${pendingDeleteIds.length} items`}?`}
    message={`This permanently deletes ${pendingDeleteIds.length === 1 ? "its" : "their"} files from disk — including structural children — and cannot be undone. Directly monitored Entities go back to Wanted and are searched again; unmonitored branches are removed. Parent monitoring alone never overrides a child you turned off.`}
    confirmLabel="Delete files"
    danger
    onConfirm={handleConfirmDelete}
    onClose={() => (confirmDeleteOpen = false)}
  />
{/if}

{#if enableLightbox && lightboxEntities.length > 0 && UniversalLightboxLazy && ImageLightboxDetailsLazy}
  <UniversalLightboxLazy
    entities={lightboxEntities}
    initialIndex={lightboxIndex}
    onClose={closeLightbox}
    onIndexChange={(index: number) => (lightboxIndex = index)}
    onRatingChange={(entityId: string, rating: number | null) => void handleLightboxRatingChange(entityId, rating)}
    sharedKey={`index-${lightboxTitle}`}
  >
    {#snippet detailsContent(entity: UniversalLightboxEntity)}
      <ImageLightboxDetailsLazy {entity} onRatingChange={updateLightboxCardRating} />
    {/snippet}
  </UniversalLightboxLazy>
{/if}

<style>
  .page-head {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: 1rem;
    padding-bottom: 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle);
    position: relative;
  }

  .page-head::after {
    content: "";
    position: absolute;
    left: 0;
    bottom: -1px;
    width: 5rem;
    height: 1px;
    background: linear-gradient(
      to right,
      rgb(221 180 119) 0%,
      rgb(196 154 90 / 0.4) 70%,
      transparent
    );
    box-shadow: 0 0 8px rgb(196 154 90 / 0.45);
  }

  .page-head-meta {
    min-width: 0;
    display: flex;
    align-items: center;
  }

  .page-head-title {
    display: inline-flex;
    align-items: center;
    gap: 0.6rem;
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.55rem;
    font-weight: 600;
    letter-spacing: -0.025em;
    line-height: 1.05;
  }

  .page-head-title :global(.page-head-icon) {
    filter: drop-shadow(0 0 10px rgb(196 154 90 / 0.4));
  }

  .page-head-action {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    height: 2.4rem;
    padding: 0 1rem;
    border-radius: var(--radius-sm);
    border: 1px solid rgb(244 220 170 / 0.5);
    background: linear-gradient(180deg, #ddb477, #a07a3e);
    color: #1a1408;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    text-decoration: none;
    box-shadow:
      inset 0 1px 0 rgb(255 255 255 / 0.35),
      inset 0 -1px 0 rgb(0 0 0 / 0.25),
      0 0 18px rgb(196 154 90 / 0.18),
      0 2px 6px rgb(0 0 0 / 0.4);
    transition:
      background var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default),
      transform var(--duration-fast) var(--ease-mechanical);
  }

  .page-head-action:hover {
    background: linear-gradient(180deg, #e8c189, #b48a47);
    color: #1a1408;
    box-shadow:
      inset 0 1px 0 rgb(255 255 255 / 0.4),
      inset 0 -1px 0 rgb(0 0 0 / 0.25),
      0 0 28px rgb(196 154 90 / 0.32),
      0 2px 8px rgb(0 0 0 / 0.45);
  }

  .page-head-action:active {
    transform: translateY(1px);
  }
</style>
