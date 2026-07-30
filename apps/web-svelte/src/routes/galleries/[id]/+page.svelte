<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, Layers, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityRating } from "$lib/api/entity-mutations";
  import {
    getCapability,
    getGalleryMetadataCapability,
    getImagesCapability,
    getRatingValue,
    isNsfw as hasNsfwFlag,
    withRatingCapability,
  } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { getAllChildIds } from "$lib/entities/entity-children";
  import type { EntityDetailCredit, EntityDetailTag } from "$lib/entities/entity-detail";
  import type { EntityKindCode } from "$lib/entities/entity-codes";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import ImageLightboxDetails from "$lib/components/ImageLightboxDetails.svelte";
  import UniversalLightbox from "$lib/components/UniversalLightbox.svelte";
  import {
    lightboxEntityFromCard,
    type UniversalLightboxEntity,
  } from "$lib/components/universal-lightbox-media";

  let childCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let lightboxOpen = $state(false);
  let lightboxCards = $state.raw<EntityThumbnailCard[]>([]);
  let lightboxIndex = $state(0);
  let hydratedLightboxEntities = $state.raw<Record<string, UniversalLightboxEntity>>({});
  let lightboxHydrationInFlight = $state.raw<string[]>([]);
  const currentGalleryId = $derived(page.params.id ?? "");

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => currentGalleryId,
    load: async ({ signal }) => {
      const nextGallery = await fetchEntity(currentGalleryId, { signal });
      const [children, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(getAllChildIds(nextGallery), { signal }),
        hydrateStandardRelationshipCards(nextGallery, { signal }),
      ]);
      signal.throwIfAborted();

      childCards = thumbnailsToCards(children, {
        hrefFor: (thumbnail) => resolveEntityHref(thumbnail.kind, thumbnail.id),
      });
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      return nextGallery;
    },
    breadcrumbs: (nextGallery) => [
      { label: "Galleries", href: "/galleries" },
      { label: nextGallery.title },
    ],
  });

  const gallery = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!gallery) return null;
    return {
      ...entityCardToDetailCard(gallery),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const primaryStudio = $derived(relationshipStudio);
  const galleryMetadata = $derived(
    gallery ? getGalleryMetadataCapability(gallery.capabilities) : undefined,
  );

  const dates = $derived(card?.dates ?? []);
  const acq = useEntityAcquisition({
    entityId: () => gallery?.id,
    capabilities: () => gallery?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/galleries"),
  });
  const fileManagement = {
    onDeleted: () => goto("/galleries"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    {
      id: "details",
      label: "Details",
      icon: Info,
      sections: ["description", "tags", "studio", "credits"],
    },
    {
      id: "metadata",
      label: "Metadata",
      icon: SlidersHorizontal,
      sections: ["stats", "dates", "classification", "technical", "source", "links"],
      layout: "grid",
    },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

  const imageChildren = $derived(childCards.filter((c) => c.entity.kind === "image"));
  const galleryChildren = $derived(childCards.filter((c) => c.entity.kind === "gallery"));
  const lightboxEntities = $derived(
    lightboxCards.map((c) => hydratedLightboxEntities[c.entity.id] ?? lightboxEntityFromCard(c)),
  );

  $effect(() => {
    if (!lightboxOpen || lightboxCards.length === 0) return;
    const currentCard = lightboxCards[lightboxIndex];
    if (!currentCard || currentCard.entity.kind !== "image") return;
    if (hydratedLightboxEntities[currentCard.entity.id]) return;
    void hydrateLightboxEntity(currentCard.entity.id);
  });

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

  function lightboxEntityFromEntity(entity: EntityCardFull): UniversalLightboxEntity {
    const rating = getRatingValue(entity.capabilities);
    return {
      id: entity.id,
      kind: entity.kind,
      title: entity.title,
      capabilities: entity.capabilities,
      coverUrl: getImagesCapability(entity.capabilities)?.coverUrl ?? null,
      isNsfw: hasNsfwFlag(entity.capabilities),
      rating: rating > 0 ? rating : null,
    };
  }

  async function hydrateLightboxEntity(entityId: string) {
    if (lightboxHydrationInFlight.includes(entityId)) return;
    lightboxHydrationInFlight = [...lightboxHydrationInFlight, entityId];
    try {
      const image = await fetchEntity(entityId);
      hydratedLightboxEntities = {
        ...hydratedLightboxEntities,
        [entityId]: lightboxEntityFromEntity(image),
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
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load gallery."
    onRetry={detail.retry}
  >
  {#if card && gallery}
    <EntityDetail
      {card}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      peopleLabel="People"
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if primaryStudio}
          <a href={resolveEntityHref(primaryStudio.kind as EntityKindCode, primaryStudio.id)} class="meta-item is-studio">{primaryStudio.title}</a>
        {/if}
        {#if galleryMetadata?.galleryType}
          {#if primaryStudio}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{galleryMetadata.galleryType}</span>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(primaryStudio || galleryMetadata?.galleryType)} />
        {#if childCards.length > 0}
          <span class="meta-sep"></span>
          <span class="meta-item">{childCards.length} {childCards.length === 1 ? "item" : "items"}</span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if galleryMetadata?.galleryType}
          <span class="hero-badge">{galleryMetadata.galleryType}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard {acq} entity={gallery} {fileManagement} />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if galleryChildren.length > 0}
      <EntityGridSection
        title="Sub Galleries"
        count={galleryChildren.length}
        icon={Layers}
        prefsKey={`gallery-${gallery?.id}-children-section`}
      >
        <EntityGrid
          cards={galleryChildren}
          prefsKey={`gallery-${gallery?.id}-children`}
          emptyTitle="No sub-galleries"
          emptyMessage="This gallery has no sub-galleries."
        />
      </EntityGridSection>
    {/if}

    {#if imageChildren.length > 0}
      <EntityGridSection
        title="Images"
        count={imageChildren.length}
        prefsKey={`gallery-${gallery?.id}-images-section`}
      >
        <EntityGrid
          cards={imageChildren}
          prefsKey={`gallery-${gallery?.id}-images`}
          initialMediaWall
          enableFeedView
          onCardActivate={openImageLightbox}
          emptyTitle="No images"
          emptyMessage="This gallery has no images."
        />
      </EntityGridSection>
    {/if}

    {#if childCards.length === 0}
      <div class="empty-children">
        <p>No images or sub-galleries in this gallery yet.</p>
      </div>
    {/if}
  {/if}
  </EntityDetailPageState>
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

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c7c9cc); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }


  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

</style>
