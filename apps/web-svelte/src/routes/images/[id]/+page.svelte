<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchImage, type ImageDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import {
    getCapability,
    getImagesCapability,
    getRatingValue,
    isNsfw,
  } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { hydrateStandardRelationshipCards } from "$lib/entities/entity-relationship-thumbnails";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityKindCode } from "$lib/entities/entity-codes";
  import EntityDetail, {
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import UniversalLightbox from "$lib/components/UniversalLightbox.svelte";
  import type { UniversalLightboxEntity } from "$lib/components/universal-lightbox-media";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();

  let loadState: LoadState = $state("loading");
  let image = $state<ImageDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

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
    onChanged: loadImage,
    onPruned: () => goto("/images"),
  });
  const fileManagement = {
    onDeleted: () => goto("/images"),
    onReverted: () => refreshAfterManagedFileRevert(acq, loadImage),
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

  onMount(() => {
    void loadImage();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadImage();
  });

  async function loadImage() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextImage = await fetchImage(page.params.id ?? "");
      const relationships = await hydrateStandardRelationshipCards(nextImage);
      image = nextImage;
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!image || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(image, value, (next) => (image = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!image) return;
    await toggleOptimisticEntityFlag(image, "isFavorite", (next) => (image = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!image) return;
    await toggleOptimisticEntityFlag(image, "isOrganized", (next) => (image = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!image) return;
    await updateEntityMetadata(image.id, request, { kind: image.kind });
    await loadImage();
  }

  function closeLightbox() {
    void goto("/images");
  }
</script>

<svelte:head>
  <title>{image?.title ?? "Image"} · Prismedia</title>
</svelte:head>

<div class="image-detail-shell">
  {#if loadState === "loading"}
    <EntityDetailSkeleton posterSize="medium" />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load image."}</p>
      <button type="button" onclick={() => void loadImage()}>Retry</button>
    </div>
  {/if}
</div>

{#if loadState === "ready" && card && image && lightboxEntities.length > 0}
  <UniversalLightbox
    entities={lightboxEntities}
    initialIndex={0}
    onClose={closeLightbox}
    onRatingChange={(_, value) => void handleRatingChange(value)}
    sharedKey={`image-${image?.id ?? "detail"}`}
  >
    {#snippet detailsContent()}
      <div class="image-detail-back-page">
        <EntityDetail
          {card}
          onRatingChange={handleRatingChange}
          onFavoriteToggle={handleFavoriteToggle}
          onOrganizedToggle={handleOrganizedToggle}
          onMetadataSave={handleMetadataSave}
          {ratingBusy}
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
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  .image-detail-back-page { display: contents; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }


</style>
