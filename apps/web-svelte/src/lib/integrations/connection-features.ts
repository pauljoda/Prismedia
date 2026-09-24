import {
  CONNECTION_STATUS,
  INTEGRATION_OPERATION,
  PLUGIN_CAPABILITY,
  type IntegrationOperationCode,
  type PluginCapabilityCode,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind } from "$lib/api/generated/model";

/** One capability family and the operations a feature needs from it; `any` accepts one of the operations. */
interface CapabilityNeed {
  readonly capability: PluginCapabilityCode;
  readonly operations: readonly IntegrationOperationCode[];
  readonly any?: boolean;
}

const manager = PLUGIN_CAPABILITY.externalManager;
const library = PLUGIN_CAPABILITY.connectedLibrary;
const catalog = PLUGIN_CAPABILITY.catalogDiscovery;
const source = PLUGIN_CAPABILITY.acquisitionSource;
const executor = PLUGIN_CAPABILITY.transferExecutor;
const operation = INTEGRATION_OPERATION;

/**
 * Every Prismedia feature a connection can offer, named once with the operations the server requires for it.
 * Surfaces ask for a feature instead of re-listing operations, so they cannot disagree about what a
 * connection supports.
 */
export const CONNECTION_FEATURE = {
  /** Search a manager's own catalog for new titles and add one. */
  managerDiscovery: [{ capability: manager, operations: [operation.discoverManaged, operation.lookupManaged, operation.ensureManaged] }],
  /** Read a manager's profiles and root folders. */
  managerOptions: [{ capability: manager, operations: [operation.managerOptions] }],
  /** Observe and change a linked holding's monitoring, profile, or search. */
  managerControls: [{ capability: manager, operations: [operation.reconcileManaged] }],
  /** Hand a linked holding back to its manager. */
  holdingRelease: [{ capability: manager, operations: [operation.inspectManagedRelease] }],
  /** Request wanted work that the manager may need to add. */
  managedRequest: [{ capability: manager, operations: [operation.lookupManaged, operation.ensureManaged] }],
  /** Review and request a metadata-led title that the manager adds, searches, and delivers into a mapped library. */
  reviewedManagedRequest: [
    {
      capability: manager,
      operations: [operation.lookupManaged, operation.ensureManaged, operation.requestManaged,
        operation.reconcileManaged, operation.configureManaged],
    },
    { capability: library, operations: [operation.getLibraryItem, operation.listLibraries] },
  ],
  /** Request a target inside a holding the manager already has. */
  existingHoldingRequest: [{
    capability: manager,
    operations: [operation.lookupManaged, operation.reconcileManaged, operation.configureManaged, operation.requestManaged],
  }],
  /** Review, request, and follow each Book format in a mapped library. */
  bookManager: [
    { capability: manager, operations: [operation.lookupManaged, operation.reconcileManaged, operation.configureManaged] },
    { capability: library, operations: [operation.getLibraryItem, operation.listLibraries] },
  ],
  /** Browse titles already in a connected library. */
  libraryBrowse: [{ capability: library, operations: [operation.searchLibrary, operation.getLibraryItem] }],
  /** Browse or search a source catalog. */
  catalogDiscovery: [{ capability: catalog, operations: [operation.browse, operation.search], any: true }],
  /** Browse a source catalog's listings. */
  catalogBrowse: [{ capability: catalog, operations: [operation.browse] }],
  /** Search a source catalog. */
  catalogSearch: [{ capability: catalog, operations: [operation.search] }],
  /** Resolve a catalog offer into files Prismedia imports directly. */
  sourceImport: [{ capability: source, operations: [operation.resolve] }],
  /** Ask a source to prepare an offer and follow it until its files are ready. */
  sourceRequest: [{ capability: source, operations: [operation.resolve, operation.requestSource, operation.observeSource] }],
  /** Inspect a URL and have an executor import its outputs. */
  executorImport: [
    { capability: catalog, operations: [operation.inspect] },
    { capability: executor, operations: [operation.submit] },
  ],
} as const satisfies Record<string, readonly CapabilityNeed[]>;

/** A named connection feature. */
export type ConnectionFeature = keyof typeof CONNECTION_FEATURE;

/**
 * Whether an enabled, ready connection admits a feature, optionally for one entity kind. The server already
 * intersects effective capabilities with the user's enabled capabilities and clears them when that choice changes.
 */
export function connectionSupports(
  connection: ConnectionResponse | null | undefined,
  feature: ConnectionFeature,
  entityKind?: EntityKind,
): boolean {
  if (!connection?.enabled || connection.status !== CONNECTION_STATUS.ready) return false;
  const needs: readonly CapabilityNeed[] = CONNECTION_FEATURE[feature];
  return needs.every(need => connection.effectiveCapabilities.some(capability => capability.kind === need.capability
    && (!entityKind || capability.entityKinds.includes(entityKind))
    && (need.any
      ? need.operations.some(required => capability.operations.includes(required))
      : need.operations.every(required => capability.operations.includes(required)))));
}

/** Entity kinds for which a connection admits a feature, in the connection's own order. */
export function connectionFeatureKinds(connection: ConnectionResponse | null | undefined, feature: ConnectionFeature): EntityKind[] {
  const kinds = connection?.effectiveCapabilities.flatMap(capability => capability.entityKinds) ?? [];
  return [...new Set(kinds)].filter(kind => connectionSupports(connection, feature, kind));
}
