import { afterEach, describe, expect, it, vi } from "vitest";
import { ManagerActionRejectedError, saveControlAction } from "./managed-controls";
import { ManagedRequestRejectedError, saveManagedRequest } from "./managed-requests";
import { ManagedReleaseRejectedError, saveOwnershipRelease } from "./managed-release";
import type { CreateManagedControlRequest, CreateManagedRequestInput, ReleaseManagedHoldingRequest } from "./generated/model";

describe("Durable manager acceptance failures", () => {
  afterEach(() => vi.unstubAllGlobals());
  const operations = [
    [() => saveControlAction("connection", "holding", {} as CreateManagedControlRequest), ManagerActionRejectedError],
    [() => saveManagedRequest("connection", {} as CreateManagedRequestInput), ManagedRequestRejectedError],
    [() => saveOwnershipRelease("connection", "holding", {} as ReleaseManagedHoldingRequest), ManagedReleaseRejectedError],
  ] as const;
  it.each(operations)("classifies a real HTTP conflict as a definite refusal", async (submit, rejected) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: "Review changed" }), { status: 409, headers: { "content-type": "application/json" } })));
    await expect(submit()).rejects.toBeInstanceOf(rejected);
  });
  it.each(operations)("preserves uncertainty for a server failure after possible acceptance", async (submit, rejected) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Response interrupted", { status: 500 })));
    try { await submit(); throw new Error("Expected failure"); }
    catch (error) { expect(error).not.toBeInstanceOf(rejected); expect(error).toHaveProperty("status", 500); }
  });
});
