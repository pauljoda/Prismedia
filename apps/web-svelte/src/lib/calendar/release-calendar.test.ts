import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE, ENTITY_KIND } from "$lib/api/generated/codes";
import {
  calendarDayEventSlice,
  localDateKey,
  monthGridRange,
  releaseCalendarEventHref,
  releaseCalendarEventTitle,
  releaseDateLabel,
} from "./release-calendar";

describe("release calendar", () => {
  it("builds a complete Sunday-to-Saturday grid around the visible month", () => {
    const range = monthGridRange(new Date(2026, 7, 1, 12));

    expect(range.start).toBe("2026-07-26");
    expect(range.end).toBe("2026-09-05");
    expect(range.days).toHaveLength(42);
  });

  it("keeps calendar keys in local date space", () => {
    expect(localDateKey(new Date(2026, 0, 2, 0, 5))).toBe("2026-01-02");
  });

  it("labels generated semantic milestones", () => {
    expect(releaseDateLabel(ENTITY_DATE_TYPE.streamingRelease)).toBe("Streaming release");
  });

  it("adds parent context and resolves nested season links", () => {
    const season = {
      entityId: "season-15",
      kind: ENTITY_KIND.videoSeason,
      title: "Season 15",
      parentEntityId: "series-1",
      parentKind: ENTITY_KIND.videoSeries,
      parentTitle: "It's Always Sunny in Philadelphia",
    };

    expect(releaseCalendarEventTitle(season)).toBe("It's Always Sunny in Philadelphia · Season 15");
    expect(releaseCalendarEventHref(season)).toBe("/series/series-1/seasons/season-15");
  });

  it("bounds crowded month cells and reports every hidden event", () => {
    const events = Array.from({ length: 12 }, (_, index) => `event-${index + 1}`);

    expect(calendarDayEventSlice(events)).toEqual({
      visible: ["event-1", "event-2", "event-3"],
      hiddenCount: 9,
    });
  });
});
