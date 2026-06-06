import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("dashboard empty library button styling", () => {
  it("uses the shared primary button recipe instead of the legacy flat square CTA", async () => {
    const source = await readFile("src/routes/+page.svelte", "utf8");

    expect(source).toContain("buttonVariants");
    expect(source).toContain("variant: \"primary\"");
    expect(source).toContain("size: \"lg\"");
    expect(source).toContain("min-h-11");
    expect(source).not.toContain("bg-accent-500 hover:bg-accent-400 text-accent-950");
  });
});
