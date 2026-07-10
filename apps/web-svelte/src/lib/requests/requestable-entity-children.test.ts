import { describe, expect, it } from "vitest";
import { ENTITY_KIND, THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityKind } from "$lib/api/generated/model";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";

describe("requestableDirectChildCards", () => {
  it("passes direct child books while excluding structural and non-direct book nodes", () => {
    const cards = [
      card("book-2", ENTITY_KIND.book, "book-1"),
      card("volume-1", ENTITY_KIND.bookVolume, "book-1"),
      card("nested-book", ENTITY_KIND.book, "other-book"),
    ];

    expect(requestableDirectChildCards("book-1", cards).map((item) => item.entity.id))
      .toEqual(["book-2"]);
  });

  it("keeps every requestable direct child kind in a mixed series", () => {
    const cards = [
      card("season-1", ENTITY_KIND.videoSeason, "series-1"),
      card("sub-series", ENTITY_KIND.videoSeries, "series-1"),
      card("special-1", ENTITY_KIND.video, "series-1"),
      card("movie-1", ENTITY_KIND.movie, "series-1"),
      card("nested-episode", ENTITY_KIND.video, "season-1"),
    ];

    expect(requestableDirectChildCards("series-1", cards).map((item) => item.entity.id))
      .toEqual(["season-1", "sub-series", "special-1", "movie-1"]);
  });

  it("keeps requestable audio-library children but excludes non-requestable tracks", () => {
    const cards = [
      card("disc-1", ENTITY_KIND.audioLibrary, "album-1"),
      card("track-1", ENTITY_KIND.audioTrack, "album-1"),
    ];

    expect(requestableDirectChildCards("album-1", cards).map((item) => item.entity.id))
      .toEqual(["disc-1"]);
  });
});

function card(id: string, kind: EntityKind, parentEntityId: string): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind,
      title: id,
      parentEntityId,
      sortOrder: null,
      capabilities: [],
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none },
  };
}
