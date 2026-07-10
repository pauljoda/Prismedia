import { bulkDeleteEntities as bulkDeleteGenerated, deleteEntity as deleteGenerated } from "$lib/api/generated/prismedia";
import type { EntityDeleteResponse } from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import {
  ENTITY_KINDS_SUPPORTING_FILE_DELETION,
  type FileDeletableEntityKindCode,
} from "$lib/api/generated/codes";

/**
 * Entity kinds whose grid items may invoke the managed delete-files endpoint. The generated list comes
 * from EntityKindRegistry.SupportsFileDeletion; detail pages additionally require the source-backed
 * file-management capability projected on that specific Entity.
 */
export type DeletableMediaKind = FileDeletableEntityKindCode;

/** Whether a kind code is a file-backed media kind that supports permanent delete-with-files. */
export function isDeletableMediaKind(kind: string | null | undefined): kind is DeletableMediaKind {
  return Boolean(kind)
    && (ENTITY_KINDS_SUPPORTING_FILE_DELETION as readonly string[]).includes(kind as string);
}

/**
 * Permanently deletes one media entity, its descendant tree, and its source files/folders from disk.
 * `deleteFiles` must be true; library-only removal is intentionally unsupported.
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
 * Permanently deletes the given media entities, their descendant trees, and their source files/folders
 * from disk. `deleteFiles` must be true; library-only removal is intentionally unsupported.
 */
export async function bulkDeleteMediaEntities(
  ids: string[],
  deleteFiles: boolean,
  options?: RequestOptions,
): Promise<EntityDeleteResponse> {
  const response = await bulkDeleteGenerated({ ids, deleteFiles }, requestInit(options));
  return unwrapGenerated<EntityDeleteResponse>(response, "Failed to delete entities");
}
