import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { EntityKind, LibraryRoot } from "$lib/api/generated/model";

/** Writable destinations whose enabled scanner can materialize the selected media type. */
export function integrationImportRoots(roots: LibraryRoot[], kind: EntityKind | undefined): LibraryRoot[] {
  return roots.filter(root => root.enabled && !root.isReadOnly && (kind === ENTITY_KIND.image ? root.scanImages
    : kind === ENTITY_KIND.gallery ? root.scanImages && root.recursive
    : kind === ENTITY_KIND.book || kind === ENTITY_KIND.comicInstallment ? root.scanBooks : false));
}
