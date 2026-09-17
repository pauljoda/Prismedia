import { INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind } from "$lib/api/generated/model";

/**
 * Returns whether a connection exposes a usable Request browse surface.
 *
 * The enabled capability list is the user's intent; effective capabilities are
 * the adapter's admitted operations and entity kinds. Both are required so a
 * connection cannot be surfaced from an unrelated capability that happens to
 * mention the requested entity kind.
 */
export function canBrowseRequestSource(
  connection: ConnectionResponse,
  entityKind?: EntityKind,
): boolean {
  if (!connection.enabled) return false;

  return hasCatalogBrowse(connection, entityKind) || hasConnectedLibraryBrowse(connection, entityKind);
}

/** Returns the connected-library or catalog mode available for a source. */
export function requestSourceMode(
  connection: ConnectionResponse,
  entityKind?: EntityKind,
): typeof PLUGIN_CAPABILITY.connectedLibrary | typeof PLUGIN_CAPABILITY.catalogDiscovery | null {
  if (!connection.enabled) return null;
  if (hasConnectedLibraryBrowse(connection, entityKind)) return PLUGIN_CAPABILITY.connectedLibrary;
  if (hasCatalogBrowse(connection, entityKind)) return PLUGIN_CAPABILITY.catalogDiscovery;
  return null;
}

function hasCatalogBrowse(connection: ConnectionResponse, entityKind?: EntityKind): boolean {
  if (!connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.catalogDiscovery)) return false;
  return connection.effectiveCapabilities.some((capability) =>
    capability.kind === PLUGIN_CAPABILITY.catalogDiscovery
      && capability.operations.some((operation) =>
        operation === INTEGRATION_OPERATION.browse || operation === INTEGRATION_OPERATION.search)
      && (!entityKind || capability.entityKinds.includes(entityKind)),
  );
}

function hasConnectedLibraryBrowse(connection: ConnectionResponse, entityKind?: EntityKind): boolean {
  if (!connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.connectedLibrary)) return false;
  return connection.effectiveCapabilities.some((capability) =>
    capability.kind === PLUGIN_CAPABILITY.connectedLibrary
      && capability.operations.includes(INTEGRATION_OPERATION.searchLibrary)
      && capability.operations.includes(INTEGRATION_OPERATION.getLibraryItem)
      && (!entityKind || capability.entityKinds.includes(entityKind)),
  );
}
