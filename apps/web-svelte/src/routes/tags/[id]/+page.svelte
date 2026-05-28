<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Film } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchEntities } from "$lib/api/entities";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getTag } from "$lib/api/generated/prismedia";
  import type { TagDetail } from "$lib/api/generated/model";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, { type EntityMetadataUpdateRequest } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let tag = $state<TagDetail | null>(null);
  let relatedCards = $state<EntityThumbnailCard[]>([]);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!tag) return null;
    return entityCardToDetailCard(tag);
  });

  onMount(() => {
    void loadTag();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadTag();
  });

  $effect(() => {
    if (!tag) return;
    return appChrome.setBreadcrumbs([
      { label: "Tags", href: "/tags" },
      { label: tag.title },
    ]);
  });

  async function loadTag() {
    loadState = "loading";
    errorMessage = null;
    try {
      const id = page.params.id ?? "";
      tag = unwrapGenerated<TagDetail>(await getTag(id), `Failed to fetch tag ${id}`);
      await loadRelated(id);
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function loadRelated(tagId: string) {
    try {
      const response = await fetchEntities({ referencedBy: tagId, relationshipCode: "tags", limit: 1000 });
      relatedCards = response.items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
    } catch {
      relatedCards = [];
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!tag || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(tag, value, (next) => (tag = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!tag) return;
    await toggleOptimisticEntityFlag(tag, "isFavorite", (next) => (tag = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!tag) return;
    await toggleOptimisticEntityFlag(tag, "isOrganized", (next) => (tag = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!tag) return;
    await updateEntityMetadata(tag.id, request, { kind: tag.kind });
    await loadTag();
  }
</script>

<svelte:head>
  <title>{tag?.title ?? "Tag"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load tag."}</p>
      <button type="button" onclick={() => void loadTag()}>Retry</button>
    </div>
  {:else if card && tag}
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
        {#if relatedCards.length > 0}
          <span class="meta-item">{relatedCards.length} {relatedCards.length === 1 ? "item" : "items"}</span>
        {/if}
        {#if tag?.ignoreAutoTag}
          {#if relatedCards.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item is-muted">Auto-tag ignored</span>
        {/if}
      {/snippet}
    </EntityDetail>

    {#if relatedCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Film class="h-4 w-4" />
          Tagged Content
          <span class="content-count">{relatedCards.length}</span>
        </h2>
        <EntityGrid
          cards={relatedCards}
          prefsKey={`tag-${tag?.id}-content`}
          selectable={false}
          emptyTitle="No content"
          emptyMessage="No content tagged with this tag."
        />
      </section>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-muted) { color: var(--color-text-muted, #8a93a6); opacity: 0.7; font-style: italic; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

</style>
