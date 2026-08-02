import { beforeEach, describe, expect, it, vi } from "vitest";

const consumptionMocks = vi.hoisted(() => ({
  recordEvent: vi.fn(async () => undefined),
  updateConsumption: vi.fn(async () => undefined),
}));

vi.mock("$lib/api/consumption", () => ({
  recordEntityConsumptionEvent: consumptionMocks.recordEvent,
  updateEntityConsumption: consumptionMocks.updateConsumption,
}));

import { MusicConsumptionReporter } from "./music-consumption-reporter";

describe("MusicConsumptionReporter", () => {
  beforeEach(() => vi.clearAllMocks());

  it("records one access per loaded track and bounded active listening", () => {
    let now = 0;
    let positionSeconds = 4;
    const reporter = new MusicConsumptionReporter(
      () => ({ positionSeconds, durationSeconds: 180 }),
      () => now,
    );

    reporter.open("track-1");
    reporter.start();
    reporter.start();
    now = 10_000;
    positionSeconds = 14;
    reporter.heartbeat();
    now = 15_000;
    positionSeconds = 19;
    reporter.pause();

    expect(consumptionMocks.recordEvent).toHaveBeenCalledTimes(1);
    expect(consumptionMocks.recordEvent).toHaveBeenCalledWith(
      "track-1",
      expect.objectContaining({ kind: "accessed", positionSeconds: 4, durationSeconds: 180 }),
    );
    expect(consumptionMocks.updateConsumption).toHaveBeenNthCalledWith(1, "track-1", {
      positionSeconds: 14,
      activitySeconds: 10,
    });
    expect(consumptionMocks.updateConsumption).toHaveBeenNthCalledWith(2, "track-1", {
      positionSeconds: 19,
      activitySeconds: 5,
    });
  });
});
