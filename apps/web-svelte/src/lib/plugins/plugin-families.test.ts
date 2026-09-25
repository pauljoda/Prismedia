import { describe, expect, it } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, IDENTIFY_ACTION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
import { capabilityLabels } from "$lib/integrations/connection-labels";
import { MEDIA_FAMILIES } from "$lib/entities/media-families";
import { familyCoverage, labelForIdentifyAction, pluginAttention, pluginFamilies } from "./plugin-families";

const tmdb: PluginProvider = {
  id: "tmdb",
  name: "TMDB",
  version: "1.0.0",
  installed: true,
  enabled: true,
  isNsfw: false,
  supports: [
    { entityKind: ENTITY_KIND.videoSeason, actions: [IDENTIFY_ACTION.lookupId] },
    { entityKind: ENTITY_KIND.movie, actions: [IDENTIFY_ACTION.lookupUrl, IDENTIFY_ACTION.search] },
    { entityKind: ENTITY_KIND.videoSeries, actions: [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId] },
  ],
  auth: [],
  missingAuthKeys: [],
};

const sonarr: PluginProvider = {
  ...tmdb,
  id: "sonarr",
  name: "Sonarr",
  supports: [],
  integration: {
    protocolVersion: 1,
    settings: [],
    capabilities: [
      { kind: PLUGIN_CAPABILITY.externalManager, operations: [], entityKinds: [ENTITY_KIND.videoSeries] },
    ],
  },
};

function connection(overrides: Partial<ConnectionResponse>): ConnectionResponse {
  return {
    id: "c1",
    pluginId: "sonarr",
    name: "Sonarr",
    baseUrl: "http://sonarr",
    enabled: true,
    enabledCapabilities: [],
    settings: {},
    configuredSecretKeys: [],
    revision: 1,
    status: CONNECTION_STATUS.ready,
    remoteInstanceId: null,
    hasPersistentRemoteIdentity: false,
    effectiveCapabilities: [],
    lastCheckedAt: null,
    lastError: null,
    ...overrides,
  };
}

describe("pluginFamilies", () => {
  it("folds kinds into media families in spectrum order with search before lookups", () => {
    const families = pluginFamilies(tmdb);

    expect(families.map((support) => support.family.key)).toEqual([ENTITY_KIND.movie, ENTITY_KIND.videoSeries]);
    expect(families[0]?.operations).toEqual(["Search", "URL"]);
    expect(families[1]?.operations).toEqual(["Search", "ID"]);
    expect(families[1]?.kinds).toHaveLength(2);
  });

  it("names integration capabilities on the families they cover", () => {
    const [series] = pluginFamilies(sonarr);

    expect(series?.family.key).toBe(ENTITY_KIND.videoSeries);
    expect(series?.operations).toEqual([capabilityLabels[PLUGIN_CAPABILITY.externalManager]]);
  });

  it("labels identify actions and keeps unknown actions visible", () => {
    expect(labelForIdentifyAction(IDENTIFY_ACTION.lookupId)).toBe("ID");
    expect(labelForIdentifyAction("lookup-isbn")).toBe("lookup-isbn");
  });
});

describe("pluginAttention", () => {
  it("stays silent for a plugin that simply works", () => {
    expect(pluginAttention(sonarr, [connection({})])).toBeNull();
  });

  it("names missing keys and unusable connections", () => {
    expect(pluginAttention({ ...tmdb, missingAuthKeys: ["api_key"] }, [])?.label).toBe("Needs keys");
    expect(pluginAttention(sonarr, [connection({ status: CONNECTION_STATUS.unavailable })])?.label).toBe(
      "Connection down",
    );
    expect(pluginAttention(sonarr, [connection({ enabled: false, status: CONNECTION_STATUS.unavailable })])).toBeNull();
  });
});

describe("familyCoverage", () => {
  it("lists every named family and counts only enabled plugins", () => {
    const coverage = familyCoverage([tmdb, { ...sonarr, enabled: false }]);

    expect(coverage).toHaveLength(MEDIA_FAMILIES.length);
    expect(coverage.find((entry) => entry.family.key === ENTITY_KIND.videoSeries)?.sources).toEqual(["TMDB"]);
    expect(coverage.find((entry) => entry.family.key === ENTITY_KIND.book)?.sources).toEqual([]);
  });
});
