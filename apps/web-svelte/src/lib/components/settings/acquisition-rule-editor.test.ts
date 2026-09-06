import { describe, expect, it } from "vitest";
import { literalTitleCondition } from "./acquisition-rule-editor";

describe("Basic title rules", () => {
  it("keeps regex characters literal while accepting release separators", () => {
    const condition = literalTitleCondition("Director's Cut (2024)+");
    const pattern = new RegExp(condition.value, "i");
    expect(pattern.test("Movie.Director's-Cut_(2024)+.1080p")).toBe(true);
    expect(pattern.test("Movie Director's Cut 202444")).toBe(false);
    expect(condition.required).toBe(true);
  });
  it("does not treat user-supplied expressions as executable patterns", () => {
    const condition = literalTitleCondition("(a+)+$");
    expect(new RegExp(condition.value).test("aaaaaaaa!")).toBe(false);
    expect(new RegExp(condition.value).test("(a+)+$")).toBe(true);
  });
});
