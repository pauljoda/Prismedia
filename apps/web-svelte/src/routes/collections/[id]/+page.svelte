<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    Layers,
    Music,
    Play,
    RefreshCw,
    Shuffle,
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
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import {
    collectCollectionAudioTracks,
    isAudioCollectionMemberKind,
  } from "$lib/entities/audio-track-collections";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
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
  import EntityActionButton from "$lib/components/entities/EntityActionButton.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import AudioTrackList from "$lib/components/AudioTrackList.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback, type PlaybackContext } from "$lib/stores/audio-playback.svelte";

  type LoadState = "loading" | "ready" | "error";
  type CollectionBodyTab = "items" | "audio";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playback = useAudioPlayback()!;

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
  let activeBodyTab = $state<CollectionBodyTab>("items");
  let audioTrackItems = $state.raw<AudioTrackListItemDto[]>([]);
  let audioAlbumCoverUrls = $state<Record<string, string | null | undefined>>({});

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
  const hasAudioMembers = $derived(collectionItems.some((item) => isAudioCollectionMemberKind(item.entityType)));
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
      const audio = await collectCollectionAudioTracks(nextItems);
      collection = nextCollection;
      collectionItems = nextItems;
      itemCards = nextItems
        .map((item) => item.entity ? entityCardToThumbnailCard(item.entity, getEntityHref(item, `/collections/${id}`)) : null)
        .filter((card): card is EntityThumbnailCard => Boolean(card));
      audioTrackItems = audio.tracks;
      audioAlbumCoverUrls = audio.albumCoverUrls;
      if (!nextItems.some((item) => isAudioCollectionMemberKind(item.entityType))) {
        activeBodyTab = "items";
      }
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

  async function handleTrackRatingChange(trackId: string, value: number | null) {
    const previousTrackItems = audioTrackItems;
    audioTrackItems = audioTrackItems.map((track) =>
      track.id === trackId ? { ...track, rating: value } : track,
    );

    try {
      await updateEntityRating(trackId, value);
    } catch (err) {
      audioTrackItems = previousTrackItems;
      console.warn("Unable to update collection audio track rating", err);
    }
  }

  async function handleTrackRename(track: AudioTrackListItemDto, title: string) {
    const previousTrackItems = audioTrackItems;
    audioTrackItems = audioTrackItems.map((item) =>
      item.id === track.id ? { ...item, title } : item,
    );

    try {
      await updateEntityMetadata(track.id, {
        fields: ["title"],
        patch: {
          title,
          description: null,
          externalIds: {},
          urls: [],
          tags: [],
          studio: null,
          credits: [],
          dates: {},
          stats: {},
          positions: {},
          classification: null,
        },
      }, { kind: ENTITY_KIND.audioTrack });
    } catch (err) {
      audioTrackItems = previousTrackItems;
      throw err;
    }
  }

  function collectionPlaybackContext(): PlaybackContext {
    return {
      albumTitle: null,
      artistName: null,
      coverUrl: card?.posterCard?.cover?.src ?? null,
      albumCoverUrls: audioAlbumCoverUrls,
    };
  }

  function playAll() {
    const firstTrack = audioTrackItems[0];
    if (!firstTrack) return;
    playback.play(audioTrackItems, firstTrack.id, collectionPlaybackContext(), { shuffle: false });
  }

  function shuffleAll() {
    if (audioTrackItems.length === 0) return;
    playback.play(audioTrackItems, undefined, collectionPlaybackContext(), { shuffle: true });
  }

  function playTrack(trackId: string) {
    if (playback.isCurrent(trackId)) playback.toggle();
    else playback.play(audioTrackItems, trackId, collectionPlaybackContext(), { shuffle: false });
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

    {#if hasAudioMembers}
      <div class="collection-tabs" role="tablist" aria-label="Collection views">
        <button
          type="button"
          role="tab"
          aria-selected={activeBodyTab === "items"}
          class:active={activeBodyTab === "items"}
          onclick={() => (activeBodyTab = "items")}
        >
          <Layers class="h-3.5 w-3.5" />
          Items
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeBodyTab === "audio"}
          class:active={activeBodyTab === "audio"}
          onclick={() => (activeBodyTab = "audio")}
        >
          <Music class="h-3.5 w-3.5" />
          Audio
        </button>
      </div>
    {/if}

    {#if activeBodyTab === "items"}
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
    {:else if hasAudioMembers}
      <section class="grid gap-3">
        <div class="collection-audio-heading">
          <h2 class="m-0 font-heading text-[1.1rem] font-semibold text-text-primary flex items-center gap-2">
            <Music class="h-4 w-4 text-text-muted" />
            Audio
            <span class="font-mono text-[0.68rem] font-semibold text-text-muted px-1.5 py-0.5 rounded-xs border border-border-subtle bg-surface-3 tabular-nums">
              {audioTrackItems.length}
            </span>
          </h2>
          {#if audioTrackItems.length > 0}
            <div class="collection-audio-actions">
              <EntityActionButton
                label="Play All"
                icon={Play}
                iconFill="currentColor"
                variant="primary"
                onClick={playAll}
              />
              <EntityActionButton
                label="Shuffle"
                icon={Shuffle}
                onClick={shuffleAll}
              />
            </div>
          {/if}
        </div>

        {#if audioTrackItems.length > 0}
          <AudioTrackList
            bulkActions={itemBulkActions}
            tracks={audioTrackItems}
            activeTrackId={playback.currentTrack?.id ?? null}
            isPlaying={playback.playing}
            onPlay={playTrack}
            onRatingChange={handleTrackRatingChange}
            onRename={handleTrackRename}
          />
        {:else}
          <div class="surface-well p-8 text-center text-[0.85rem] text-text-muted">
            <p class="m-0">No playable tracks in this collection.</p>
          </div>
        {/if}
      </section>
    {/if}
  {/if}
</div>

<style>
  .collection-tabs {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    width: max-content;
    max-width: 100%;
    overflow-x: auto;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-2, #101420);
    padding: 0.25rem;
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30);
  }

  .collection-tabs button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.4rem;
    min-height: 2rem;
    border: 1px solid transparent;
    border-radius: var(--radius-xs, 4px);
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    padding: 0 0.7rem;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .collection-tabs button:hover,
  .collection-tabs button:focus-visible,
  .collection-tabs button.active {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-accent, #c7c9cc);
    outline: none;
  }

  .collection-audio-heading {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    flex-wrap: wrap;
  }

  .collection-audio-actions {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex-wrap: wrap;
  }
</style>
