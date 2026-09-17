import { describe, expect, it } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import { canBrowseRequestSource, requestSourceMode } from "./request-source-compatibility";

function source(overrides: Partial<ConnectionResponse> = {}): ConnectionResponse {
  return {
    id: "source",
    pluginId: "fixture",
    name: "Fixture source",
    baseUrl: "http://fixture.test",
    enabled: true,
    enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery],
    settings: {},
    configuredSecretKeys: [],
    revision: 1,
    status: CONNECTION_STATUS.ready,
    remoteInstanceId: null,
    hasPersistentRemoteIdentity: false,
    lastCheckedAt: null,
    lastError: null,
    effectiveCapabilities: [{
      kind: PLUGIN_CAPABILITY.catalogDiscovery,
      operations: [INTEGRATION_OPERATION.browse],
      entityKinds: [ENTITY_KIND.book],
    }],
    ...overrides,
  };
}

describe("request source compatibility", () => {
  it("requires the enabled catalog capability to support the requested entity kind", () => {
    expect(canBrowseRequestSource(source(), ENTITY_KIND.book)).toBe(true);
    expect(canBrowseRequestSource(source(), ENTITY_KIND.musicArtist)).toBe(false);
  });

  it("does not borrow entity kinds from an unrelated effective capability", () => {
    const connection = source({
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery, PLUGIN_CAPABILITY.externalManager],
      effectiveCapabilities: [
        { kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] },
        { kind: PLUGIN_CAPABILITY.externalManager, operations: [INTEGRATION_OPERATION.managerOptions], entityKinds: [ENTITY_KIND.musicArtist] },
      ],
    });

    expect(canBrowseRequestSource(connection, ENTITY_KIND.musicArtist)).toBe(false);
  });

  it("recognizes a connected library only when its library search operation is admitted", () => {
    const connection = source({
      enabledCapabilities: [PLUGIN_CAPABILITY.connectedLibrary],
      effectiveCapabilities: [{
        kind: PLUGIN_CAPABILITY.connectedLibrary,
        operations: [INTEGRATION_OPERATION.searchLibrary, INTEGRATION_OPERATION.getLibraryItem],
        entityKinds: [ENTITY_KIND.musicArtist],
      }],
    });

    expect(requestSourceMode(connection, ENTITY_KIND.musicArtist)).toBe(PLUGIN_CAPABILITY.connectedLibrary);
    expect(canBrowseRequestSource(connection, ENTITY_KIND.book)).toBe(false);
  });
});
