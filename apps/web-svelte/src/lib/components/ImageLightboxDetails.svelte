<script lang="ts">
  import { Users } from "@lucide/svelte";
  import { fetchImage, type ImageDetail } from "$lib/api/media";
  import {
    updateEntityFlags,
    updateEntityMetadata,
    updateEntityRating,
  } from "$lib/api/entity-mutations";
  import { getCapability } from "$lib/api/capabilities";
  import { CAPABILITY_KIND } from "$lib/entities/entity-codes";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import type { UniversalLightboxEntity } from "$lib/components/universal-lightbox-media";

  type LoadState = "loading" | "ready" | "error";

  interface Props {
    entity: UniversalLightboxEntity;
    onRatingChange?: (entityId: string, value: number | null) => void;
  }

  let { entity, onRatingChange }: Props = $props();

  let loadState: LoadState = $state("loading");
  let loadedEntityId: string | null = $state(null);
  let image = $state<ImageDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let ratingBusy = $state(false);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!image) return null;
    return entityCardToDetailCard(image);
  });

  const studio = $derived.by((): { id: string; title: string } | null => null);
  const credits = $derived.by((): Array<{ id: string; title: string }> => []);
  const dates = $derived.by(() => {
    if (!image) return [];
    const cap = getCapability(image.capabilities, CAPABILITY_KIND.dates);
    return cap?.items ?? [];
  });

  $effect(() => {
    if (loadedEntityId === entity.id) return;
    loadedEntityId = entity.id;
    void loadImage(entity.id);
  });

  async function loadImage(entityId: string) {
    loadState = "loading";
    errorMessage = null;
    try {
      const next = await fetchImage(entityId);
      if (loadedEntityId !== entityId) return;
      image = next;
      loadState = "ready";
    } catch (err) {
      if (loadedEntityId !== entityId) return;
      image = null;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!image || ratingBusy) return;
    const entityId = image.id;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(image, value, (next) => (image = next), updateEntityRating);
      onRatingChange?.(entityId, value);
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
    await loadImage(image.id);
  }
</script>

<div class="image-detail-back-page">
  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true" aria-label="Loading image details"></div>
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load image details."}</p>
      <button type="button" onclick={() => void loadImage(entity.id)}>Retry</button>
    </div>
  {:else if card}
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
  {/if}
</div>

<style>
  .image-detail-back-page {
    max-width: 72rem;
    margin: 0 auto;
    padding: clamp(0.75rem, 2vw, 1.25rem);
  }

  .loading-shell {
    min-height: 28rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    animation: pulse 1.2s ease-in-out infinite;
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

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c49a5a);
    text-decoration: none;
    transition: opacity 0.15s;
  }

  :global(.meta-item.is-studio:hover) {
    opacity: 0.8;
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

  /* Edge padding comes from EntityDetail's .detail-after-body. */
  .credits-section {
    border-top: 1px solid var(--color-border, #1c2235);
    padding-top: 1rem;
  }

  .section-label {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    margin: 0 0 0.75rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--color-text-muted, #8a93a6);
  }

  .credits-grid {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
  }

  .credit-chip {
    padding: 0.22rem 0.55rem;
    font-size: 0.75rem;
    color: var(--color-text-secondary, #c4c9d4);
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    text-decoration: none;
    transition: border-color 0.15s, color 0.15s;
  }

  .credit-chip:hover {
    color: var(--color-text-accent, #c49a5a);
    border-color: var(--color-accent-overlay-medium);
  }

  @keyframes pulse {
    0%, 100% {
      opacity: 0.45;
    }
    50% {
      opacity: 0.85;
    }
  }
</style>
