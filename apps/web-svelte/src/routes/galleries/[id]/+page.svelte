<script lang="ts">
  import { page } from "$app/state";
  import { Layers } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchImage, fetchGallery, type GalleryDetail, type ImageDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import {
    getCapability,
    getImagesCapability,
    getRatingValue,
    isNsfw as hasNsfwFlag,
    withRatingCapability,
  } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { getAllChildIds } from "$lib/entities/entity-children";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import type { EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import ImageLightboxDetails from "$lib/components/ImageLightboxDetails.svelte";
  import UniversalLightbox from "$lib/components/UniversalLightbox.svelte";
  import {
    lightboxEntityFromCard,
    type UniversalLightboxEntity,
  } from "$lib/components/universal-lightbox-media";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let gallery = $state<GalleryDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let ratingBusy = $state(false);
  let childCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let lightboxOpen = $state(false);
  let lightboxCards = $state.raw<EntityThumbnailCard[]>([]);
  let lightboxIndex = $state(0);
  let hydratedLightboxEntities = $state.raw<Record<string, UniversalLightboxEntity>>({});
  let lightboxHydrationInFlight = $state.raw<string[]>([]);
  let activeLoadToken = 0;
  const currentGalleryId = $derived(page.params.id ?? "");

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!gallery) return null;
    return {
      ...entityCardToDetailCard(gallery),
      tags: relationshipTags,
    };
  });

  const primaryStudio = $derived(studioCards[0]?.entity ?? null);

  const dates = $derived.by(() => {
    if (!gallery) return [];
    const cap = getCapability(gallery.capabilities, "dates");
    return cap?.items ?? [];
  });

  const imageChildren = $derived(childCards.filter((c) => c.entity.kind === "image"));
  const galleryChildren = $derived(childCards.filter((c) => c.entity.kind === "gallery"));
  const lightboxEntities = $derived(
    lightboxCards.map((c) => hydratedLightboxEntities[c.entity.id] ?? lightboxEntityFromCard(c)),
  );

  $effect(() => {
    const currentNsfwMode = nsfw.mode;
    if (!currentGalleryId) return;
    void loadGallery(currentGalleryId, currentNsfwMode);
  });

  $effect(() => {
    if (!gallery) return;
    return appChrome.setBreadcrumbs([
      { label: "Galleries", href: "/galleries" },
      { label: gallery.title },
    ]);
  });

  $effect(() => {
    if (!lightboxOpen || lightboxCards.length === 0) return;
    const currentCard = lightboxCards[lightboxIndex];
    if (!currentCard || currentCard.entity.kind !== "image") return;
    if (hydratedLightboxEntities[currentCard.entity.id]) return;
    void hydrateLightboxEntity(currentCard.entity.id);
  });

  async function loadGallery(galleryId = currentGalleryId, nsfwMode = nsfw.mode) {
    const loadToken = activeLoadToken + 1;
    activeLoadToken = loadToken;
    loadState = "loading";
    errorMessage = null;
    try {
      const nextGallery = await fetchGallery(galleryId);
      if (loadToken !== activeLoadToken) return;
      gallery = nextGallery;
      await hydrateGalleryThumbnails(nextGallery);
      if (loadToken !== activeLoadToken) return;
      loadState = "ready";
    } catch (err) {
      if (loadToken !== activeLoadToken) return;
      if (redirectHiddenEntityNotFound(err, nsfwMode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function hydrateGalleryThumbnails(nextGallery: GalleryDetail) {
    const [children, relationships] = await Promise.all([
      fetchOrderedEntityThumbnails(getAllChildIds(nextGallery)),
      hydrateStandardRelationshipCards(nextGallery),
    ]);
    childCards = thumbnailsToCards(children, {
      hrefFor: (thumbnail) => resolveEntityHref(thumbnail.kind, thumbnail.id),
    });
    studioCards = relationships.studioCards;
    creditCards = relationships.creditCards;
    relationshipTags = relationships.relationshipTags;
  }

  async function handleRatingChange(value: number | null) {
    if (!gallery || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(gallery, value, (next) => (gallery = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!gallery) return;
    await toggleOptimisticEntityFlag(gallery, "isFavorite", (next) => (gallery = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!gallery) return;
    await toggleOptimisticEntityFlag(gallery, "isOrganized", (next) => (gallery = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!gallery) return;
    await updateEntityMetadata(gallery.id, request, { kind: gallery.kind });
    await loadGallery();
  }

  function openImageLightbox(card: EntityThumbnailCard, visibleCards: EntityThumbnailCard[]) {
    const nextCards = visibleCards.length > 0 ? visibleCards : [card];
    const index = nextCards.findIndex((c) => c.entity.id === card.entity.id);
    lightboxCards = nextCards;
    lightboxIndex = Math.max(0, index);
    lightboxOpen = true;
  }

  function closeLightbox() {
    lightboxOpen = false;
    lightboxCards = [];
    lightboxIndex = 0;
    hydratedLightboxEntities = {};
  }

  function updateLightboxCardRating(entityId: string, value: number | null) {
    childCards = childCards.map((childCard) =>
      childCard.entity.id === entityId
        ? {
            ...childCard,
            entity: {
              ...childCard.entity,
              capabilities: withRatingCapability(childCard.entity.capabilities, value),
            },
          }
        : childCard,
    );
    lightboxCards = lightboxCards.map((lightboxCard) =>
      lightboxCard.entity.id === entityId
        ? {
            ...lightboxCard,
            entity: {
              ...lightboxCard.entity,
              capabilities: withRatingCapability(lightboxCard.entity.capabilities, value),
            },
          }
        : lightboxCard,
    );

    const hydrated = hydratedLightboxEntities[entityId];
    if (hydrated) {
      hydratedLightboxEntities = {
        ...hydratedLightboxEntities,
        [entityId]: {
          ...hydrated,
          capabilities: withRatingCapability(hydrated.capabilities, value),
          rating: value,
        },
      };
    }
  }

  async function handleLightboxRatingChange(entityId: string, value: number | null) {
    updateLightboxCardRating(entityId, value);
    await updateEntityRating(entityId, value);
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
  <title>{gallery?.title ?? "Gallery"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load gallery."}</p>
      <button type="button" onclick={() => void loadGallery()}>Retry</button>
    </div>
  {:else if card && gallery}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      peopleLabel="People"
      posterSize="large"
    >
      {#snippet heroMeta()}
        {#if primaryStudio}
          <a href={resolveEntityHref(primaryStudio.kind, primaryStudio.id)} class="meta-item is-studio">{primaryStudio.title}</a>
        {/if}
        {#if gallery?.galleryType}
          {#if primaryStudio}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{gallery.galleryType}</span>
        {/if}
        {#each dates as date, i (date.code)}
          <span class="meta-sep"></span>
          <span class="meta-item">{date.value}</span>
        {/each}
        {#if childCards.length > 0}
          <span class="meta-sep"></span>
          <span class="meta-item">{childCards.length} {childCards.length === 1 ? "item" : "items"}</span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if gallery?.galleryType}
          <span class="hero-badge">{gallery.galleryType}</span>
        {/if}
      {/snippet}

      {#snippet afterBody()}
        {#if studioCards.length > 0 || creditCards.length > 0}
          <div class="credits-section">
            <EntityCastAndCrewSection {studioCards} {creditCards} castLabel="People" />
          </div>
        {/if}
      {/snippet}
    </EntityDetail>

    {#if galleryChildren.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Layers class="h-4 w-4" />
          Sub-Galleries
          <span class="content-count">{galleryChildren.length}</span>
        </h2>
        <EntityGrid
          cards={galleryChildren}
          prefsKey={`gallery-${gallery?.id}-children`}
          selectable={false}
          emptyTitle="No sub-galleries"
          emptyMessage="This gallery has no sub-galleries."
        />
      </section>
    {/if}

    {#if imageChildren.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          Images
          <span class="content-count">{imageChildren.length}</span>
        </h2>
        <EntityGrid
          cards={imageChildren}
          prefsKey={`gallery-${gallery?.id}-images`}
          selectable={false}
          initialMediaWall
          enableFeedView
          onCardActivate={openImageLightbox}
          emptyTitle="No images"
          emptyMessage="This gallery has no images."
        />
      </section>
    {/if}

    {#if childCards.length === 0}
      <div class="empty-children">
        <p>No images or sub-galleries in this gallery yet.</p>
      </div>
    {/if}
  {/if}
</div>

{#if lightboxOpen && lightboxEntities.length > 0}
  <UniversalLightbox
    entities={lightboxEntities}
    initialIndex={lightboxIndex}
    onClose={closeLightbox}
    onIndexChange={(index) => (lightboxIndex = index)}
    onRatingChange={(entityId, value) => void handleLightboxRatingChange(entityId, value)}
    sharedKey={`gallery-${gallery?.id ?? "detail"}`}
  >
    {#snippet detailsContent(entity)}
      <ImageLightboxDetails {entity} onRatingChange={updateLightboxCardRating} />
    {/snippet}
  </UniversalLightbox>
{/if}

<style>
  .detail-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }


  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .error-notice button {
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.4rem 0.8rem;
    font-size: 0.78rem;
    cursor: pointer;
  }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .credits-section { padding: 1rem 1.5rem; border-top: 1px solid var(--color-border, #1c2235); }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

  @media (min-width: 640px) { .credits-section { padding: 1rem 2rem; } }
</style>
