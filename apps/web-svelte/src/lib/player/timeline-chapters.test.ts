import { describe, expect, it } from "vitest";
import {
  buildTimelineChapterCues,
  findTimelineChapterTitle,
} from "./timeline-chapters";

describe("buildTimelineChapterCues", () => {
  it("uses explicit marker ends before falling back to the next marker", () => {
    expect(
      buildTimelineChapterCues(
        [
          { id: "a", time: 20, endTime: 45, title: "Scene A" },
          { id: "b", time: 70, title: "Scene B" },
          { id: "c", time: 100, title: "Scene C" },
        ],
        130,
      ),
    ).toEqual([
      { startTime: 20, endTime: 45, text: "Scene A" },
      { startTime: 70, endTime: 100, text: "Scene B" },
      { startTime: 100, endTime: 130, text: "Scene C" },
    ]);
  });

  it("sorts markers and ignores cue ranges that cannot produce a visible chapter", () => {
    expect(
      buildTimelineChapterCues(
        [
          { id: "late", time: 90, title: "Late" },
          { id: "bad", time: 30, endTime: 20, title: "Bad" },
          { id: "outside", time: 150, title: "Outside" },
          { id: "early", time: 10, title: "Early" },
        ],
        100,
      ),
    ).toEqual([
      { startTime: 10, endTime: 30, text: "Early" },
      { startTime: 30, endTime: 90, text: "Bad" },
      { startTime: 90, endTime: 100, text: "Late" },
    ]);
  });

  it("finds the chapter title covering a hovered timestamp", () => {
    const cues = buildTimelineChapterCues(
      [
        { id: "intro", time: 8, endTime: 42, title: "Intro" },
        { id: "speech", time: 90, title: "Speech" },
      ],
      120,
    );

    expect(findTimelineChapterTitle(cues, 10)).toBe("Intro");
    expect(findTimelineChapterTitle(cues, 50)).toBeNull();
    expect(findTimelineChapterTitle(cues, 100)).toBe("Speech");
  });

  it("omits a hover chapter title when no marker chapters exist", () => {
    expect(findTimelineChapterTitle([], 12)).toBeNull();
  });
});
