import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { EntityThumbnail } from "$lib/api/generated/model";
import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import {
  audiobookAbsoluteTime,
  audiobookDuration,
  audiobookTrackItems,
  resolveAudiobookResume,
} from "./audiobook-playback";

function part(id: string, duration: number | null): AudioTrackListItemDto {
  return { id, title: id, duration } as AudioTrackListItemDto;
}

describe("audiobook playback positions", () => {
  const parts = [part("part-1", 100), part("part-2", 80), part("part-3", 120)];

  it("sums known part durations for the logical book runtime", () => {
    expect(audiobookDuration(parts)).toBe(300);
    expect(audiobookDuration([part("unknown", null), part("known", 40)])).toBe(40);
  });

  it("uses browser-learned durations for parts that have not been probed yet", () => {
    const unprobedParts = [part("part-1", null), part("part-2", null), part("part-3", 120)];
    const runtimeDurations = new Map([
      ["part-1", 100],
      ["part-2", 80],
      // Probed metadata remains authoritative when both values are present.
      ["part-3", 999],
    ]);

    expect(audiobookDuration(unprobedParts, runtimeDurations)).toBe(300);
    expect(audiobookAbsoluteTime(unprobedParts, "part-2", 45, runtimeDurations)).toBe(145);
  });

  it("resolves an absolute resume time to the concrete part and local offset", () => {
    expect(resolveAudiobookResume(parts, 145)).toEqual({
      trackId: "part-2",
      trackOffsetSeconds: 45,
    });
  });

  it("uses the next part at an exact boundary", () => {
    expect(resolveAudiobookResume(parts, 100)).toEqual({
      trackId: "part-2",
      trackOffsetSeconds: 0,
    });
  });

  it("clamps a completed resume position to the end of the last part", () => {
    expect(resolveAudiobookResume(parts, 999)).toEqual({
      trackId: "part-3",
      trackOffsetSeconds: 120,
    });
  });

  it("falls back to the first part when durations are unavailable", () => {
    expect(resolveAudiobookResume([part("part-1", null), part("part-2", null)], 90)).toEqual({
      trackId: "part-1",
      trackOffsetSeconds: 0,
    });
  });

  it("converts a concrete part position back to an absolute book position", () => {
    expect(audiobookAbsoluteTime(parts, "part-3", 25)).toBe(205);
    expect(audiobookAbsoluteTime(parts, "missing", 25)).toBe(0);
  });

  it("maps ordered audio-track children through the shared thumbnail adapter", () => {
    const tracks = audiobookTrackItems({
      id: "book-1",
      childrenByKind: [{
        kind: "audio-track",
        label: "Audio Tracks",
        entities: [
          thumbnail("part-2", "Part 2", 2, "2:00"),
          thumbnail("part-1", "Part 1", 1, "1:30"),
        ],
      }],
    });

    expect(tracks.map((track) => track.id)).toEqual(["part-1", "part-2"]);
    expect(tracks.map((track) => track.duration)).toEqual([90, 120]);
    expect(tracks.every((track) => track.libraryId === "book-1")).toBe(true);
  });
});

function thumbnail(id: string, title: string, sortOrder: number, duration: string): EntityThumbnail {
  return {
    id,
    kind: "audio-track",
    title,
    parentEntityId: "book-1",
    sortOrder,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: THUMBNAIL_HOVER_KIND.none,
    hoverUrl: null,
    hoverImages: [],
    isFavorite: false,
    isOrganized: true,
    isNsfw: false,
    rating: null,
    playCount: 0,
    meta: [{ icon: "duration", label: duration }],
  };
}
