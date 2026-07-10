import { describe, expect, it, vi } from "vitest";
import { refreshAfterManagedFileRevert } from "./entity-file-management";

describe("refreshAfterManagedFileRevert", () => {
  it("retires the stale acquisition before fetching its replacement and reloading the Entity", async () => {
    const order: string[] = [];
    const acquisition = {
      clearAcquisition: vi.fn(() => order.push("clear")),
      refresh: vi.fn(async () => { order.push("acquisition"); }),
    };
    const reloadEntity = vi.fn(async () => { order.push("entity"); });

    await refreshAfterManagedFileRevert(acquisition, reloadEntity);

    expect(order).toEqual(["clear", "acquisition", "entity"]);
  });
});
