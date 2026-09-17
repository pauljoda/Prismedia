import { describe, expect, it } from "vitest";
import { ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, LibraryRoot } from "$lib/api/generated/model";
import { executorKinds } from "./executor-options";
import { integrationImportRoots } from "./import-options";

describe("executor destinations", () => {
  const roots = [
    { id: "books", enabled: true, scanBooks: true },
    { id: "images", enabled: true, scanImages: true },
    { id: "external", enabled: true, scanImages: true, isReadOnly: true },
    { id: "disabled", enabled: false, scanImages: true },
  ].map(root => ({ path: "/library", label: root.id, recursive: true, scanVideos: false, scanImages: false,
    scanAudio: false, scanBooks: false, isNsfw: false, lastScannedAt: null, createdAt: "", updatedAt: "", ...root } satisfies LibraryRoot));
  it("uses writable image libraries for images and publication libraries for books", () => {
    expect(integrationImportRoots(roots, ENTITY_KIND.image).map(root => root.id)).toEqual(["images"]);
    expect(integrationImportRoots(roots, ENTITY_KIND.book).map(root => root.id)).toEqual(["books"]);
    expect(integrationImportRoots(roots, ENTITY_KIND.gallery).map(root => root.id)).toEqual(["images"]);
    expect(integrationImportRoots(roots.map(root => ({ ...root, recursive: false })), ENTITY_KIND.gallery)).toEqual([]);
  });
  it("offers only kinds that can both be inspected and submitted", () => {
    const connection = { effectiveCapabilities: [
      { kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.inspect], entityKinds: [ENTITY_KIND.image, ENTITY_KIND.book, ENTITY_KIND.gallery] },
      { kind: PLUGIN_CAPABILITY.transferExecutor, operations: [INTEGRATION_OPERATION.submit], entityKinds: [ENTITY_KIND.image, ENTITY_KIND.gallery] },
    ] } as ConnectionResponse;
    expect(executorKinds(connection)).toEqual([ENTITY_KIND.image, ENTITY_KIND.gallery]);
    expect(executorKinds(undefined)).toEqual([]);
  });
});
