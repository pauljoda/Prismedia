import { describe, expect, it } from "vitest";
import {
  buildNavCatalog,
  defaultNavPrefs,
  normalizeNavPrefs,
  resolveNav,
  type NavPrefs,
} from "./nav-catalog";

describe("defaultNavPrefs", () => {
  it("seeds the refreshed section order with Comics and eBooks under Books", () => {
    const prefs = defaultNavPrefs();
    expect(prefs.sections.map((s) => s.id)).toEqual([
      "overview",
      "video",
      "images",
      "audio",
      "books",
      "browse",
      "operate",
    ]);

    const books = prefs.sections.find((s) => s.id === "books");
    expect(books?.items).toEqual(["/authors", "/books", "/comics", "/ebooks"]);

    const video = prefs.sections.find((s) => s.id === "video");
    expect(video?.items).toEqual(["/movies", "/series", "/videos"]);
    expect(video?.accent).toBe("#b76337");

    const overview = prefs.sections.find((s) => s.id === "overview");
    expect(overview?.items).toEqual(["/", "/search", "/stats"]);

    expect(prefs.mobileFavorites).toEqual(["/files", "/videos", "/galleries", "/people"]);
  });
});

describe("resolveNav", () => {
  it("keeps Comics and eBooks and drops hrefs no longer in the catalog", () => {
    const catalog = buildNavCatalog();
    const prefs: NavPrefs = {
      v: 1,
      sections: [
        { id: "books", label: "Books", items: ["/books", "/comics", "/ebooks", "/gone"] },
      ],
      hidden: [],
      mobileFavorites: [],
    };
    const resolved = resolveNav(catalog, prefs);
    const books = resolved.find((s) => s.id === "books");
    // Kept prefs stay in the user's order; catalog items missing from prefs (Authors) append after.
    expect(books?.items.map((i) => i.href)).toEqual(["/books", "/comics", "/ebooks", "/authors"]);
  });
});

describe("normalizeNavPrefs", () => {
  it("accepts a server document using the `version` key", () => {
    const doc = {
      version: 1,
      sections: [
        {
          id: "books",
          label: "Books",
          items: ["/books"],
          collapsed: true,
          accent: "#0ab3e6",
        },
      ],
      hidden: ["/images"],
      mobileFavorites: ["/files", "/videos", "/galleries", "/people", "/extra"],
    };
    const prefs = normalizeNavPrefs(doc);
    expect(prefs).not.toBeNull();
    expect(prefs!.v).toBe(1);
    expect(prefs!.sections[0]).toEqual({
      id: "books",
      label: "Books",
      items: ["/books"],
      collapsed: true,
      accent: "#0ab3e6",
    });
    expect(prefs!.hidden).toEqual(["/images"]);
    // Mobile favorites are capped at four.
    expect(prefs!.mobileFavorites).toHaveLength(4);
  });

  it("drops unsafe persisted section accent values", () => {
    const prefs = normalizeNavPrefs({
      version: 1,
      sections: [
        { id: "safe", label: "Safe", items: ["/videos"], accent: "#ff571f" },
        { id: "unsafe", label: "Unsafe", items: ["/images"], accent: "url(javascript:bad)" },
      ],
      hidden: [],
      mobileFavorites: [],
    });

    expect(prefs?.sections[0]?.accent).toBe("#ff571f");
    expect(prefs?.sections[1]?.accent).toBeUndefined();
  });

  it("returns null for unusable shapes so callers fall back to defaults", () => {
    expect(normalizeNavPrefs(null)).toBeNull();
    expect(normalizeNavPrefs({ version: 2, sections: [] })).toBeNull();
    expect(normalizeNavPrefs({ version: 1, sections: "nope" })).toBeNull();
    expect(normalizeNavPrefs({ version: 1, sections: [] })).toBeNull();
  });
});
