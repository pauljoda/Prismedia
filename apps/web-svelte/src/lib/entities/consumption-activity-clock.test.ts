import { describe, expect, it } from "vitest";
import { ConsumptionActivityClock } from "./consumption-activity-clock";

describe("ConsumptionActivityClock", () => {
  it("reports elapsed active time and restarts its interval", () => {
    const clock = new ConsumptionActivityClock(60);
    clock.start(1_000);

    expect(clock.take(6_500)).toBe(5.5);
    expect(clock.take(8_000)).toBe(1.5);
  });

  it("bounds stale intervals and stops until restarted", () => {
    const clock = new ConsumptionActivityClock(60);
    clock.start(1_000);

    expect(clock.stop(121_000)).toBe(60);
    expect(clock.take(122_000)).toBeNull();
  });
});
