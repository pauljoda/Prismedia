import { beforeEach, describe, expect, it, vi } from "vitest";

const playbackMocks = vi.hoisted(() => ({
  recordEvent: vi.fn(async () => undefined),
  updatePlayback: vi.fn(async () => undefined),
}));

vi.mock("$lib/api/playback", () => ({
  recordEntityPlaybackEvent: playbackMocks.recordEvent,
  updateEntityPlayback: playbackMocks.updatePlayback,
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

    expect(playbackMocks.recordEvent).toHaveBeenCalledTimes(1);
    expect(playbackMocks.recordEvent).toHaveBeenCalledWith(
      "track-1",
      expect.objectContaining({ kind: "accessed", positionSeconds: 4, durationSeconds: 180 }),
    );
    expect(playbackMocks.updatePlayback).toHaveBeenNthCalledWith(1, "track-1", {
      resumeSeconds: 14,
      durationSeconds: 10,
    });
    expect(playbackMocks.updatePlayback).toHaveBeenNthCalledWith(2, "track-1", {
      resumeSeconds: 19,
      durationSeconds: 5,
    });
  });
});
