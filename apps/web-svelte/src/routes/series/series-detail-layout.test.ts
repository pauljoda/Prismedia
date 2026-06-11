import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

function readLocalSource(path: string) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

function readCssRule(source: string, selector: string) {
  const escaped = selector.replace(".", "\\.");
  const match = source.match(new RegExp(`${escaped}\\s*\\{(?<body>[\\s\\S]*?)\\n\\s*\\}`));
  if (!match?.groups?.body) throw new Error(`Could not find ${selector} CSS rule`);
  return match.groups.body;
}

describe("series detail layout", () => {
  it("keeps outer spacing owned by the shared app layout", () => {
    const source = readLocalSource("./[id]/+page.svelte");
    const rule = readCssRule(source, ".series-page");

    expect(rule).toContain("padding: 0;");
    expect(rule).toContain("max-width: none;");
    expect(rule).toContain("margin: 0;");
    expect(rule).not.toContain("padding: clamp");
    expect(rule).not.toContain("margin: 0 auto");
  });

  it("keeps season detail outer spacing owned by the shared app layout", () => {
    const source = readLocalSource("./[id]/seasons/[seasonId]/+page.svelte");
    const rule = readCssRule(source, ".season-page");

    expect(rule).toContain("padding: 0;");
    expect(rule).toContain("max-width: none;");
    expect(rule).toContain("margin: 0;");
    expect(rule).not.toContain("padding: clamp");
    expect(rule).not.toContain("margin: 0 auto");
  });

  it("uses shared thumbnails for series cast instead of custom chips", () => {
    const source = readLocalSource("./[id]/+page.svelte");

    expect(source).toContain("EntityCastAndCrewSection");
    expect(source).toContain("creditCards");
    expect(source).toContain("studioCards");
    expect(source).not.toContain('class="credit-chip"');
    expect(source).not.toContain("credit-scroller");
  });

  it("moves series links out of the main details tab", () => {
    const source = readLocalSource("./[id]/+page.svelte");

    expect(source).toContain("tabs={detailTabs}");
    expect(source).toContain("sections={detailSections}");
    expect(source).toContain('id: "metadata"');
    expect(source).toContain('sections: ["links"]');
    expect(source).toContain('sections: ["description", "tags", "cast-and-crew"]');
  });

  it("keeps studio and rendering mode out of the series hero header", () => {
    const source = readLocalSource("./[id]/+page.svelte");

    expect(source).not.toContain("series.renderingMode");
    expect(source).not.toContain("{#snippet heroBadges()}");
    expect(source).not.toContain("meta-item is-studio");
  });

  it("shows labeled aired date, season count, and episode count in the series hero", () => {
    const source = readLocalSource("./[id]/+page.svelte");

    expect(source).toContain("{airedDate.label}");
    expect(source).toContain("{airedDate.display}");
    expect(source).toContain("Seasons:");
    expect(source).toContain("Episodes:");
    expect(source).toContain("loadSeasonEpisodeCounts");
    expect(source).not.toContain("item\" : \"items\"");
  });

  it("moves season links out of the main details tab", () => {
    const source = readLocalSource("./[id]/seasons/[seasonId]/+page.svelte");

    expect(source).toContain("tabs={detailTabs}");
    expect(source).toContain("sections={detailSections}");
    expect(source).toContain('id: "metadata"');
    expect(source).toContain('sections: ["links"]');
    expect(source).toContain('sections: ["description", "tags", "cast-and-crew"]');
  });
});
