import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("book progress card styling", () => {
  it("renders the shared media progress panel with the material card treatment", async () => {
    const page = await readFile("src/routes/books/[id]/+page.svelte", "utf8");
    expect(page).toContain("<MediaProgressPanel");
    expect(page).toContain('kind="read"');

    const panel = await readFile("src/lib/components/MediaProgressPanel.svelte", "utf8");
    expect(panel).toContain("var(--color-surface-2");
    expect(panel).toContain("border-radius: var(--radius-md");
    // Reuses the shared design-language meter and accent tokens rather than hardcoded values.
    expect(panel).toContain('class="meter-fill"');
    expect(panel).toContain("var(--color-text-accent");
    expect(panel).not.toContain("backdrop-filter");
  });

  it("omits empty chapter placeholders and softens chapter cards", async () => {
    const source = await readFile("src/routes/books/[id]/+page.svelte", "utf8");

    expect(source).not.toContain("No chapters linked to this book yet.");
    expect(source).not.toContain(".empty-children");
    expect(source).toContain("<EntityGrid");
    expect(source).toContain("prefsKey={`book-${book.id}-chapters`}");
  });

  it("offers resume only while reading is incomplete, with start over always available", async () => {
    const page = await readFile("src/routes/books/[id]/+page.svelte", "utf8");
    expect(page).toContain("canResume={!progressDisplay.isComplete}");
    expect(page).toContain("canStartOver");
    expect(page).toContain("onStartOver={startProgressOver}");

    const panel = await readFile("src/lib/components/MediaProgressPanel.svelte", "utf8");
    expect(panel).toContain("{#if canResume && onResume}");
    expect(panel).toContain("{#if canStartOver && onStartOver}");
  });
});
