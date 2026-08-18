import { SvelteMap } from "svelte/reactivity";
import { getEntityHoverImages } from "$lib/api/generated/prismedia";
import { requestInit, unwrapGenerated } from "$lib/api/generated-response";
import type { EntityHoverImagesResponse } from "$lib/api/generated/model";
import type { EntityThumbnailAsset } from "$lib/entities/entity-thumbnail";

/**
 * Lazy hover-preview assets, fetched per entity on first hover intent. List responses no longer
 * carry sampled child artwork for every row; a card without an inline hover model asks here, the
 * result is cached for the session, and the card's hover model upgrades reactively when the
 * assets arrive. Entities with no child artwork cache an empty result so they are asked once.
 */
const assetsByEntity = new SvelteMap<string, EntityThumbnailAsset[]>();
const requested = new Set<string>();

/** Cached hover assets for an entity; undefined until requested/resolved. */
export function lazyHoverAssetsFor(entityId: string): EntityThumbnailAsset[] | undefined {
  return assetsByEntity.get(entityId);
}

/** Requests hover assets once per entity per session; safe to call on every hover intent. */
export function requestLazyHoverAssets(entityId: string): void {
  if (!entityId || requested.has(entityId)) return;
  requested.add(entityId);
  void getEntityHoverImages({ ids: [entityId] }, {}, requestInit())
    .then((response) => {
      const items = unwrapGenerated<EntityHoverImagesResponse>(
        response,
        "Failed to fetch hover previews",
      ).items;
      const images = items.find((set) => set.entityId === entityId)?.images ?? [];
      assetsByEntity.set(
        entityId,
        images.map((image) => ({
          src: image.path,
          alt: `${image.title} preview`,
          role: "preview",
          entityId: image.entityId,
        })),
      );
    })
    .catch(() => {
      // Allow a retry on the next hover after a transient failure.
      requested.delete(entityId);
    });
}
