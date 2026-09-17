import { describe, expect, it } from "vitest";
import { ENTITY_KIND, EXTERNAL_ID_PROVIDER } from "$lib/api/generated/codes";
import type { ManagedItemInput, ManagedLibraryItem } from "$lib/api/generated/model";
import { isTrackedManagedItem } from "./managed-item-identity";

const item: ManagedLibraryItem = {
  remoteId: "remote-one",
  entityKind: ENTITY_KIND.movie,
  title: "A film",
  year: null,
  externalIds: {
    [EXTERNAL_ID_PROVIDER.tmdb]: "1",
    [EXTERNAL_ID_PROVIDER.imdb]: "tt0000001",
  },
  monitored: false,
  profileId: null,
  remoteFileCount: 0,
};

function tracked(expectedExternalIds: ManagedItemInput["expectedExternalIds"]): ManagedItemInput {
  return { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds };
}

describe("managed item identity matching", () => {
  it("keeps an older tracking pin valid when the source adds another identifier", () => {
    expect(isTrackedManagedItem(tracked({ [EXTERNAL_ID_PROVIDER.tmdb]: "1" }), item)).toBe(true);
  });

  it("rejects recycled remote IDs and identity-free tracking records", () => {
    expect(isTrackedManagedItem(tracked({ [EXTERNAL_ID_PROVIDER.tmdb]: "999" }), item)).toBe(false);
    expect(isTrackedManagedItem(tracked({}), item)).toBe(false);
  });
});
