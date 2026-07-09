import { describe, expect, it } from "vitest";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE } from "$lib/api/generated/codes";
import type { EntityCapability } from "$lib/api/generated/model";
import { externalIdentities, firstExternalIdentity, hasSourceMedia } from "./capabilities";

describe("entity capability identities", () => {
  it("keeps every namespace/value pair structured and opaque", () => {
    const capabilities: EntityCapability[] = [{
      kind: CAPABILITY_KIND.links,
      externalIds: [
        { provider: "tmdbseason", value: "Show:AbC:01:5", url: null },
        { provider: "custom", value: "Case:SENSITIVE", url: null },
      ],
      urls: [],
    }];

    expect(externalIdentities(capabilities)).toEqual([
      { namespace: "tmdbseason", value: "Show:AbC:01:5" },
      { namespace: "custom", value: "Case:SENSITIVE" },
    ]);
    expect(firstExternalIdentity(capabilities)).toEqual({
      namespace: "tmdbseason",
      value: "Show:AbC:01:5",
    });
  });

  it("returns no identity when the Entity has no links capability", () => {
    expect(externalIdentities([])).toEqual([]);
    expect(firstExternalIdentity([])).toBeNull();
  });
});

describe("entity source media", () => {
  it("requires a direct Source file rather than generated media assets", () => {
    const capabilities: EntityCapability[] = [{
      kind: CAPABILITY_KIND.files,
      items: [
        { role: ENTITY_FILE_ROLE.thumbnail, path: "/cache/thumb.webp", mimeType: "image/webp" },
        { role: ENTITY_FILE_ROLE.hls, path: "/cache/stream.m3u8", mimeType: "application/vnd.apple.mpegurl" },
      ],
    }];

    expect(hasSourceMedia(capabilities)).toBe(false);

    capabilities[0] = {
      kind: CAPABILITY_KIND.files,
      items: [
        { role: ENTITY_FILE_ROLE.source, path: "/media/movie.mkv", mimeType: "video/x-matroska" },
      ],
    };
    expect(hasSourceMedia(capabilities)).toBe(true);
  });

  it("returns false when the Entity has no files capability", () => {
    expect(hasSourceMedia([])).toBe(false);
  });
});
