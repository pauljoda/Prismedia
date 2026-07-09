import { bulkDeleteEntities as bulkDeleteGenerated, deleteEntity as deleteGenerated } from "$lib/api/generated/prismedia";
import type { EntityDeleteResponse } from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

/**
 * Entity kinds whose grid/detail items represent real things on disk and may be permanently deleted
 * (with their files) through the media-entity delete endpoints. Mirrors the backend's deletable-kind
 * gate; taxonomy kinds (tag/person/studio) keep their own detach-only delete.
 */
const DELETABLE_MEDIA_KINDS = [
  ENTITY_KIND.video,
  ENTITY_KIND.videoSeries,
  ENTITY_KIND.videoSeason,
  ENTITY_KIND.movie,
  ENTITY_KIND.gallery,
  ENTITY_KIND.image,
  ENTITY_KIND.book,
  ENTITY_KIND.bookAuthor,
  ENTITY_KIND.bookVolume,
  ENTITY_KIND.audioLibrary,
  ENTITY_KIND.audioTrack,
  ENTITY_KIND.musicArtist,
] as const;

export type DeletableMediaKind = (typeof DELETABLE_MEDIA_KINDS)[number];

/** Whether a kind code is a file-backed media kind that supports permanent delete-with-files. */
export function isDeletableMediaKind(kind: string | null | undefined): kind is DeletableMediaKind {
  return Boolean(kind) && (DELETABLE_MEDIA_KINDS as readonly string[]).includes(kind as string);
}

/**
 * Permanently deletes one media entity (and its descendant tree). `deleteFiles` also removes its
 * source files/folders from disk — irreversible.
 */
export async function deleteMediaEntity(
  id: string,
  deleteFiles: boolean,
  options?: RequestOptions,
): Promise<EntityDeleteResponse> {
  const response = await deleteGenerated(id, { deleteFiles }, requestInit(options));
  return unwrapGenerated<EntityDeleteResponse>(response, "Failed to delete entity");
}

/**
 * Permanently deletes the given media entities (and their descendant trees). `deleteFiles` also
 * removes their source files/folders from disk — irreversible.
 */
export async function bulkDeleteMediaEntities(
  ids: string[],
  deleteFiles: boolean,
  options?: RequestOptions,
): Promise<EntityDeleteResponse> {
  const response = await bulkDeleteGenerated({ ids, deleteFiles }, requestInit(options));
  return unwrapGenerated<EntityDeleteResponse>(response, "Failed to delete entities");
}
