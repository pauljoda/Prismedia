<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Layers } from "@lucide/svelte";
  import {
    fetchCollection,
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
    type CollectionDetail,
  } from "$lib/api/prismedia";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { getAllChildIds } from "$lib/entities/entity-children";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let collection = $state<CollectionDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let itemCards = $state<EntityThumbnailCard[]>([]);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!collection) return null;
    return entityCardToDetailCard(collection);
  });

  onMount(() => {
    void loadCollection();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadCollection();
  });

  $effect(() => {
    if (!collection) return;
    return appChrome.setBreadcrumbs([
      { label: "Collections", href: "/collections" },
      { label: collection.title },
    ]);
  });

  async function loadCollection() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextCollection = await fetchCollection(page.params.id ?? "");
      collection = nextCollection;
      itemCards = thumbnailsToCards(await fetchOrderedEntityThumbnails(getAllChildIds(nextCollection)), {
        hrefFor: (thumbnail) => resolveEntityHref(thumbnail.kind, thumbnail.id),
      });
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!collection || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(collection, value, (next) => (collection = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!collection) return;
    await toggleOptimisticEntityFlag(collection, "isFavorite", (next) => (collection = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!collection) return;
    await toggleOptimisticEntityFlag(collection, "isOrganized", (next) => (collection = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!collection) return;
    await updateEntityMetadata(collection.id, request, { kind: collection.kind });
    await loadCollection();
  }
</script>

<svelte:head>
  <title>{collection?.title ?? "Collection"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true"></div>
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load collection."}</p>
      <button type="button" onclick={() => void loadCollection()}>Retry</button>
    </div>
  {:else if card && collection}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      posterSize="large"
    >
      {#snippet heroMeta()}
        {#if collection?.mode}
          <span class="meta-item">{collection.mode}</span>
        {/if}
        {#if itemCards.length > 0}
          {#if collection?.mode}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{itemCards.length} {itemCards.length === 1 ? "item" : "items"}</span>
        {/if}
      {/snippet}

      {#snippet heroBadges()}
        {#if collection?.mode}
          <span class="hero-badge">{collection.mode}</span>
        {/if}
      {/snippet}
    </EntityDetail>

    {#if itemCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Layers class="h-4 w-4" />
          Items
          <span class="content-count">{itemCards.length}</span>
        </h2>
        <EntityGrid
          cards={itemCards}
          prefsKey={`collection-${collection?.id}-items`}
          selectable={false}
          emptyTitle="Empty collection"
          emptyMessage="This collection has no items."
        />
      </section>
    {:else}
      <div class="empty-children">
        <p>This collection is empty.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .loading-shell { min-height: 28rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #101420); animation: pulse 1.2s ease-in-out infinite; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 0.85; } }
</style>
