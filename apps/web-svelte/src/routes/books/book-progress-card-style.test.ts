import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("book progress card styling", () => {
  it("uses the Prism Noir Luxe glass card treatment", async () => {
    const source = await readFile("src/routes/books/[id]/+page.svelte", "utf8");

    expect(source).toContain(".progress-section");
    expect(source).toContain("border-radius: var(--radius-md");
    expect(source).toContain("var(--color-overlay-glass)");
    expect(source).toContain("var(--shadow-elevated)");
    expect(source).toContain("backdrop-filter: blur(var(--glass-blur-md))");
    expect(source).toContain(".progress-track");
    expect(source).toContain("border-radius: var(--radius-xs");
    expect(source).not.toContain("background: var(--color-glass-1");
  });

  it("omits empty chapter placeholders and softens chapter cards", async () => {
    const source = await readFile("src/routes/books/[id]/+page.svelte", "utf8");

    expect(source).not.toContain("No chapters linked to this book yet.");
    expect(source).not.toContain(".empty-children");
    expect(source).toContain(".chapter-card");
    expect(source).toContain("border-radius: var(--radius-sm");
  });
});
