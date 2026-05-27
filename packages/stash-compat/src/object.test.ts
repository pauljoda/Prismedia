import { describe, expect, it } from "vitest";
import { getOwnString, hasOwnField } from "./object";

describe("object helpers", () => {
  it("ignores inherited properties when reading untrusted input", () => {
    const input = Object.create({ url: "https://inherited.example/scene" }) as Record<string, unknown>;

    expect(hasOwnField(input, "url")).toBe(false);
    expect(getOwnString(input, "url")).toBeUndefined();
  });

  it("reads own string properties", () => {
    expect(getOwnString({ url: "https://example.com/scene" }, "url")).toBe("https://example.com/scene");
    expect(getOwnString({ url: 42 }, "url")).toBeUndefined();
  });
});
