import { describe, expect, it } from "vitest";
import type {
  AudioLibraryDetail,
  EntityThumbnail,
} from "$lib/api/generated/model";
import type { CollectionItem } from "$lib/collections/models";
import {
  collectCollectionAudioTracks,
  tracksFromAudioLibraryDetail,
} from "$lib/entities/audio-track-collections";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

function track(id: string, isWanted = false): EntityThumbnail {
  return {
    id,
    kind: ENTITY_KIND.audioTrack,
    title: id,
    parentEntityId: "album-1",
    sortOrder: 1,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
    isWanted,
  };
}

describe("audio track collections", () => {
  it("keeps wanted album children out of playable track lists", () => {
    const detail = {
      id: "album-1",
      title: "Album",
      childrenByKind: [{
        kind: ENTITY_KIND.audioTrack,
        entities: [track("playable"), track("wanted", true)],
      }],
    } as AudioLibraryDetail;

    expect(tracksFromAudioLibraryDetail(detail, false).map((item) => item.id))
      .toEqual(["playable"]);
  });

  it("keeps directly collected wanted tracks out of playback queues", async () => {
    const item = { entity: track("wanted", true) } as CollectionItem;

    const result = await collectCollectionAudioTracks([item]);

    expect(result.tracks).toEqual([]);
  });
});
