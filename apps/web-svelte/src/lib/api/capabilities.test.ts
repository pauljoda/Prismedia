import { describe, expect, it } from "vitest";
import { CAPABILITY_KIND } from "$lib/api/generated/codes";
import type { EntityCapability } from "$lib/api/generated/model";
import { externalIdentities, firstExternalIdentity } from "./capabilities";

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
