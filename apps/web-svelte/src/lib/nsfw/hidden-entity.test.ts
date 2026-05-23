import { describe, expect, it } from "vitest";
import { isHiddenEntityNotFoundError } from "./hidden-entity";

describe("hidden entity redirects", () => {
  it("recognizes API problem code and message forms for hidden detail responses", () => {
    expect(isHiddenEntityNotFoundError(new Error("entity_not_found"))).toBe(true);
    expect(isHiddenEntityNotFoundError(new Error("Entity 'abc' was not found."))).toBe(true);
    expect(isHiddenEntityNotFoundError(new Error("Network failed"))).toBe(false);
  });
});
