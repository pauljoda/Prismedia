<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { onMount } from "svelte";
  import { CloudDownload, Info, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import {
    getImagesCapability,
    getRatingValue,
    isNsfw,
  } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { hydrateStandardRelationshipCards } from "$lib/entities/entity-relationship-thumbnails";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityKindCode } from "$lib/entities/entity-codes";
  import EntityDetail, {
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import UniversalLightbox from "$lib/components/UniversalLightbox.svelte";
  import type { UniversalLightboxEntity } from "$lib/components/universal-lightbox-media";
  import { EntityViewingSession } from "$lib/entities/entity-viewing-session";
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  const viewingSession = new EntityViewingSession();

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const nextImage = await fetchEntity(page.params.id ?? "", { signal });
      const relationships = await hydrateStandardRelationshipCards(nextImage);
      signal.throwIfAborted();
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      return nextImage;
    },
    breadcrumbs: (image) => [
      { label: "Images", href: "/images" },
      { label: image.title },
    ],
  });

  const image = $derived(detail.entity);

  $effect(() => {
    if (image?.id) {
      viewingSession.open(image.id, document.visibilityState === "visible");
    }
  });

  onMount(() => {
    const heartbeat = window.setInterval(() => viewingSession.heartbeat(), 15_000);
    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible") viewingSession.resume();
      else viewingSession.pause();
    };
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      window.clearInterval(heartbeat);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      viewingSession.close();
    };
  });

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!image) return null;
    return {
      ...entityCardToDetailCard(image),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const lightboxEntity = $derived.by((): UniversalLightboxEntity | null => {
    if (!image) return null;
    const rating = getRatingValue(image.capabilities);
    return {
      id: image.id,
      kind: image.kind,
      title: image.title,
      capabilities: image.capabilities,
      coverUrl: getImagesCapability(image.capabilities)?.coverUrl ?? null,
      isNsfw: isNsfw(image.capabilities),
      rating: rating > 0 ? rating : null,
    };
  });

  const lightboxEntities = $derived(lightboxEntity ? [lightboxEntity] : []);
  const studio = $derived(relationshipStudio);

  const dates = $derived(card?.dates ?? []);
  const acq = useEntityAcquisition({
    entityId: () => image?.id,
    capabilities: () => image?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/images"),
  });
  const fileManagement = {
    onDeleted: () => goto("/images"),
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

  function closeLightbox() {
    void goto("/images");
  }
</script>

<svelte:head>
  <title>{image?.title ?? "Image"} · Prismedia</title>
</svelte:head>

<div class="image-detail-shell">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load image."
    onRetry={detail.retry}
    posterSize="medium"
  />
</div>

{#if detail.loadState === "ready" && card && image && lightboxEntities.length > 0}
  <UniversalLightbox
    entities={lightboxEntities}
    initialIndex={0}
    onClose={closeLightbox}
    onRatingChange={(_, value) => void detail.changeRating(value)}
    sharedKey={`image-${image?.id ?? "detail"}`}
  >
    {#snippet detailsContent()}
      <div class="image-detail-back-page">
        <EntityDetail
          {card}
          onRatingChange={detail.changeRating}
          onFavoriteToggle={detail.toggleFavorite}
          onOrganizedToggle={detail.toggleOrganized}
          onMetadataSave={detail.saveMetadata}
          ratingBusy={detail.ratingBusy}
          tabs={detailTabs}
          sections={detailSections}
        >
          {#snippet heroMeta()}
            {#if studio}
              <a href={resolveEntityHref(studio.kind as EntityKindCode, studio.id)} class="meta-item is-studio">{studio.title}</a>
            {/if}
            <EntityDetailHeroDates {dates} leadingSeparator={Boolean(studio)} />
          {/snippet}

          {#snippet sectionContent(section)}
            {#if section.id === "acquisition"}
              <EntityAcquisitionCard {acq} entity={image} {fileManagement} />
            {/if}
          {/snippet}
        </EntityDetail>
      </div>
    {/snippet}
  </UniversalLightbox>
{/if}

<style>
  .image-detail-shell { display: grid; min-height: 100dvh; place-items: center; padding: clamp(1rem, 3vw, 2rem); }

  .image-detail-back-page { display: contents; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c7c9cc); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }


</style>
