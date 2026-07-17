import { describe, expect, it } from "vitest";
import { PROBLEM_CODE } from "$lib/api/generated/codes";
import { ApiError } from "$lib/api/orval-fetch";
import { isHiddenEntityNotFoundError } from "./hidden-entity";

describe("hidden entity redirects", () => {
  it("matches the generated problem code, never message text", () => {
    expect(isHiddenEntityNotFoundError(new ApiError("Not found", 404, PROBLEM_CODE.entityNotFound))).toBe(true);
    expect(isHiddenEntityNotFoundError(new ApiError("Not found", 404, PROBLEM_CODE.acquisitionNotFound))).toBe(false);
    expect(isHiddenEntityNotFoundError(new Error(PROBLEM_CODE.entityNotFound))).toBe(true);
    expect(isHiddenEntityNotFoundError(new Error("Entity 'abc' was not found."))).toBe(false);
    expect(isHiddenEntityNotFoundError(new Error("Network failed"))).toBe(false);
  });
});
