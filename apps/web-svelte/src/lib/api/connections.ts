import { browseConnection, createConnection, deleteConnection, listConnections, testConnection, updateConnection } from "$lib/api/generated/prismedia";
import type { BrowseConnectionRequest, DiscoveryPageResponse, ConnectionResponse, CreateConnectionRequest, UpdateConnectionRequest } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

export const fetchConnections = (): Promise<ConnectionResponse[]> => listConnections()
  .then(response => unwrapGenerated(response, "Could not load connections"));
export const addConnection = (request: CreateConnectionRequest): Promise<ConnectionResponse> => createConnection(request)
  .then(response => unwrapGenerated(response, "Could not create connection", [201]));
export const saveConnection = (id: string, request: UpdateConnectionRequest): Promise<ConnectionResponse> => updateConnection(id, request)
  .then(response => unwrapGenerated(response, "Could not save connection"));
export const probeConnection = (id: string): Promise<ConnectionResponse> => testConnection(id)
  .then(response => unwrapGenerated(response, "Could not test connection"));
export const removeConnection = (connection: ConnectionResponse): Promise<void> => deleteConnection(connection.id, { expectedRevision: connection.revision })
  .then(response => unwrapGenerated(response, "Could not remove connection", [204]));

/** Browses a catalog using protected selections; source addresses and credentials stay on the server. */
export const fetchConnectionCatalog = (id: string, request: BrowseConnectionRequest): Promise<DiscoveryPageResponse> => browseConnection(id, request)
  .then(response => unwrapGenerated(response, "Could not browse catalog"));
