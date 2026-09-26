import { afterEach, describe, expect, it, vi } from "vitest";
import { ManagerActionRejectedError, saveControlAction } from "./managed-controls";
import { ManagedRequestRejectedError, saveReviewedManagedRequest } from "./reviewed-managed-requests";
import { ManagedReleaseRejectedError, saveOwnershipRelease } from "./managed-release";
import type { CommitReviewedManagedRequestInput, CreateManagedControlRequest, ReleaseManagedHoldingRequest } from "./generated/model";
import { PROBLEM_CODE } from "./generated/codes";

describe("Durable manager acceptance failures", () => {
  afterEach(() => vi.unstubAllGlobals());
  const operations = [
    [() => saveControlAction("connection", "holding", {} as CreateManagedControlRequest), ManagerActionRejectedError],
    [() => saveReviewedManagedRequest("connection", {} as CommitReviewedManagedRequestInput), ManagedRequestRejectedError],
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
  it("preserves the typed problem code on a definite managed-request refusal", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: PROBLEM_CODE.requestProposalChanged,
      message: "Review changed",
    }), { status: 409, headers: { "content-type": "application/json" } })));

    await expect(saveReviewedManagedRequest("connection", {} as CommitReviewedManagedRequestInput)).rejects.toMatchObject({
      problemCode: PROBLEM_CODE.requestProposalChanged,
    });
  });
});
