import { beforeEach, describe, expect, it, vi } from "vitest";

const generated = vi.hoisted(() => ({
  removeWanted: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

import { removeWantedEntities } from "./requests";

describe("request API", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns the complete server-confirmed wanted removal result", async () => {
    generated.removeWanted.mockResolvedValue({
      status: 200,
      data: { removed: "1", failures: [] },
    });

    await expect(removeWantedEntities(["wanted-1"])).resolves.toEqual({
      removed: "1",
      failures: [],
    });
    expect(generated.removeWanted).toHaveBeenCalledWith({ entityIds: ["wanted-1"] });
  });

  it("preserves structured cleanup failures returned with HTTP 200", async () => {
    generated.removeWanted.mockResolvedValue({
      status: 200,
      data: {
        removed: 0,
        failures: [{ entityId: "wanted-1", message: "The transfer could not be removed." }],
      },
    });

    await expect(removeWantedEntities(["wanted-1"])).resolves.toEqual({
      removed: 0,
      failures: [{ entityId: "wanted-1", message: "The transfer could not be removed." }],
    });
  });
});
