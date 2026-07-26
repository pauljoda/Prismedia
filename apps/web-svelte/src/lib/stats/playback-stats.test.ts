import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import type {
  PlaybackStatisticsBucket,
  PlaybackStatisticsKindSlice,
  PlaybackStatisticsRhythmCell,
} from "$lib/api/generated/model";
import {
  aggregateDaySeries,
  buildDailySeries,
  buildDispersion,
  buildRhythm,
  completionRate,
  formatHourLabel,
  formatSpanLabel,
  formatWatchDuration,
  localDayKey,
  niceAxisMax,
  rollingAverage,
  statNumber,
  summarizeCadence,
} from "./playback-stats";

function bucket(
  date: string,
  completedCount: number,
  skippedCount: number,
  watchSeconds = 0,
): PlaybackStatisticsBucket {
  return { date, completedCount, skippedCount, watchSeconds };
}

function slice(
  kind: string,
  totalEvents: number,
  watchSeconds = 0,
): PlaybackStatisticsKindSlice {
  return {
    kind,
    totalEvents,
    completedCount: totalEvents,
    skippedCount: 0,
    distinctEntityCount: 1,
    watchSeconds,
  } as PlaybackStatisticsKindSlice;
}

describe("statNumber", () => {
  it("normalizes the string form the contract uses for wide numerics", () => {
    expect(statNumber("1204")).toBe(1204);
    expect(statNumber(12.5)).toBe(12.5);
  });

  it("falls back to zero for absent or unparseable values", () => {
    expect(statNumber(null)).toBe(0);
    expect(statNumber(undefined)).toBe(0);
    expect(statNumber("not-a-number")).toBe(0);
  });
});

describe("localDayKey", () => {
  it("resolves the calendar day at the requested offset, not at UTC", () => {
    expect(localDayKey("2026-06-18T02:00:00Z", -300)).toBe("2026-06-17");
    expect(localDayKey("2026-06-18T02:00:00Z", 0)).toBe("2026-06-18");
    expect(localDayKey("2026-06-17T23:00:00Z", 120)).toBe("2026-06-18");
  });
});

describe("buildDailySeries", () => {
  it("fills quiet days so gaps stay visible on the timeline", () => {
    const series = buildDailySeries(
      [bucket("2026-06-15", 3, 1, 900), bucket("2026-06-18", 2, 0, 600)],
      "2026-06-15T00:00:00Z",
      "2026-06-18T23:00:00Z",
      0,
    );

    expect(series.map((day) => day.date)).toEqual([
      "2026-06-15",
      "2026-06-16",
      "2026-06-17",
      "2026-06-18",
    ]);
    expect(series[0]).toMatchObject({ totalEvents: 4, watchSeconds: 900 });
    expect(series[1]).toMatchObject({ totalEvents: 0, watchSeconds: 0 });
  });

  it("anchors an all-time window to the first day with activity", () => {
    const series = buildDailySeries(
      [bucket("2026-06-17", 1, 0)],
      "1970-01-01T00:00:00Z",
      "2026-06-18T12:00:00Z",
      0,
    );

    expect(series).toHaveLength(2);
    expect(series[0].date).toBe("2026-06-17");
  });

  it("returns a single trailing day when the window has no activity at all", () => {
    const series = buildDailySeries([], "2026-06-01T00:00:00Z", "2026-06-18T12:00:00Z", 0);

    expect(series.map((day) => day.date)).toContain("2026-06-18");
    expect(series.every((day) => day.totalEvents === 0)).toBe(true);
  });
});

