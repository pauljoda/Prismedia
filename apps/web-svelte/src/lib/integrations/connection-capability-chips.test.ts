import { describe, expect, it } from "vitest";
import { ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import { summarizeConnectionCapabilities } from "./connection-capability-chips";

describe("connection capability chips", () => {
  it("summarizes enabled effective actions with the media families they cover", () => {
    const summary = summarizeConnectionCapabilities({
      enabledCapabilities: [
        PLUGIN_CAPABILITY.catalogDiscovery,
        PLUGIN_CAPABILITY.acquisitionSource,
        PLUGIN_CAPABILITY.externalManager,
        PLUGIN_CAPABILITY.connectedLibrary,
      ],
      effectiveCapabilities: [
        {
          kind: PLUGIN_CAPABILITY.catalogDiscovery,
          operations: [INTEGRATION_OPERATION.search, INTEGRATION_OPERATION.browse],
          entityKinds: [ENTITY_KIND.book, ENTITY_KIND.comicSeries],
        },
        {
          kind: PLUGIN_CAPABILITY.acquisitionSource,
          operations: [INTEGRATION_OPERATION.resolve, INTEGRATION_OPERATION.requestSource],
          entityKinds: [ENTITY_KIND.book],
        },
        {
          kind: PLUGIN_CAPABILITY.externalManager,
          operations: [
            INTEGRATION_OPERATION.discoverManaged,
            INTEGRATION_OPERATION.lookupManaged,
            INTEGRATION_OPERATION.ensureManaged,
          ],
          entityKinds: [ENTITY_KIND.movie, ENTITY_KIND.videoSeries],
        },
        {
          kind: PLUGIN_CAPABILITY.connectedLibrary,
          operations: [INTEGRATION_OPERATION.searchLibrary, INTEGRATION_OPERATION.getLibraryItem],
          entityKinds: [ENTITY_KIND.movie],
        },
      ],
    });

    expect(summary.capabilities.map((chip) => chip.label)).toEqual([
      "Search",
      "Request",
      "Library",
      "Import",
      "Browse",
    ]);
    expect(summary.capabilities.find((chip) => chip.label === "Search")?.entityKinds).toEqual([
      ENTITY_KIND.book,
      ENTITY_KIND.comicSeries,
      ENTITY_KIND.movie,
      ENTITY_KIND.videoSeries,
    ]);
    expect(summary.entityKinds.map((kind) => kind.label)).toEqual(["Books", "Comics", "Movies", "Series"]);
  });

  it("does not borrow chips from disabled or empty effective capabilities", () => {
    const summary = summarizeConnectionCapabilities({
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery],
      effectiveCapabilities: [
        {
          kind: PLUGIN_CAPABILITY.connectedLibrary,
          operations: [INTEGRATION_OPERATION.searchLibrary],
          entityKinds: [ENTITY_KIND.movie],
        },
      ],
    });

    expect(summary.capabilities).toEqual([]);
    expect(summary.entityKinds).toEqual([]);
  });

  it("does not present exact-id lookup as catalog search", () => {
    const summary = summarizeConnectionCapabilities({
      enabledCapabilities: [PLUGIN_CAPABILITY.externalManager, PLUGIN_CAPABILITY.connectedLibrary],
      effectiveCapabilities: [
        {
          kind: PLUGIN_CAPABILITY.externalManager,
          operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged],
          entityKinds: [ENTITY_KIND.movie],
        },
        {
          kind: PLUGIN_CAPABILITY.connectedLibrary,
          operations: [INTEGRATION_OPERATION.searchLibrary, INTEGRATION_OPERATION.getLibraryItem],
          entityKinds: [ENTITY_KIND.movie],
        },
      ],
    });

    expect(summary.capabilities.map((chip) => chip.label)).toEqual(["Request", "Library"]);
  });
});
