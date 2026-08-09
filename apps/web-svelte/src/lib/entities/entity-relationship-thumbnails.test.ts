import { describe, expect, it } from "vitest";
import type { EntityThumbnail } from "$lib/api/generated/model";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { thumbnailsToCards } from "./entity-relationship-thumbnails";

function episode(
  id: string,
  title: string,
  episodeNumber: number,
  sharedSourceEpisodes: EntityThumbnail["sharedSourceEpisodes"] = [],
): EntityThumbnail {
  return {
    id,
    kind: ENTITY_KIND.videoEpisode,
    title,
    parentEntityId: "season-7",
    sortOrder: episodeNumber,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
    sharedSourceEpisodes,
  };
}

describe("episode source grouping", () => {
  it("shows two provider episodes carried by one file as one E2 + E3 card", () => {
    const sharedSourceEpisodes = [
      { id: "episode-2", title: "Friends Like", seasonNumber: 7, episodeNumber: 2 },
      { id: "episode-3", title: "Space Restaurant", seasonNumber: 7, episodeNumber: 3 },
    ];

    const cards = thumbnailsToCards([
      episode("episode-2", "Friends Like", 2, sharedSourceEpisodes),
      episode("episode-3", "Space Restaurant", 3, sharedSourceEpisodes),
      episode("episode-4", "Miss Out", 4),
    ], { groupSharedSourceEpisodes: true });

    expect(cards.map((card) => card.entity.id)).toEqual(["episode-2", "episode-4"]);
    expect(cards[0]?.entity.title).toBe("Friends Like + Space Restaurant");
    expect(cards[0]?.custom?.bottomLeft).toEqual({
      label: "S7 E2 + E3",
      title: "Season 7, Episodes 2 + 3",
    });
  });

  it("keeps an individually fetched member while retaining the shared-file label", () => {
    const sharedSourceEpisodes = [
      { id: "episode-2", title: "Friends Like", seasonNumber: 7, episodeNumber: 2 },
      { id: "episode-3", title: "Space Restaurant", seasonNumber: 7, episodeNumber: 3 },
    ];

    const cards = thumbnailsToCards([
      episode("episode-3", "Space Restaurant", 3, sharedSourceEpisodes),
    ], { groupSharedSourceEpisodes: true });

    expect(cards).toHaveLength(1);
    expect(cards[0]?.entity.id).toBe("episode-3");
    expect(cards[0]?.custom?.bottomLeft?.label).toBe("S7 E2 + E3");
  });
});
