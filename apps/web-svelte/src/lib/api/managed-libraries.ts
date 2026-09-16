import { getConnectedLibraryItem, getManagerOptions, searchConnectedLibrary } from "$lib/api/generated/prismedia";
import type { ManagedItemInput, ManagedItemSnapshot, ManagedLibraryPage, ManagedLibraryQuery, ManagerOptions, EntityKind } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** Reads existing holdings without changing remote acquisition or monitoring. */
export const fetchManagedLibrary = (connectionId: string, query: ManagedLibraryQuery): Promise<ManagedLibraryPage> =>
  searchConnectedLibrary(connectionId, query).then(response => unwrapGenerated(response, "Could not read the connected library"));
/** Reads exact remote file associations; this does not assert local byte access. */
export const fetchManagedItem = (connectionId: string, input: ManagedItemInput): Promise<ManagedItemSnapshot> =>
  getConnectedLibraryItem(connectionId, input).then(response => unwrapGenerated(response, "Could not read the connected item"));
/** Keeps external profile and root identities intact. */
export const fetchManagerOptions = (connectionId: string, entityKind: EntityKind): Promise<ManagerOptions> =>
  getManagerOptions(connectionId, { entityKind }).then(response => unwrapGenerated(response, "Could not read manager profiles and roots"));
