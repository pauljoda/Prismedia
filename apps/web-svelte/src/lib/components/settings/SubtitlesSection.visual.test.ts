import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("SubtitlesSection visual controls", () => {
  it("uses compact language code text and square subtitle sliders", async () => {
    const source = await readFile("src/lib/components/settings/SubtitlesSection.svelte", "utf8");
    const appCss = await readFile("src/app.css", "utf8");

    expect(source).toContain("language-code-token");
    expect(source).toContain("language-code-input");
    expect(source).toContain("allow-compact-input-text");
    expect(appCss).toContain("input:not(.allow-compact-input-text)");
    expect(source).toContain("font-size: 0.68rem");
    expect(source).toContain("font-size: max(16px, 1rem)");
    expect(source).toContain("input.language-code-input");
    expect(source).toContain("font-size: 0.74rem !important");
    expect(source).toContain("settings-menu-option");
    expect(source).toContain("settings-menu-control");
    expect(source).toContain("style={`--range-progress: ${rangeProgress");
    expect(source).toContain('.settings-menu-control input[type="range"]');
    expect(source).toContain("border-radius: 0;");
  });
});
