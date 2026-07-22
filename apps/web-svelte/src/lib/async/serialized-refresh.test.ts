import { describe, expect, it, vi } from "vitest";
import { createSerializedRefresh } from "./serialized-refresh";

describe("createSerializedRefresh", () => {
  it("runs one request at a time and collapses overlap into one trailing refresh", async () => {
    const pending: Array<() => void> = [];
    let activePasses = 0;
    let maxActivePasses = 0;
    const refresh = vi.fn(() => new Promise<void>((resolve) => {
      activePasses += 1;
      maxActivePasses = Math.max(maxActivePasses, activePasses);
      pending.push(() => {
        activePasses -= 1;
        resolve();
      });
    }));
    const request = createSerializedRefresh(refresh);

    const first = request();
    await vi.waitFor(() => expect(refresh).toHaveBeenCalledOnce());
    request();
    request();

    pending.shift()?.();
    await vi.waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
    expect(maxActivePasses).toBe(1);

    pending.shift()?.();
    await first;
    expect(activePasses).toBe(0);
  });
});
