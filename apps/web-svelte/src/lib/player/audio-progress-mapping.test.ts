import { describe, expect, it } from "vitest";
import { PROGRESS_UNIT } from "$lib/api/generated/codes";
import type { PlaybackProgressMapping } from "$lib/api/generated/model";
import { resolvePlaybackProgressMappingForTime } from "./audio-progress-mapping";

function mapping(start: number, end: number): PlaybackProgressMapping {
  return {
    itemId: "audio-1",
    currentEntityId: "book-1",
    unit: PROGRESS_UNIT.cfi,
    startIndex: start * 10,
    endIndex: end * 10,
    total: 2000,
    mode: null,
    sourceStartSeconds: start,
    sourceEndSeconds: end,
  };
}

describe("audio progress mapping", () => {
  it("selects the embedded chapter window that owns the physical playback time", () => {
    const first = mapping(0, 100);
    const second = mapping(100, 200);

    expect(resolvePlaybackProgressMappingForTime([first, second], "audio-1", 50)).toBe(first);
    expect(resolvePlaybackProgressMappingForTime([first, second], "audio-1", 100)).toBe(second);
    expect(resolvePlaybackProgressMappingForTime([first, second], "audio-1", 150)).toBe(second);
  });
});
