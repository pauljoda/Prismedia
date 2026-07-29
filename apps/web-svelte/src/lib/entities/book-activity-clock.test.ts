import { describe, expect, it } from "vitest";
import { BookActivityClock } from "./book-activity-clock";

describe("BookActivityClock", () => {
  it("reports only time since the preceding heartbeat and caps stale intervals", () => {
    const clock = new BookActivityClock(60);

    clock.start(1_000);
    expect(clock.take(16_000)).toBe(15);
    expect(clock.take(17_500)).toBe(1.5);
    expect(clock.stop(100_000)).toBe(60);
    expect(clock.take(101_000)).toBeNull();
  });

  it("does not count time before an active reader or player starts", () => {
    const clock = new BookActivityClock(60);

    expect(clock.take(10_000)).toBeNull();
    clock.start(10_000);
    expect(clock.stop(10_000)).toBeNull();
  });
});
