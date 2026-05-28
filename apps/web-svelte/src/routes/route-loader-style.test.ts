import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("route loader styling", () => {
  it("uses circular fields and ripple rings for the loading animation", async () => {
    const source = await readFile("src/app.css", "utf8");

    expect(source).toContain("route-loader-ripple-circle");
    expect(source).toContain(".route-loader-ripple-ring");
    expect(source).toContain(".route-loader-core-field");
    expect(source.match(/border-radius: 9999px;/g)?.length).toBeGreaterThanOrEqual(2);
    expect(source).not.toContain("route-loader-ripple-square");
    expect(source).not.toContain("sharp squares");
  });
});
