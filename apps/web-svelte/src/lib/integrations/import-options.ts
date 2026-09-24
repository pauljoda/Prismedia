import { ENTITY_KIND_LIBRARY_ROOTS } from "$lib/api/generated/codes";
import type { EntityKind, LibraryRoot } from "$lib/api/generated/model";

/** Whether a library root scans the media a kind's files need. Capability codes match root property names. */
export function rootScansKind(root: LibraryRoot, kind: EntityKind | undefined): boolean {
  const requirement = kind ? ENTITY_KIND_LIBRARY_ROOTS[kind as keyof typeof ENTITY_KIND_LIBRARY_ROOTS] : undefined;
  return requirement !== undefined && root[requirement.capability] === true;
}

/** Writable destinations whose enabled scanner can materialize the selected media type. */
export function integrationImportRoots(roots: LibraryRoot[], kind: EntityKind | undefined): LibraryRoot[] {
  const requirement = kind ? ENTITY_KIND_LIBRARY_ROOTS[kind as keyof typeof ENTITY_KIND_LIBRARY_ROOTS] : undefined;
  return roots.filter(root => root.enabled && !root.isReadOnly && rootScansKind(root, kind)
    && (!requirement?.requiresRecursiveRoot || root.recursive));
}
