import { afterEach, describe, expect, it, vi } from "vitest";
import { AudioStreamRecoveryController } from "./audio-stream-recovery";

afterEach(() => {
  vi.useRealTimers();
});

describe("AudioStreamRecoveryController", () => {
  it("reopens a stalled stream after a short grace period", () => {
    vi.useFakeTimers();
    const recover = vi.fn();
    const controller = new AudioStreamRecoveryController(recover);

    controller.interrupt({ trackId: "track-1", positionSeconds: 89.88 });
    vi.advanceTimersByTime(3_999);
    expect(recover).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(recover).toHaveBeenCalledWith({ trackId: "track-1", positionSeconds: 89.88 });
  });

  it("coalesces waiting and stalled signals for the same interruption", () => {
    vi.useFakeTimers();
    const recover = vi.fn();
    const controller = new AudioStreamRecoveryController(recover);

    controller.interrupt({ trackId: "track-1", positionSeconds: 20 });
    controller.interrupt({ trackId: "track-1", positionSeconds: 21 });
    vi.advanceTimersByTime(4_000);

    expect(recover).toHaveBeenCalledTimes(1);
    expect(recover).toHaveBeenCalledWith({ trackId: "track-1", positionSeconds: 20 });
  });

  it("cancels recovery when playback resumes during the grace period", () => {
    vi.useFakeTimers();
    const recover = vi.fn();
    const controller = new AudioStreamRecoveryController(recover);

    controller.interrupt({ trackId: "track-1", positionSeconds: 20 });
    controller.playing();
    vi.advanceTimersByTime(4_000);

    expect(recover).not.toHaveBeenCalled();
  });

  it("recovers terminal media errors promptly", () => {
    vi.useFakeTimers();
    const recover = vi.fn();
    const controller = new AudioStreamRecoveryController(recover);

    controller.interrupt({ trackId: "track-1", positionSeconds: 20 }, true);
    vi.advanceTimersByTime(250);

    expect(recover).toHaveBeenCalledTimes(1);
  });

  it("backs off when a replacement request also fails", () => {
    vi.useFakeTimers();
    const recover = vi.fn();
    const controller = new AudioStreamRecoveryController(recover);

    controller.interrupt({ trackId: "track-1", positionSeconds: 20 }, true);
    vi.advanceTimersByTime(250);
    controller.interrupt({ trackId: "track-1", positionSeconds: 20 }, true);
    vi.advanceTimersByTime(7_999);
    expect(recover).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(1);
    expect(recover).toHaveBeenCalledTimes(2);
  });
});
