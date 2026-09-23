import {
  CONNECTION_STATUS,
  ENTITY_KIND,
  INTEGRATION_OPERATION,
  PLUGIN_CAPABILITY,
} from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";

/** Reports whether a ready connection can review and track a Book in its mapped library. */
export function supportsBookManager(connection: ConnectionResponse): boolean {
  if (!connection.enabled || connection.status !== CONNECTION_STATUS.ready) return false;
  const manager = connection.effectiveCapabilities.find(capability =>
    capability.kind === PLUGIN_CAPABILITY.externalManager && capability.entityKinds.includes(ENTITY_KIND.book));
  const library = connection.effectiveCapabilities.find(capability =>
    capability.kind === PLUGIN_CAPABILITY.connectedLibrary && capability.entityKinds.includes(ENTITY_KIND.book));
  return Boolean(manager && library
    && [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.reconcileManaged,
      INTEGRATION_OPERATION.configureManaged].every(operation => manager.operations.includes(operation))
    && library.operations.includes(INTEGRATION_OPERATION.getLibraryItem)
    && library.operations.includes(INTEGRATION_OPERATION.listLibraries));
}
