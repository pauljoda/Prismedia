import { describe, expect, it } from "vitest";
import type { RequestChildOption } from "$lib/api/generated/model";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { buildSeasonPassRows } from "./season-pass-options";

function seasonCard(id: string, title: string, number: number, externalId: string): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind: "video-season",
      title,
      parentEntityId: "series-1",
      sortOrder: number,
      capabilities: [
        {
          kind: "links",
          externalIds: [{ provider: "tmdb", value: externalId, url: null }],
          urls: [],
        } as never,
      ],
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "video",
    cover: null,
    hover: { kind: "none" },
  };
}

function providerSeason(id: string, title: string, number: number | null): RequestChildOption {
  return {
    id: `tmdb:${id}`,
    title,
    kind: "season",
    requestable: true,
    number,
    year: null,
    itemCount: null,
    overview: null,
    posterUrl: null,
    monitored: null,
  };
}

describe("season pass options", () => {
  it("lists provider-available regular seasons even when the local library only has some of them", () => {
    const rows = buildSeasonPassRows({
      localSeasons: [
        seasonCard("local-s1", "Season 1", 1, "51145"),
        seasonCard("local-s2", "Season 2", 2, "78300"),
        seasonCard("local-s3", "Season 3", 3, "86118"),
        seasonCard("local-s4", "Season 4", 4, "106225"),
        seasonCard("local-s5", "Season 5", 5, "123879"),
      ],
      episodeCounts: { "local-s5": 20 },
      providerChildren: [
        providerSeason("110159", "Specials", null),
        providerSeason("51145", "Season 1", 1),
        providerSeason("78300", "Season 2", 2),
        providerSeason("86118", "Season 3", 3),
        providerSeason("106225", "Season 4", 4),
        providerSeason("123879", "Season 5", 5),
        providerSeason("308651", "Season 6", 6),
        providerSeason("400177", "Season 7", 7),
      ],
    });

    expect(rows.map((row) => row.title)).toEqual([
      "Season 1",
      "Season 2",
      "Season 3",
      "Season 4",
      "Season 5",
      "Season 6",
      "Season 7",
    ]);
    expect(rows[4]).toMatchObject({ entityId: "local-s5", externalId: "tmdb:123879", episodes: 20 });
    expect(rows[5]).toMatchObject({ entityId: null, externalId: "tmdb:308651", number: 6 });
    expect(rows[6]).toMatchObject({ entityId: null, externalId: "tmdb:400177", number: 7 });
  });
});
