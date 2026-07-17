import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { ALL_SEARCH_KINDS } from "$lib/search/models";
import { SEARCH_KIND_CONFIG } from "./search-kind-config";

describe("search-kind-config", () => {
  it("covers every exported search kind with a label and href", () => {
    expect(new Set(ALL_SEARCH_KINDS).size).toBe(ALL_SEARCH_KINDS.length);

    for (const kind of ALL_SEARCH_KINDS) {
      expect(SEARCH_KIND_CONFIG[kind]).toBeDefined();
      expect(SEARCH_KIND_CONFIG[kind].label.length).toBeGreaterThan(0);
      expect(SEARCH_KIND_CONFIG[kind].href.startsWith("/")).toBe(true);
    }
  });

  it("preserves Prismedia terminology for people search results", () => {
    expect(SEARCH_KIND_CONFIG[ENTITY_KIND.person].label).toBe("People");
    expect(SEARCH_KIND_CONFIG[ENTITY_KIND.person].href).toBe("/people");
  });

  it("sends series search results to the dedicated series route", () => {
    expect(SEARCH_KIND_CONFIG[ENTITY_KIND.videoSeries].href).toBe("/series");
  });
});
