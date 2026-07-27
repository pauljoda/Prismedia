import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE } from "$lib/api/generated/codes";
import { localDateKey, monthGridRange, releaseDateLabel } from "./release-calendar";

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
});
