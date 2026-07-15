import { describe, expect, it, vi } from "vitest";
import {
  EntityKind,
  ThumbnailHoverKind,
  type EntityListResponse,
  type EntityThumbnail,
  type ListAudioTracksParams,
} from "$lib/api/generated/model";
import { loadAudioTrackLibrary } from "$lib/entities/audio-track-library";

function thumbnail(
  id: string,
  kind: EntityThumbnail["kind"],
  title: string,
  parentEntityId: string | null,
  coverUrl: string | null = null,
): EntityThumbnail {
  return {
    id,
    kind,
    title,
    parentEntityId,
    sortOrder: null,
    coverUrl,
    coverThumbUrl: null,
    hoverKind: ThumbnailHoverKind.none,
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
  };
}

describe("loadAudioTrackLibrary", () => {
  it("loads every cursor page and hydrates album artwork plus artist and album labels", async () => {
    const firstTrack = thumbnail("track-1", EntityKind["audio-track"], "First", "album-1", "covers/album.jpg");
    const secondTrack = thumbnail("track-2", EntityKind["audio-track"], "Second", "album-1");
    const album = thumbnail("album-1", EntityKind["audio-library"], "The Album", "artist-1");
    const artist = thumbnail("artist-1", EntityKind["music-artist"], "The Artist", null);
    const listPage = vi.fn(async (params: ListAudioTracksParams): Promise<EntityListResponse> =>
      params.cursor
        ? { items: [firstTrack, secondTrack], nextCursor: null, totalCount: 2 }
        : { items: [firstTrack], nextCursor: "page-2", totalCount: 2 },
    );
    const fetchThumbnails = vi.fn(async (ids: string[]) =>
      ids[0] === "album-1" ? [album] : [artist],
    );

    const result = await loadAudioTrackLibrary(
      { hideNsfw: true },
      { listPage, fetchThumbnails },
    );

    expect(listPage).toHaveBeenCalledTimes(2);
    expect(listPage.mock.calls.map(([params]) => params)).toEqual([
      { cursor: undefined, hideNsfw: true, limit: 1_000 },
      { cursor: "page-2", hideNsfw: true, limit: 1_000 },
    ]);
    expect(fetchThumbnails.mock.calls.map(([ids]) => ids)).toEqual([
      ["album-1"],
      ["artist-1"],
    ]);
    expect(result.albumCoverUrls).toEqual({ "album-1": "/covers/album.jpg" });
    expect(result.tracks.map((track) => ({
      id: track.id,
      libraryId: track.libraryId,
      album: track.embeddedAlbum,
      artist: track.embeddedArtist,
    }))).toEqual([
      { id: "track-1", libraryId: "album-1", album: "The Album", artist: "The Artist" },
      { id: "track-2", libraryId: "album-1", album: "The Album", artist: "The Artist" },
    ]);
  });

  it("stops safely when an API repeats a cursor", async () => {
    const track = thumbnail("track-1", EntityKind["audio-track"], "Track", null);
    const listPage = vi.fn(async (): Promise<EntityListResponse> => ({
      items: [track],
      nextCursor: "same-cursor",
      totalCount: 1,
    }));

    const result = await loadAudioTrackLibrary(
      { hideNsfw: false },
      { listPage, fetchThumbnails: async () => [] },
    );

    expect(listPage).toHaveBeenCalledTimes(2);
    expect(result.tracks.map((item) => item.id)).toEqual(["track-1"]);
  });
});
