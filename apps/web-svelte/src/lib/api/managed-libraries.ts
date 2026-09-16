import { getConnectedLibraryItem, getManagerOptions, searchConnectedLibrary, listExternalLibraryMounts, createExternalLibraryMount, inspectExternalLibraryAccess } from "$lib/api/generated/prismedia";
import type { ManagedItemInput, ManagedItemSnapshot, ManagedLibraryPage, ManagedLibraryQuery, ManagerOptions, EntityKind, ExternalLibraryMount, CreateExternalLibraryMountRequest, MappedLibrarySnapshot } from "$lib/api/generated/model";
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

/** Reads retained mappings even while the connected application is offline. */
export const fetchLibraryMounts = (connectionId: string): Promise<ExternalLibraryMount[]> =>
  listExternalLibraryMounts(connectionId).then(response => unwrapGenerated(response, "Could not read library mappings"));
/** Creates a paused read-only library after validating both sides of its mapping. */
export const saveLibraryMount = (connectionId: string, request: CreateExternalLibraryMountRequest): Promise<ExternalLibraryMount> =>
  createExternalLibraryMount(connectionId, request).then(response => unwrapGenerated(response, "Could not map the library"));
/** Separately checks the bytes Prismedia can read for a fresh remote holding observation. */
export const inspectLocalLibraryAccess = (connectionId: string, input: ManagedItemInput): Promise<MappedLibrarySnapshot> =>
  inspectExternalLibraryAccess(connectionId, input).then(response => unwrapGenerated(response, "Could not check local library files"));
