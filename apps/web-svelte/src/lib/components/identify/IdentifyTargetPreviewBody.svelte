<script lang="ts">
  import { onMount } from "svelte";
  import { ImageOff, Loader2 } from "@lucide/svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityCard } from "$lib/api/entities";
  import type { EntityThumbnail as EntityThumbnailDto } from "$lib/api/generated/model";
  import { fetchSeason, fetchSeries } from "$lib/api/media";
  import {
    ENTITY_KIND,
    isEntityKindCode,
    resolveEntityHref,
  } from "$lib/entities/entity-codes";
  import { getChildIds } from "$lib/entities/entity-children";
  import {
    fetchOrderedEntityThumbnails,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    entity: EntityCard;
  }

  let { entity }: Props = $props();

  const PREVIEW_LIMIT = 8;

  let loading = $state(true);
  let cards = $state<EntityThumbnailCard[]>([]);

  const isSingle = $derived(cards.length === 1);

  function previewHrefFor(thumbnail: EntityThumbnailDto): string | undefined {
    const parent =
      thumbnail.parentEntityId && thumbnail.parentKind && isEntityKindCode(thumbnail.parentKind)
        ? { kind: thumbnail.parentKind, id: thumbnail.parentEntityId }
        : undefined;
    return resolveEntityHref(thumbnail.kind, thumbnail.id, parent);
  }

  /**
   * Resolves which entities to preview as thumbnails. Series and seasons have no
   * file of their own, so they preview as a row of example episodes (drilling into
   * the first season when a series only groups seasons). Books/comics and other
   * concrete entities preview as their own thumbnail, whose cover/hover gives
   * enough context to identify it.
   */
  async function resolvePreviewIds(): Promise<string[]> {
    if (entity.kind === ENTITY_KIND.videoSeries) {
      const series = await fetchSeries(entity.id);
      let ids = getChildIds(series, ENTITY_KIND.video);
      if (ids.length === 0) {
        const seasonIds = getChildIds(series, ENTITY_KIND.videoSeason);
        if (seasonIds.length > 0) {
          const firstSeason = await fetchSeason(entity.id, seasonIds[0]).catch(() => null);
          ids = getChildIds(firstSeason, ENTITY_KIND.video);
          if (ids.length === 0) ids = seasonIds;
        }
      }
      return ids;
    }

    if (entity.kind === ENTITY_KIND.videoSeason && entity.parentEntityId) {
      const season = await fetchSeason(entity.parentEntityId, entity.id).catch(() => null);
      return getChildIds(season, ENTITY_KIND.video);
    }

    // Other entity kinds preview through their own shared thumbnail shell.
    return [entity.id];
  }

  onMount(async () => {
    loading = true;
    try {
      const ids = await resolvePreviewIds();
      const thumbnails = await fetchOrderedEntityThumbnails(ids.slice(0, PREVIEW_LIMIT));
      cards = thumbnailsToCards(thumbnails, { hrefFor: previewHrefFor });
    } catch {
      // Leave the preview empty on failure; identification still works.
    } finally {
      loading = false;
    }
  });
</script>

<div class="p-3.5">
  {#if loading}
    <div class="flex items-center justify-center gap-2 py-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="font-mono text-[0.72rem]">Loading preview…</span>
    </div>
  {:else if cards.length === 0}
    <div class="flex items-center justify-center gap-2 py-10 text-text-disabled">
      <ImageOff class="h-4 w-4" />
      <span class="font-mono text-[0.72rem]">No preview available for this item.</span>
    </div>
  {:else if isSingle}
    <div class="max-w-sm">
      <EntityThumbnail card={cards[0]} linkTarget="_blank" />
    </div>
  {:else}
    <div class="flex flex-col gap-2">
      <span class="font-mono text-[0.68rem] uppercase tracking-[0.12em] text-text-muted">
        Example episodes
      </span>
      <div class="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
        {#each cards as card (card.entity.id)}
          <EntityThumbnail {card} linkTarget="_blank" />
        {/each}
      </div>
    </div>
  {/if}
</div>
