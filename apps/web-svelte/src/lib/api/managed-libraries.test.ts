import { describe, expect, it, vi } from "vitest";
import { refreshTracking, saveManagedTracking } from "./managed-libraries";
import { refreshManagedHolding, trackManagedHolding } from "$lib/api/generated/prismedia";

vi.mock("$lib/api/generated/prismedia", () => ({
  refreshManagedHolding: vi.fn(),
  trackManagedHolding: vi.fn(),
}));

describe("managed library writes", () => {
  it("accepts the API's 202 response for a saved link and queued refresh", async () => {
    const saved = { id: "holding", title: "Run", status: "pending" };
    vi.mocked(trackManagedHolding).mockResolvedValueOnce({ status: 202, data: saved } as never);
    vi.mocked(refreshManagedHolding).mockResolvedValueOnce({ status: 202, data: undefined } as never);

    expect(await saveManagedTracking("connection", {} as never)).toBe(saved);
    await expect(refreshTracking("connection", "holding")).resolves.toBeUndefined();
  });

  it("keeps an actual rejected link visible as an error", async () => {
    vi.mocked(trackManagedHolding).mockResolvedValueOnce({ status: 409, data: { title: "Scope changed" } } as never);

    await expect(saveManagedTracking("connection", {} as never)).rejects.toThrow("Scope changed");
  });
});
