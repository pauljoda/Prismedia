import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  EntityCard,
  EntityThumbnail,
} from "$lib/api/generated/model";
import type { CollectionItem } from "$lib/collections/models";
import { fetchEntityChildren, fetchEntityThumbnails } from "$lib/api/entities";
import {
  collectCollectionAudioTracks,
  collectLibraryTrackGroups,
  tracksFromAudioLibraryChildren,
  tracksFromAudioLibraryDetail,
} from "$lib/entities/audio-track-collections";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

vi.mock("$lib/api/entities", () => ({
  fetchEntityChildren: vi.fn(),
  fetchEntityThumbnails: vi.fn(),
}));

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

function library(id: string, sortOrder = 0): EntityThumbnail {
  return {
    ...track(id),
    kind: ENTITY_KIND.audioLibrary,
    parentEntityId: null,
    sortOrder,
  };
}

function artist(id: string): EntityThumbnail {
  return {
    ...track(id),
    kind: ENTITY_KIND.musicArtist,
    parentEntityId: null,
  };
}

describe("audio track collections", () => {
  beforeEach(() => {
    vi.mocked(fetchEntityChildren).mockReset();
    vi.mocked(fetchEntityThumbnails).mockReset();
  });

  it("keeps wanted album children out of playable track lists", () => {
    const detail = {
      id: "album-1",
      title: "Album",
      childrenByKind: [{
        kind: ENTITY_KIND.audioTrack,
        entities: [track("playable"), track("wanted", true)],
      }],
    } as EntityCard;

    expect(tracksFromAudioLibraryDetail(detail, false).map((item) => item.id))
      .toEqual(["playable"]);
  });

  it("keeps directly collected wanted tracks out of playback queues", async () => {
    const item = { entity: track("wanted", true) } as CollectionItem;

    const result = await collectCollectionAudioTracks([item]);

    expect(result.tracks).toEqual([]);
  });

  it("projects batched album children with the same wanted and ordering rules", () => {
    const later = { ...track("later"), sortOrder: 2 };
    const earlier = { ...track("earlier"), sortOrder: 1 };

    const result = tracksFromAudioLibraryChildren(
      { id: "album-1", title: "Album" },
      [later, track("wanted", true), earlier],
      true,
    );

    expect(result.map((item) => item.id)).toEqual(["earlier", "later"]);
    expect(result.every((item) => item.sectionLabel === "Album")).toBe(true);
  });

  it("hydrates mixed collection audio roots per level without changing duplicate depth-first queue order", async () => {
    const libraryRoot = library("library-root");
    const artistRoot = artist("artist-root");
    const directTrack = { ...track("direct-track"), parentEntityId: "direct-album" };
    const artistAlbumLate = library("artist-album-late", 2);
    const artistAlbumEarly = library("artist-album-early", 1);
    const nestedLibrary = library("nested-library");
    const nestedArtistAlbum = library("nested-artist-album");
    const groupsByParent = new Map<string, EntityThumbnail[]>([
      ["library-root", [track("library-root-late"), { ...track("library-root-early"), sortOrder: 0 }, nestedLibrary]],
      ["artist-root", [artistAlbumLate, artistAlbumEarly]],
      ["artist-album-early", [track("artist-early-track"), nestedArtistAlbum]],
      ["artist-album-late", [track("artist-late-track")]],
      ["nested-library", [track("nested-library-track")]],
      ["nested-artist-album", [track("nested-artist-track")]],
    ]);
    vi.mocked(fetchEntityThumbnails).mockImplementation(async (ids) =>
      ids.map((id) => library(id)),
    );
    vi.mocked(fetchEntityChildren).mockImplementation(async (parentIds) =>
      parentIds.map((parentId) => ({ parentId, items: groupsByParent.get(parentId) ?? [] })),
    );

    const result = await collectCollectionAudioTracks([
      { entity: directTrack } as CollectionItem,
      { entity: libraryRoot } as CollectionItem,
      { entity: libraryRoot } as CollectionItem,
      { entity: artistRoot } as CollectionItem,
    ]);

    expect(result.tracks.map((item) => item.id)).toEqual([
      "direct-track",
      "library-root-early",
      "library-root-late",
      "nested-library-track",
      "library-root-early",
      "library-root-late",
      "nested-library-track",
      "artist-early-track",
      "nested-artist-track",
      "artist-late-track",
    ]);
    expect(result.tracks.filter((item) => item.id === "library-root-early")).toHaveLength(2);
    expect(result.tracks.find((item) => item.id === "nested-artist-track")?.sectionLabel)
      .toBe("nested-artist-album");
    expect(fetchEntityThumbnails).toHaveBeenCalledTimes(1);
    expect(fetchEntityThumbnails).toHaveBeenCalledWith(["direct-album"], { signal: undefined });
    expect(fetchEntityChildren).toHaveBeenCalledTimes(3);
    expect(fetchEntityChildren).toHaveBeenNthCalledWith(1, ["library-root", "artist-root"], { signal: undefined });
    expect(fetchEntityChildren).toHaveBeenNthCalledWith(2, ["artist-album-early", "artist-album-late"], { signal: undefined });
    expect(fetchEntityChildren).toHaveBeenNthCalledWith(3, ["nested-library", "nested-artist-album"], { signal: undefined });
  });

  it("loads artist-page album groups through a single root and child batch", async () => {
    vi.mocked(fetchEntityThumbnails).mockImplementation(async (ids) => ids.map((id) => library(id)));
    vi.mocked(fetchEntityChildren).mockImplementation(async (parentIds) => parentIds.map((parentId) => ({
      parentId,
      items: [track(`${parentId}-track`)],
    })));

    const result = await collectLibraryTrackGroups(["album-one", "album-two"]);

    expect(result.tracks.map((item) => item.id)).toEqual(["album-one-track", "album-two-track"]);
    expect(fetchEntityThumbnails).toHaveBeenCalledTimes(1);
    expect(fetchEntityThumbnails).toHaveBeenCalledWith(["album-one", "album-two"], { signal: undefined });
    expect(fetchEntityChildren).toHaveBeenCalledTimes(1);
    expect(fetchEntityChildren).toHaveBeenCalledWith(["album-one", "album-two"], { signal: undefined });
  });
});
