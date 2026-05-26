import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

function readLocalSource(path: string) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

function readDetailPageCssRule(source: string) {
  const match = source.match(/\.detail-page\s*\{(?<body>[\s\S]*?)\n\s*\}/);
  if (!match?.groups?.body) throw new Error("Could not find .detail-page CSS rule");
  return match.groups.body;
}

describe("gallery detail layout", () => {
  it("uses the same unconstrained EntityDetail page wrapper as other detail pages", () => {
    const source = readLocalSource("./[id]/+page.svelte");
    const rule = readDetailPageCssRule(source);

    expect(rule).toContain("padding: 0;");
    expect(rule).toContain("max-width: none;");
    expect(rule).toContain("margin: 0;");
    expect(rule).not.toContain("max-width: 72rem");
    expect(rule).not.toContain("margin: 0 auto");
  });
});
