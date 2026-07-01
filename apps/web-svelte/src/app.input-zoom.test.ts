import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

/**
 * iOS Safari zooms the viewport when a focused text input/textarea has a computed
 * font-size below 16px. app.css guards every input/textarea with a 16px floor and
 * exempts native selects via `.allow-compact-input-text`. Compact text inputs that
 * opt out for a dense desktop look must still be re-floored on touch devices, or
 * they reintroduce the zoom (APP-141).
 */
describe("global input zoom guard", () => {
  it("floors regular inputs/textareas at 16px and re-floors compact text inputs on touch", async () => {
    const appCss = await readFile("src/app.css", "utf8");

    // Base floor for all non-opted-out inputs/textareas.
    expect(appCss).toContain("input:not(.allow-compact-input-text)");
    expect(appCss).toMatch(/font-size:\s*max\(16px,\s*1em\)\s*!important/);

    // Compact text inputs/textareas must regain the floor on coarse pointers,
    // while selects stay exempt (they open the iOS picker and never zoom). The base floor and the
    // compact re-floor are separate (pointer: coarse) blocks, so find the one that owns the re-floor.
    const coarseBlocks = appCss.match(/@media\s*\(pointer:\s*coarse\)\s*\{[\s\S]*?\n {2}\}/g) ?? [];
    const block = coarseBlocks.find((candidate) => candidate.includes("input.allow-compact-input-text"));
    expect(block, "expected a (pointer: coarse) re-floor block for compact text inputs").toBeDefined();
    expect(block).toContain("textarea.allow-compact-input-text");
    expect(block).not.toContain("select.allow-compact-input-text");
    expect(block).toMatch(/font-size:\s*max\(16px,\s*1em\)\s*!important/);
  });
});
