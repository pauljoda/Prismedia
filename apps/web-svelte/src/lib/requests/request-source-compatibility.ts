import { PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind } from "$lib/api/generated/model";
import { connectionSupports } from "$lib/integrations/connection-features";

/**
 * Returns whether a connection exposes a usable Request browse surface. Each surface is judged from one
 * effective capability, so a connection cannot be surfaced from an unrelated capability that happens to
 * mention the requested entity kind.
 */
export function canBrowseRequestSource(
  connection: ConnectionResponse,
  entityKind?: EntityKind,
): boolean {
  return requestSourceMode(connection, entityKind) !== null;
}

/** Returns the primary discovery surface available for a source. */
export function requestSourceMode(
  connection: ConnectionResponse,
  entityKind?: EntityKind,
): typeof PLUGIN_CAPABILITY.externalManager | typeof PLUGIN_CAPABILITY.connectedLibrary | typeof PLUGIN_CAPABILITY.catalogDiscovery | null {
  if (canDiscoverManagerTitles(connection, entityKind)) return PLUGIN_CAPABILITY.externalManager;
  if (connectionSupports(connection, "libraryBrowse", entityKind)) return PLUGIN_CAPABILITY.connectedLibrary;
  if (connectionSupports(connection, "catalogDiscovery", entityKind)) return PLUGIN_CAPABILITY.catalogDiscovery;
  return null;
}

/** New-title discovery must be explicitly admitted by this connection and its plugin. */
export function canDiscoverManagerTitles(connection: ConnectionResponse, entityKind?: EntityKind): boolean {
  return connectionSupports(connection, "managerDiscovery", entityKind);
}
