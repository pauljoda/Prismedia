<script lang="ts">
  import type { Component } from "svelte";
  import { onMount } from "svelte";
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
  import { EntityIndexPageState } from "./entity-index-page.svelte.ts";
  import type { EntityGridRequest } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { updateEntityRating } from "$lib/api/entity-mutations";
  import { fetchImage, type ImageDetail } from "$lib/api/media";
  import type { EntityCard } from "$lib/api/entities";

  interface Props {
    actionHref?: string;
    actionIcon?: Component;
    actionLabel?: string;
    description: string;
    emptyMessage: string;
    emptyTitle: string;
    enableLightbox?: boolean;
    errorMessage?: string;
    icon: Component;
    initialMediaWall?: boolean;
    kind: string;
    lightboxTitle?: string;
    prefsKey: string;
    resolveHref?: (item: EntityCard) => string | undefined;
    title: string;
  }

  type UniversalLightboxComponent = typeof import("$lib/components/UniversalLightbox.svelte").default;
  type ImageLightboxDetailsComponent = typeof import("$lib/components/ImageLightboxDetails.svelte").default;

  let {
    actionHref,
    actionIcon: ActionIcon,
    actionLabel,
    description,
    emptyMessage,
    emptyTitle,
    enableLightbox = false,
    errorMessage,
    icon: Icon,
    initialMediaWall = false,
    kind,
    prefsKey,
    resolveHref,
    title,
    lightboxTitle = title,
  }: Props = $props();

  const nsfw = useNsfw();
  const page = new EntityIndexPageState({
    getKind: () => kind,
    getHideNsfw: () => nsfw.mode === "off",
    resolveHref: (item) => resolveHref?.(item),
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
    void page.loadInitial();
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
    if (!currentCard || currentCard.entity.kind !== "image") return;
    if (hydratedLightboxEntities[currentCard.entity.id]) return;
    void hydrateLightboxEntity(currentCard.entity.id);
  });

  function handleRequestChange(request: EntityGridRequest) {
    page.setQuery(request.query ?? "");
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

  async function hydrateLightboxEntity(entityId: string) {
    if (lightboxHydrationInFlight.includes(entityId)) return;
    lightboxHydrationInFlight = [...lightboxHydrationInFlight, entityId];
    try {
      const image = await fetchImage(entityId);
      hydratedLightboxEntities = {
        ...hydratedLightboxEntities,
        [entityId]: lightboxEntityFromImageDetail(image),
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
      <span class="page-head-eyebrow" aria-hidden="true">
        <span class="eyebrow-tick"></span>
        LIBRARY · {kind.toUpperCase()}
      </span>
      <h1 class="page-head-title">
        <Icon class="h-5 w-5 text-text-accent page-head-icon" />
        {title}
      </h1>
      <p class="page-head-description">
        {description}
      </p>
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
      {prefsKey}
      {emptyTitle}
      {emptyMessage}
      {initialMediaWall}
      initialPageSize={page.pageSize}
      hasMore={page.nextCursor !== null}
      loadingMore={page.loadingMore}
      loadMoreError={page.loadMoreError}
      {remoteTotalCount}
      onCardActivate={enableLightbox ? handleCardActivate : undefined}
      onPageSizeChange={(size) => page.setPageSize(size)}
      onLoadMore={() => page.loadMore()}
      onRequestChange={handleRequestChange}
    />
  {/if}
</section>

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
    flex-direction: column;
    gap: 0.28rem;
  }

  .page-head-eyebrow {
    display: inline-flex;
    align-items: center;
    gap: 0.55rem;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.56rem;
    font-weight: 600;
    letter-spacing: 0.22em;
  }

  .eyebrow-tick {
    display: inline-block;
    width: 0.4rem;
    height: 0.4rem;
    background: rgb(196 154 90);
    box-shadow: 0 0 8px rgb(196 154 90 / 0.6);
    border-radius: 0;
    animation: tick-pulse 3.2s ease-in-out infinite;
  }

  @keyframes tick-pulse {
    0%, 100% {
      box-shadow: 0 0 8px rgb(196 154 90 / 0.6);
      opacity: 1;
    }
    50% {
      box-shadow: 0 0 14px rgb(196 154 90 / 0.85);
      opacity: 0.75;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .eyebrow-tick {
      animation: none;
    }
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

  .page-head-description {
    margin: 0;
    color: var(--color-text-muted);
    font-size: 0.82rem;
    line-height: 1.45;
    max-width: 60ch;
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
