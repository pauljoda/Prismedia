import { describe, expect, it } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import { connectionFeatureKinds, connectionSupports } from "./connection-features";

const operation = INTEGRATION_OPERATION;

function manager(overrides: Partial<ConnectionResponse> = {}): ConnectionResponse {
  return {
    id: "manager", pluginId: "fixture", name: "Fixture manager", baseUrl: "http://fixture.test", enabled: true,
    enabledCapabilities: [PLUGIN_CAPABILITY.externalManager, PLUGIN_CAPABILITY.connectedLibrary],
    settings: {}, configuredSecretKeys: [], revision: 1, status: CONNECTION_STATUS.ready, remoteInstanceId: null,
    hasPersistentRemoteIdentity: true, lastCheckedAt: null, lastError: null,
    effectiveCapabilities: [
      { kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.book, ENTITY_KIND.comicSeries],
        operations: [operation.lookupManaged, operation.reconcileManaged, operation.configureManaged] },
      { kind: PLUGIN_CAPABILITY.connectedLibrary, entityKinds: [ENTITY_KIND.book],
        operations: [operation.getLibraryItem, operation.listLibraries] },
    ],
    ...overrides,
  };
}

describe("connection features", () => {
  it("requires every capability a feature needs for the same entity kind", () => {
    expect(connectionSupports(manager(), "bookManager", ENTITY_KIND.book)).toBe(true);
    expect(connectionSupports(manager(), "bookManager", ENTITY_KIND.comicSeries)).toBe(false);
    expect(connectionFeatureKinds(manager(), "managerControls")).toEqual([ENTITY_KIND.book, ENTITY_KIND.comicSeries]);
  });

  it("offers nothing from a connection the user disabled or the server has not verified", () => {
    expect(connectionSupports(manager({ enabled: false }), "managerControls")).toBe(false);
    expect(connectionSupports(manager({ status: CONNECTION_STATUS.unavailable }), "managerControls")).toBe(false);
  });
});
