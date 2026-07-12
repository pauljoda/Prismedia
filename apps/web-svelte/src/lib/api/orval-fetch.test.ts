import { afterEach, describe, expect, it, vi } from "vitest";
import { PROBLEM_CODE } from "$lib/api/generated/codes";
import { orvalFetch } from "$lib/api/orval-fetch";

describe("orvalFetch problem details", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("preserves the stable problem code alongside the user-facing API message", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({
      code: PROBLEM_CODE.requestProposalChanged,
      message: "Review this proposal again.",
    }), {
      status: 409,
      headers: { "content-type": "application/json" },
    })));

    await expect(orvalFetch("/api/requests/commit-reviewed")).rejects.toMatchObject({
      status: 409,
      problemCode: PROBLEM_CODE.requestProposalChanged,
      message: "Review this proposal again.",
    });
  });
});
