import { ENTITY_KIND_LIBRARY_ROOTS } from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind } from "$lib/api/generated/model";
import { connectionFeatureKinds } from "./connection-features";

/** Kinds this connection can inspect and submit whose files Prismedia accepts from a connected source. */
export function executorKinds(connection: ConnectionResponse | undefined): EntityKind[] {
  return connectionFeatureKinds(connection, "executorImport").filter(kind =>
    ENTITY_KIND_LIBRARY_ROOTS[kind as keyof typeof ENTITY_KIND_LIBRARY_ROOTS]?.acceptsIntegrationImport === true);
}