describe("summarizeCadence", () => {
  it("measures active days, streaks, and the busiest day", () => {
    const series = buildDailySeries(
      [
        bucket("2026-06-10", 2, 0, 100),
        bucket("2026-06-13", 9, 1, 500),
        bucket("2026-06-14", 1, 0, 60),
        bucket("2026-06-15", 1, 0, 60),
      ],
      "2026-06-10T00:00:00Z",
      "2026-06-15T12:00:00Z",
      0,
    );

    const cadence = summarizeCadence(series);

    expect(cadence.totalDays).toBe(6);
    expect(cadence.activeDays).toBe(4);
    expect(cadence.longestStreak).toBe(3);
    expect(cadence.currentStreak).toBe(3);
    expect(cadence.busiestDay?.date).toBe("2026-06-13");
    expect(cadence.watchSecondsPerActiveDay).toBe(180);
  });

  it("does not break the current streak on a window that ends mid-day", () => {
    const series = buildDailySeries(
      [bucket("2026-06-16", 4, 0), bucket("2026-06-17", 2, 0)],
      "2026-06-16T00:00:00Z",
      "2026-06-18T09:00:00Z",
      0,
    );

    expect(summarizeCadence(series).currentStreak).toBe(2);
  });

  it("reports an empty window without a busiest day", () => {
    expect(summarizeCadence([])).toMatchObject({
      activeDays: 0,
      currentStreak: 0,
      longestStreak: 0,
      busiestDay: null,
      watchSecondsPerActiveDay: 0,
    });
  });
});

describe("rollingAverage", () => {
  it("smooths spiky counts over the trailing window", () => {
    expect(rollingAverage([0, 6, 3], 2)).toEqual([0, 3, 4.5]);
  });

  it("passes values through for a window of one", () => {
    expect(rollingAverage([1, 2, 3], 1)).toEqual([1, 2, 3]);
  });
});

describe("aggregateDaySeries", () => {
  const series = buildDailySeries(
    [
      bucket("2026-06-01", 2, 1, 100),
      bucket("2026-06-02", 3, 0, 200),
      bucket("2026-06-04", 1, 2, 50),
      bucket("2026-06-05", 4, 0, 400),
    ],
    "2026-06-01T00:00:00Z",
    "2026-06-05T12:00:00Z",
    0,
  );

  it("returns one span per day when the group size is one", () => {
    const spans = aggregateDaySeries(series, 1);

    expect(spans).toHaveLength(5);
    expect(spans[0]).toMatchObject({
      startDate: "2026-06-01",
      endDate: "2026-06-01",
      dayCount: 1,
      totalEvents: 3,
    });
  });

  it("sums grouped days and keeps the span's real bounds", () => {
    const spans = aggregateDaySeries(series, 2);

    expect(spans).toHaveLength(3);
    expect(spans[0]).toMatchObject({
      startDate: "2026-06-01",
      endDate: "2026-06-02",
      dayCount: 2,
      completedCount: 5,
      skippedCount: 1,
      totalEvents: 6,
      watchSeconds: 300,
    });
    // A trailing partial group reports the days it actually covers, not the requested size.
    expect(spans[2]).toMatchObject({ startDate: "2026-06-05", dayCount: 1, totalEvents: 4 });
  });

  it("treats a non-positive group size as one day per span", () => {
    expect(aggregateDaySeries(series, 0)).toHaveLength(5);
  });
});

describe("formatSpanLabel", () => {
  it("names a single-day span in full and a grouped span as a range", () => {
    const series = buildDailySeries(
      [bucket("2026-06-01", 1, 0)],
      "2026-06-01T00:00:00Z",
      "2026-06-04T12:00:00Z",
      0,
    );

    expect(formatSpanLabel(aggregateDaySeries(series, 1)[0])).toContain("June 1");
    expect(formatSpanLabel(aggregateDaySeries(series, 4)[0])).toBe("Jun 1 – Jun 4");
  });
});

