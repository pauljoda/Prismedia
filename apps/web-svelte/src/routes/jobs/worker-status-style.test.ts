import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("jobs worker status styling", () => {
  it("uses a Prism Noir status badge instead of a sharp utility pill", async () => {
    const source = await readFile("src/routes/jobs/+page.svelte", "utf8");

    expect(source).toContain("worker-status-badge");
    expect(source).toContain("border-radius: var(--radius-xs)");
    expect(source).toContain("color-mix(in srgb, var(--color-surface-2) 82%, var(--color-accent-900) 18%)");
    expect(source).toContain("var(--shadow-glow-accent)");
    expect(source).not.toContain("border border-border-subtle bg-surface-2/60 px-2 py-1");
  });
});
