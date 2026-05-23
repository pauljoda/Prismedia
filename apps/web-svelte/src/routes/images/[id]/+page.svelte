<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { Users } from "@lucide/svelte";
  import {
    fetchImage,
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
    type ImageDetail,
  } from "$lib/api/prismedia";
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
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
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

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!image) return null;
    return entityCardToDetailCard(image);
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

  const studio = $derived.by((): { id: string; title: string } | null => null);

  const credits = $derived.by((): Array<{ id: string; title: string }> => []);

  const dates = $derived.by(() => {
    if (!image) return [];
    const cap = getCapability(image.capabilities, "dates");
    return cap?.items ?? [];
  });

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
      image = await fetchImage(page.params.id ?? "");
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
    <div class="loading-shell" aria-busy="true"></div>
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
        >
          {#snippet heroMeta()}
            {#if studio}
              <a href={resolveEntityHref("studio", studio.id)} class="meta-item is-studio">{studio.title}</a>
            {/if}
            {#each dates as date, i (date.code)}
              {#if studio || i > 0}
                <span class="meta-sep"></span>
              {/if}
              <span class="meta-item">{date.value}</span>
            {/each}
          {/snippet}

          {#snippet afterBody()}
            {#if credits.length > 0}
              <div class="credits-section">
                <h2 class="section-label">
                  <Users class="h-4 w-4" />
                  People
                </h2>
                <div class="credits-grid">
                  {#each credits as person (person.id)}
                    <a href={resolveEntityHref("person", person.id)} class="credit-chip">
                      {person.title}
                    </a>
                  {/each}
                </div>
              </div>
            {/if}
          {/snippet}
        </EntityDetail>
      </div>
    {/snippet}
  </UniversalLightbox>
{/if}

<style>
  .image-detail-shell { display: grid; min-height: 100dvh; place-items: center; padding: clamp(1rem, 3vw, 2rem); }
  .loading-shell { min-height: 28rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #101420); animation: pulse 1.2s ease-in-out infinite; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  .image-detail-back-page { max-width: 72rem; margin: 0 auto; padding: clamp(0.75rem, 2vw, 1.25rem); }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .credits-section { padding: 1rem 1.5rem; border-top: 1px solid var(--color-border, #1c2235); }
  .section-label { display: flex; align-items: center; gap: 0.45rem; margin: 0 0 0.75rem; font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--color-text-muted, #8a93a6); }
  .credits-grid { display: flex; flex-wrap: wrap; gap: 0.35rem; }
  .credit-chip { padding: 0.22rem 0.55rem; font-size: 0.75rem; color: var(--color-text-secondary, #c4c9d4); border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); text-decoration: none; transition: border-color 0.15s, color 0.15s; }
  .credit-chip:hover { color: var(--color-text-accent, #c49a5a); border-color: rgba(196, 154, 90, 0.35); }

  @media (min-width: 640px) { .credits-section { padding: 1rem 2rem; } }
  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 0.85; } }
</style>
