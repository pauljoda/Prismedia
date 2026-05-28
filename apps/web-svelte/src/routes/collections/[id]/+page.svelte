<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    ArrowDown,
    ArrowUp,
    ChevronDown,
    Layers,
    Loader2,
    Pencil,
    Plus,
    RefreshCw,
    Trash2,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
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
  let addItemKind = $state<CollectionItem["entityType"]>("video");
  let addSelection = $state<EntityPickerItem[]>([]);
  let addingItem = $state(false);
  let refreshBusy = $state(false);
  let deleteBusy = $state(false);
  let itemMutationError = $state<string | null>(null);

  const entityKinds: { value: CollectionItem["entityType"]; label: string }[] = [
    { value: "video", label: "Video" },
    { value: "video-series", label: "Series" },
    { value: "gallery", label: "Gallery" },
    { value: "image", label: "Image" },
    { value: "book", label: "Book" },
    { value: "audio-track", label: "Audio" },
  ];

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

<div class="grid gap-5">
  {#if loadState === "loading"}
    <div class="min-h-[28rem] rounded-md border border-border-subtle bg-surface-2 animate-pulse"></div>
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

      {#snippet extraActions()}
        {@const actionClass = "inline-flex w-[2.35rem] h-[2.35rem] items-center justify-center rounded-xs border border-border-subtle bg-[rgb(17_22_29/0.72)] text-text-muted backdrop-blur-[12px] transition-all hover:border-border-accent hover:text-text-accent hover:shadow-[0_0_18px_rgb(242_194_106/0.12)] disabled:cursor-not-allowed disabled:opacity-40 no-underline"}
        {@const dangerClass = "inline-flex w-[2.35rem] h-[2.35rem] items-center justify-center rounded-xs border border-border-subtle bg-[rgb(17_22_29/0.72)] text-text-muted backdrop-blur-[12px] transition-all hover:border-error/50 hover:text-error-text hover:shadow-[0_0_18px_rgb(255_128_111/0.12)] disabled:cursor-not-allowed disabled:opacity-40"}
        <a
          class={actionClass}
          aria-label="Edit collection"
          title="Edit collection"
          href={`/collections/${card.entity.id}/edit`}
        >
          <Pencil class="h-4 w-4" />
        </a>
        {#if canRefreshRules}
          <button
            type="button"
            class={actionClass}
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
          class={dangerClass}
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

    {#if canManuallyCurate}
      <section class="surface-panel p-5 space-y-4">
        <div class="flex items-center justify-between gap-3">
          <h2 class="m-0 font-heading text-[1.05rem] font-semibold text-text-primary flex items-center gap-2">
            <Plus class="h-4 w-4 text-text-muted" />
            Curate
          </h2>
          {#if addingItem}
            <Loader2 class="h-4 w-4 animate-spin text-text-accent" />
          {/if}
        </div>

        <!-- Add row -->
        <div class="grid grid-cols-1 sm:grid-cols-[minmax(8rem,12rem)_minmax(0,1fr)] gap-3 items-end">
          <div class="space-y-1.5">
            <span class="text-kicker">Kind</span>
            <div class="relative">
              <select
                bind:value={addItemKind}
                disabled={addingItem}
                class={cn(
                  "w-full appearance-none rounded-xs border border-border-subtle bg-surface-2 px-3 py-2 pr-8 text-sm text-text-primary",
                  "shadow-[inset_0_2px_8px_rgba(0,0,0,0.30)] transition-colors outline-none",
                  "focus:border-border-accent focus:shadow-[inset_0_2px_8px_rgba(0,0,0,0.30),0_0_0_1px_rgba(242,194,106,0.35),0_0_8px_rgba(242,194,106,0.15)]",
                  "disabled:cursor-not-allowed disabled:opacity-50",
                )}
              >
                {#each entityKinds as kind (kind.value)}
                  <option value={kind.value}>{kind.label}</option>
                {/each}
              </select>
              <ChevronDown class="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-muted" />
            </div>
          </div>
          <EntityPicker
            values={addSelection}
            onChange={handleAddSelection}
            onSearch={searchAddableEntities}
            mode="single"
            placeholder="Search media to add…"
            disabled={addingItem}
          />
        </div>

        <!-- Ordered item list -->
        {#if collectionItems.length > 0}
          <div class="rounded-sm border border-border-subtle overflow-hidden">
            <ol class="m-0 p-0 list-none">
              {#each collectionItems as item, index (item.id)}
                <li
                  class={cn(
                    "grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 px-3 py-2.5 transition-colors",
                    "hover:bg-surface-2",
                    index > 0 && "border-t border-border-subtle",
                  )}
                >
                  <span class="text-[0.68rem] font-mono text-text-disabled tabular-nums w-5 text-center">
                    {index + 1}
                  </span>
                  <div class="min-w-0">
                    <a
                      href={getEntityHref(item, `/collections/${collection.id}`)}
                      class="block truncate text-[0.85rem] text-text-primary no-underline transition-colors hover:text-text-accent"
                    >
                      {item.entity?.title ?? "Unknown item"}
                    </a>
                    <span class="text-kicker">
                      {item.entityType}{#if item.source !== "manual"} · {item.source}{/if}
                    </span>
                  </div>
                  <div class="flex items-center gap-1">
                    <button
                      type="button"
                      aria-label="Move up"
                      title="Move up"
                      disabled={index === 0}
                      onclick={() => moveItem(index, -1)}
                      class={cn(
                        "inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-subtle bg-surface-2 text-text-muted transition-colors",
                        "hover:border-border-accent hover:text-text-accent",
                        "disabled:cursor-not-allowed disabled:opacity-30",
                      )}
                    >
                      <ArrowUp class="h-3 w-3" />
                    </button>
                    <button
                      type="button"
                      aria-label="Move down"
                      title="Move down"
                      disabled={index === collectionItems.length - 1}
                      onclick={() => moveItem(index, 1)}
                      class={cn(
                        "inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-subtle bg-surface-2 text-text-muted transition-colors",
                        "hover:border-border-accent hover:text-text-accent",
                        "disabled:cursor-not-allowed disabled:opacity-30",
                      )}
                    >
                      <ArrowDown class="h-3 w-3" />
                    </button>
                    <button
                      type="button"
                      aria-label="Remove item"
                      title="Remove item"
                      onclick={() => removeItem(item)}
                      class={cn(
                        "inline-flex h-7 w-7 items-center justify-center rounded-xs border border-border-subtle bg-surface-2 text-text-muted transition-colors",
                        "hover:border-error/50 hover:text-error-text",
                      )}
                    >
                      <X class="h-3 w-3" />
                    </button>
                  </div>
                </li>
              {/each}
            </ol>
          </div>
        {/if}
      </section>
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
          selectable={false}
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