describe("buildRhythm", () => {
  const cells: PlaybackStatisticsRhythmCell[] = [
    { dayOfWeek: 0, hour: 21, completedCount: 8, skippedCount: 2, watchSeconds: 600 },
    { dayOfWeek: 3, hour: 9, completedCount: 1, skippedCount: 0, watchSeconds: 60 },
    // Out-of-range cells must never widen the fixed grid.
    { dayOfWeek: 9, hour: 30, completedCount: 5, skippedCount: 5, watchSeconds: 10 },
  ];

  it("expands sparse cells into a full week grid with relative intensity", () => {
    const rhythm = buildRhythm(cells);

    expect(rhythm.cells).toHaveLength(7);
    expect(rhythm.cells.every((row) => row.length === 24)).toBe(true);
    expect(rhythm.totalEvents).toBe(11);
    expect(rhythm.maxCellEvents).toBe(10);
    expect(rhythm.cells[0][21].intensity).toBe(1);
    expect(rhythm.cells[3][9].intensity).toBeCloseTo(0.1);
    expect(rhythm.cells[1][4].intensity).toBe(0);
  });

  it("summarizes the peak cell and the hour and weekday margins", () => {
    const rhythm = buildRhythm(cells);

    expect(rhythm.peak).toMatchObject({ dayOfWeek: 0, hour: 21, totalEvents: 10 });
    expect(rhythm.byHour[21]).toBe(10);
    expect(rhythm.byDayOfWeek[3]).toBe(1);
  });

  it("reports no peak for an empty window", () => {
    expect(buildRhythm([]).peak).toBeNull();
  });
});

describe("buildDispersion", () => {
  it("orders bands along the prism spectrum rather than by size", () => {
    const bands = buildDispersion([
      slice(ENTITY_KIND.audioTrack, 400),
      slice(ENTITY_KIND.video, 100),
      slice(ENTITY_KIND.book, 200),
    ]);

    expect(bands.map((band) => band.kind)).toEqual([
      ENTITY_KIND.video,
      ENTITY_KIND.book,
      ENTITY_KIND.audioTrack,
    ]);
  });

  it("computes each family's share and drops families with no events", () => {
    const bands = buildDispersion([
      slice(ENTITY_KIND.video, 300, 1200),
      slice(ENTITY_KIND.book, 100, 400),
      slice(ENTITY_KIND.image, 0),
    ]);

    expect(bands).toHaveLength(2);
    expect(bands[0]).toMatchObject({ kind: ENTITY_KIND.video, share: 0.75, watchSeconds: 1200 });
    expect(bands[1].share).toBe(0.25);
  });
});

describe("formatWatchDuration", () => {
  it("keeps hours as the unit for library-scale totals", () => {
    expect(formatWatchDuration(42)).toBe("42s");
    expect(formatWatchDuration(900)).toBe("15m");
    expect(formatWatchDuration(3600)).toBe("1h");
    expect(formatWatchDuration(12_000)).toBe("3h 20m");
    expect(formatWatchDuration(1_083_600)).toBe("301h");
  });

  it("clamps negative and empty totals", () => {
    expect(formatWatchDuration(0)).toBe("0s");
    expect(formatWatchDuration(-50)).toBe("0s");
  });
});

describe("formatHourLabel", () => {
  it("renders a short 12-hour clock label", () => {
    expect(formatHourLabel(0)).toBe("12a");
    expect(formatHourLabel(9)).toBe("9a");
    expect(formatHourLabel(12)).toBe("12p");
    expect(formatHourLabel(21)).toBe("9p");
  });
});

describe("completionRate", () => {
  it("is the completed share of all events", () => {
    expect(completionRate(3, 1)).toBe(0.75);
    expect(completionRate(0, 0)).toBe(0);
  });
});

describe("niceAxisMax", () => {
  it("rounds up to a readable gridline without stranding the data low in the plot", () => {
    expect(niceAxisMax(37)).toBe(40);
    expect(niceAxisMax(105)).toBe(120);
    expect(niceAxisMax(8)).toBe(8);
    expect(niceAxisMax(12)).toBe(12);
    expect(niceAxisMax(1400)).toBe(1600);
  });

  it("keeps every axis at least twice the midpoint so gridline labels stay distinct", () => {
    expect(niceAxisMax(1)).toBe(2);
    expect(niceAxisMax(0)).toBe(2);
    expect(niceAxisMax(-4)).toBe(2);
    expect(niceAxisMax(Number.NaN)).toBe(2);
  });
});
