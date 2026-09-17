import { ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind, LibraryRoot } from "$lib/api/generated/model";

const importKinds: readonly EntityKind[] = [ENTITY_KIND.book, ENTITY_KIND.comicInstallment, ENTITY_KIND.image, ENTITY_KIND.gallery];

/** Kinds whose effective executor capabilities support both inspection and submission. */
export function executorKinds(connection: ConnectionResponse | undefined): EntityKind[] {
  return importKinds.filter(kind => connection?.effectiveCapabilities.some(capability =>
    capability.kind === PLUGIN_CAPABILITY.catalogDiscovery && capability.operations.includes(INTEGRATION_OPERATION.inspect) && capability.entityKinds.includes(kind))
    && connection.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.transferExecutor
      && capability.operations.includes(INTEGRATION_OPERATION.submit) && capability.entityKinds.includes(kind)));
}

/** Writable destinations whose enabled scanner can materialize the selected media type. */
export function executorRoots(roots: LibraryRoot[], kind: EntityKind | undefined): LibraryRoot[] {
  return roots.filter(root => root.enabled && !root.isReadOnly && (kind === ENTITY_KIND.image ? root.scanImages
    : kind === ENTITY_KIND.gallery ? root.scanImages && root.recursive
    : kind === ENTITY_KIND.book || kind === ENTITY_KIND.comicInstallment ? root.scanBooks : false));
}
