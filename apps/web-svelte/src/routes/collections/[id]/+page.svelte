<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    Layers,
    RefreshCw,
    SlidersHorizontal,
    Trash2,
    X,
  } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getCollection } from "$lib/api/generated/prismedia";
  import type { CollectionDetail } from "$lib/api/generated/model";
  import {
    deleteCollection,
    fetchCollectionItems,
    refreshCollection,
    removeCollectionItems,
  } from "$lib/api/collections";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { EntityGridBulkAction } from "$lib/entities/entity-grid";
  import type { CollectionItem } from "$lib/collections/models";
  import { getEntityHref } from "$lib/components/collections/collection-item-helpers";
  import EntityDetail, {
    type EntityDetailActionButton,
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
  let collectionItems = $state.raw<CollectionItem[]>([]);
  let itemCards = $state<EntityThumbnailCard[]>([]);
  let refreshBusy = $state(false);
  let deleteBusy = $state(false);
  let removingItems = $state(false);
  let itemMutationError = $state<string | null>(null);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!collection) return null;
    const detailCard = entityCardToDetailCard(collection);
    return {
      ...detailCard,
      posterCard: collectionPosterCard(detailCard),
    };
  });
  const canManuallyCurate = $derived(collection?.mode !== "dynamic");
  const canRefreshRules = $derived(collection?.mode === "dynamic" || collection?.mode === "hybrid");
  // Route-level grid bulk action. Selected cards expose entity ids, so the
  // handler maps them back to the collection item ids the remove endpoint
  // expects. Only manual/hybrid collections can drop members.
  const itemBulkActions = $derived.by((): EntityGridBulkAction[] => {
    if (!canManuallyCurate) return [];
    return [
      {
        id: "remove-from-collection",
        label: "Remove from Collection",
        tone: "danger",
        onRun: (selectedIds) => void removeSelectedItems(selectedIds),
      },
    ];
  });
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    if (!card) return [];
    return [
      {
        id: "edit-rules",
        label: "Edit Rules",
        icon: SlidersHorizontal,
        href: `/collections/${card.entity.id}/edit`,
        ariaLabel: "Edit collection rules",
        title: "Edit collection rules",
      },
      {
        id: "refresh-dynamic-items",
        label: "Refresh",
        icon: RefreshCw,
        iconClass: refreshBusy ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5",
        disabled: refreshBusy,
        hidden: !canRefreshRules,
        ariaLabel: "Refresh dynamic items",
        title: "Refresh dynamic items",
        onClick: refreshDynamicItems,
      },
      {
        id: "delete-collection",
        label: "Delete",
        icon: Trash2,
        disabled: deleteBusy,
        variant: "danger",
        ariaLabel: "Delete collection",
        title: "Delete collection",
        onClick: handleDeleteCollection,
      },
    ];
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

  function collectionPosterCard(detailCard: EntityDetailCardFull): EntityThumbnailCard | null {
    if (detailCard.posterCard?.cover) return detailCard.posterCard;

    const cardsWithCovers = itemCards.filter((item) => item.cover);
    if (cardsWithCovers.length === 0) return detailCard.posterCard;

    const selectedItem = collection?.coverItemId
      ? cardsWithCovers.find((item) => item.entity.id === collection?.coverItemId)
      : null;
    const primaryItem = selectedItem ?? cardsWithCovers[0];

    if (collection?.coverMode === "item") {
      return {
        ...primaryItem,
        hover: { kind: THUMBNAIL_HOVER_KIND.none },
        href: undefined,
      };
    }

    const mosaicAssets = cardsWithCovers
      .map((item) => item.cover)
      .filter((cover): cover is NonNullable<EntityThumbnailCard["cover"]> => Boolean(cover))
      .slice(0, 5);

    return {
      ...(detailCard.posterCard ?? primaryItem),
      cover: primaryItem.cover,
      fit: "cover",
      hover: mosaicAssets.length > 1
        ? { kind: THUMBNAIL_HOVER_KIND.imageSequence, assets: mosaicAssets }
        : { kind: THUMBNAIL_HOVER_KIND.none },
      href: undefined,
    };
  }

  async function removeSelectedItems(entityIds: string[]) {
    if (!collection || removingItems || entityIds.length === 0) return;
    const selected = new Set(entityIds);
    const itemIds = collectionItems
      .filter((item) => selected.has(item.entityId))
      .map((item) => item.id);
    if (itemIds.length === 0) return;
    removingItems = true;
    itemMutationError = null;
    try {
      await removeCollectionItems(collection.id, itemIds);
      await loadCollection();
    } catch (err) {
      itemMutationError = err instanceof Error ? err.message : "Failed to remove items.";
    } finally {
      removingItems = false;
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

<div class="grid gap-5">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="flex items-center justify-between gap-4 rounded-sm border border-error/50 bg-surface-2 p-4 text-[0.85rem] text-text-muted">
      <p class="m-0">{errorMessage ?? "Failed to load collection."}</p>
      <button
        type="button"
        onclick={() => void loadCollection()}
        class="rounded-xs border border-border-subtle bg-surface-3 px-3 py-1.5 text-[0.78rem] text-text-muted transition-colors hover:border-border-default hover:text-text-primary"
      >
        Retry
      </button>
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
      standaloneMetadataSectionIds={[]}
      actionButtons={heroActions}
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

    {#if itemMutationError}
      <div class="flex items-center justify-between gap-3 rounded-sm border border-error/50 bg-surface-2 px-4 py-3 text-[0.8rem] text-error-text">
        <span>{itemMutationError}</span>
        <button
          type="button"
          onclick={() => (itemMutationError = null)}
          class="inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-subtle bg-surface-3 text-text-muted transition-colors hover:text-text-primary"
        >
          <X class="h-3 w-3" />
        </button>
      </div>
    {/if}

    {#if itemCards.length > 0}
      <section class="grid gap-3">
        <h2 class="m-0 font-heading text-[1.1rem] font-semibold text-text-primary flex items-center gap-2">
          <Layers class="h-4 w-4 text-text-muted" />
          Items
          <span class="font-mono text-[0.68rem] font-semibold text-text-muted px-1.5 py-0.5 rounded-xs border border-border-subtle bg-surface-3 tabular-nums">
            {itemCards.length}
          </span>
        </h2>
        <EntityGrid
          cards={itemCards}
          prefsKey={`collection-${collection?.id}-items`}
          selectable={canManuallyCurate}
          bulkActions={itemBulkActions}
          emptyTitle="Empty collection"
          emptyMessage="This collection has no items."
        />
      </section>
    {:else}
      <div class="surface-well p-8 text-center text-[0.85rem] text-text-muted">
        <p class="m-0">This collection is empty.</p>
      </div>
    {/if}
  {/if}
</div>
