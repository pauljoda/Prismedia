import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("ChangelogDialog", () => {
  it("matches the sidebar title typography for the dialog heading", async () => {
    const source = await readFile("src/lib/components/ChangelogDialog.svelte", "utf8");

    expect(source).toContain("font-heading");
    expect(source).toContain("tracking-[0.18em]");
    expect(source).toContain("uppercase");
    expect(source).not.toContain("font-display text-[1.55rem]");
    expect(source).not.toContain("tracking-[-0.04em]");
  });
});
