<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { ArrowDown, ArrowUp, Layers, Pencil, Play, Plus, RefreshCw, Shuffle, Trash2, X } from "@lucide/svelte";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getCollection } from "$lib/api/generated/prismedia";
  import type { CollectionDetail } from "$lib/api/generated/model";
  import {
    addCollectionItems,
    deleteCollection,
    fetchCollectionItems,
    refreshCollection,
    removeCollectionItems,
    reorderCollectionItems,
  } from "$lib/api/collections";
  import { fetchEntities } from "$lib/api/entities";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { CollectionItem } from "$lib/collections/models";
  import { getEntityHref } from "$lib/components/collections/collection-item-helpers";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityPicker, { type EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
  import { durationToSeconds } from "$lib/utils/format";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { usePlaylist } from "$lib/stores/playlist.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playlist = usePlaylist();

  let loadState: LoadState = $state("loading");
  let collection = $state<CollectionDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let collectionItems = $state.raw<CollectionItem[]>([]);
  let itemCards = $state<EntityThumbnailCard[]>([]);
  let addItemKind = $state<CollectionItem["entityType"]>("video");
  let addSelection = $state<EntityPickerItem[]>([]);
  let addingItem = $state(false);
  let refreshBusy = $state(false);
  let deleteBusy = $state(false);
  let itemMutationError = $state<string | null>(null);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!collection) return null;
    return entityCardToDetailCard(collection);
  });
  const canManuallyCurate = $derived(collection?.mode !== "dynamic");
  const canRefreshRules = $derived(collection?.mode === "dynamic" || collection?.mode === "hybrid");

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
      const id = page.params.id ?? "";
      const nextCollection = unwrapGenerated<CollectionDetail>(await getCollection(id), `Failed to fetch collection ${id}`);
      const nextItems = await fetchCollectionItems(id);
      collection = nextCollection;
      collectionItems = nextItems;
      itemCards = nextItems
        .map((item) => item.entity ? entityCardToThumbnailCard(item.entity, getEntityHref(item, `/collections/${id}`)) : null)
        .filter((card): card is EntityThumbnailCard => Boolean(card));
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

  function slideshowDurationSeconds() {
    if (!collection?.slideshowAutoAdvance) return 0;
    return durationToSeconds(collection.slideshowDuration) ?? 0;
  }

  function startPlaylist(shuffle = false) {
    if (!collection || collectionItems.length === 0) return;
    playlist.startPlaylist(collectionItems, collection.title, 0, {
      shuffle,
      slideshowDurationSeconds: slideshowDurationSeconds(),
    });
  }

  async function searchAddableEntities(query: string): Promise<EntityPickerItem[]> {
    const response = await fetchEntities({
      kind: addItemKind,
      query: query || undefined,
      limit: 20,
    });
    return response.items.map((item) => ({
      id: item.id,
      title: item.title,
      thumbnailUrl: item.coverUrl,
      subtitle: item.meta.map((meta) => meta.label).filter(Boolean).join(" · "),
    }));
  }

  async function handleAddSelection(values: EntityPickerItem[]) {
    addSelection = [];
    const item = values.at(-1);
    if (!collection || !item || addingItem) return;
    addingItem = true;
    itemMutationError = null;
    try {
      await addCollectionItems(collection.id, {
        items: [{ entityType: addItemKind, entityId: item.id }],
      });
      await loadCollection();
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to add item.";
    } finally {
      addingItem = false;
    }
  }

  async function removeItem(item: CollectionItem) {
    if (!collection) return;
    itemMutationError = null;
    try {
      await removeCollectionItems(collection.id, [item.id]);
      await loadCollection();
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to remove item.";
    }
  }

  async function moveItem(index: number, direction: -1 | 1) {
    if (!collection) return;
    const nextIndex = index + direction;
    if (nextIndex < 0 || nextIndex >= collectionItems.length) return;
    const next = [...collectionItems];
    [next[index], next[nextIndex]] = [next[nextIndex], next[index]];
    itemMutationError = null;
    try {
      await reorderCollectionItems(collection.id, next.map((item) => item.id));
      await loadCollection();
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to reorder items.";
    }
  }

  async function refreshDynamicItems() {
    if (!collection || refreshBusy) return;
    refreshBusy = true;
    itemMutationError = null;
    try {
      await refreshCollection(collection.id);
      await loadCollection();
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to refresh collection.";
    } finally {
      refreshBusy = false;
    }
  }

  async function handleDeleteCollection() {
    if (!collection || deleteBusy) return;
    if (!confirm(`Delete ${collection.title}?`)) return;
    deleteBusy = true;
    try {
      await deleteCollection(collection.id);
      await goto("/collections");
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to delete collection.";
    } finally {
      deleteBusy = false;
    }
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
        {#if collection?.slideshowAutoAdvance && slideshowDurationSeconds() > 0}
          <span class="hero-badge">auto {slideshowDurationSeconds()}s</span>
        {/if}
      {/snippet}

      {#snippet extraActions()}
        <a
          class="hero-icon-action"
          aria-label="Edit collection"
          title="Edit collection"
          href={`/collections/${card.entity.id}/edit`}
        >
          <Pencil class="h-4 w-4" />
        </a>
        {#if canRefreshRules}
          <button
            type="button"
            class="hero-icon-action"
            aria-label="Refresh dynamic items"
            title="Refresh dynamic items"
            disabled={refreshBusy}
            onclick={refreshDynamicItems}
          >
            <RefreshCw class={refreshBusy ? "h-4 w-4 animate-spin" : "h-4 w-4"} />
          </button>
        {/if}
        <button
          type="button"
          class="hero-icon-action"
          aria-label="Play collection"
          title="Play collection"
          disabled={collectionItems.length === 0}
          onclick={() => startPlaylist(false)}
        >
          <Play class="h-4 w-4" />
        </button>
        <button
          type="button"
          class="hero-icon-action"
          aria-label="Shuffle collection"
          title="Shuffle collection"
          disabled={collectionItems.length === 0}
          onclick={() => startPlaylist(true)}
        >
          <Shuffle class="h-4 w-4" />
        </button>
        <button
          type="button"
          class="hero-icon-action danger"
          aria-label="Delete collection"
          title="Delete collection"
          disabled={deleteBusy}
          onclick={handleDeleteCollection}
        >
          <Trash2 class="h-4 w-4" />
        </button>
      {/snippet}
    </EntityDetail>

    {#if itemMutationError}
      <div class="error-notice">
        <p>{itemMutationError}</p>
        <button type="button" onclick={() => (itemMutationError = null)}>Dismiss</button>
      </div>
    {/if}

    {#if canManuallyCurate}
      <section class="content-section collection-curation">
        <h2 class="content-heading">
          <Plus class="h-4 w-4" />
          Curate
        </h2>
        <div class="add-row">
          <label class="kind-select">
            <span>Kind</span>
            <select bind:value={addItemKind} disabled={addingItem}>
              <option value="video">Video</option>
              <option value="gallery">Gallery</option>
              <option value="image">Image</option>
              <option value="book">Book</option>
              <option value="audio-track">Audio</option>
            </select>
          </label>
          <EntityPicker
            values={addSelection}
            onChange={handleAddSelection}
            onSearch={searchAddableEntities}
            mode="single"
            placeholder="Search media..."
            disabled={addingItem}
          />
        </div>

        {#if collectionItems.length > 0}
          <ol class="item-order-list">
            {#each collectionItems as item, index (item.id)}
              <li class="item-order-row">
                <div class="item-order-main">
                  <a href={getEntityHref(item, `/collections/${collection.id}`)}>
                    {item.entity?.title ?? "Unknown item"}
                  </a>
                  <span>{item.entityType} · {item.source}</span>
                </div>
                <div class="item-order-actions">
                  <button
                    type="button"
                    aria-label="Move item up"
                    title="Move item up"
                    disabled={index === 0}
                    onclick={() => moveItem(index, -1)}
                  >
                    <ArrowUp class="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    aria-label="Move item down"
                    title="Move item down"
                    disabled={index === collectionItems.length - 1}
                    onclick={() => moveItem(index, 1)}
                  >
                    <ArrowDown class="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    aria-label="Remove item"
                    title="Remove item"
                    onclick={() => removeItem(item)}
                  >
                    <X class="h-3.5 w-3.5" />
                  </button>
                </div>
              </li>
            {/each}
          </ol>
        {/if}
      </section>
    {/if}

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
  :global(.hero-icon-action) { display: inline-flex; width: 2.35rem; height: 2.35rem; align-items: center; justify-content: center; border: 1px solid var(--color-border-subtle, #1c2235); background: rgb(17 22 29 / 0.72); color: var(--color-text-muted, #8a93a6); backdrop-filter: blur(12px); transition: border-color 0.16s, color 0.16s, box-shadow 0.16s; }
  :global(.hero-icon-action:hover:not(:disabled)) { border-color: rgba(242, 194, 106, 0.42); color: var(--color-text-accent, #f2c26a); box-shadow: 0 0 18px rgb(242 194 106 / 0.12); }
  :global(.hero-icon-action.danger:hover:not(:disabled)) { border-color: rgba(255, 128, 111, 0.48); color: var(--color-error-text, #ff9f92); box-shadow: 0 0 18px rgb(255 128 111 / 0.12); }
  :global(.hero-icon-action:disabled) { cursor: not-allowed; opacity: 0.4; }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

  .collection-curation { border: 1px solid var(--color-border-subtle, #1c2235); background: rgba(12, 15, 21, 0.68); padding: 1rem; }
  .add-row { display: grid; grid-template-columns: minmax(8rem, 12rem) minmax(0, 1fr); gap: 0.75rem; align-items: end; }
  .kind-select { display: grid; gap: 0.35rem; }
  .kind-select span { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.66rem; text-transform: uppercase; color: var(--color-text-disabled, #596071); }
  .kind-select select { width: 100%; border: 1px solid var(--color-border-subtle, #1c2235); border-radius: var(--radius-xs, 4px); background: var(--color-surface-2, #101420); color: var(--color-text-primary, #f2eed8); padding: 0.57rem 0.65rem; }
  .item-order-list { display: grid; gap: 1px; margin: 0; padding: 0; list-style: none; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-border-subtle, #1c2235); }
  .item-order-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 0.75rem; align-items: center; background: var(--color-surface-1, #0c0f15); padding: 0.7rem 0.75rem; }
  .item-order-main { display: grid; min-width: 0; gap: 0.1rem; }
  .item-order-main a { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--color-text-primary, #f2eed8); text-decoration: none; font-size: 0.86rem; }
  .item-order-main a:hover { color: var(--color-text-accent, #f2c26a); }
  .item-order-main span { color: var(--color-text-disabled, #596071); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.66rem; }
  .item-order-actions { display: flex; align-items: center; gap: 0.25rem; }
  .item-order-actions button { display: inline-flex; width: 2rem; height: 2rem; align-items: center; justify-content: center; border: 1px solid var(--color-border-subtle, #1c2235); border-radius: var(--radius-xs, 4px); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); }
  .item-order-actions button:hover:not(:disabled) { border-color: rgba(242, 194, 106, 0.42); color: var(--color-text-accent, #f2c26a); }
  .item-order-actions button:disabled { cursor: not-allowed; opacity: 0.35; }

  @media (max-width: 720px) {
    .add-row,
    .item-order-row { grid-template-columns: 1fr; }
    .item-order-actions { justify-content: flex-end; }
  }

  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 0.85; } }
</style>
