import { beforeEach, describe, expect, it, vi } from "vitest";

const fetchApi = vi.hoisted(() => vi.fn());
vi.mock("$lib/api/orval-fetch", () => ({ fetchApi }));

import { fetchNavLayout, saveNavLayout } from "./nav-layout";
import type { NavPrefs } from "$lib/nav/nav-catalog";

const prefs: NavPrefs = {
  v: 1,
  sections: [{ id: "books", label: "Books", accent: "#0ab3e6", items: ["/books", "/comics", "/ebooks"] }],
  hidden: ["/images"],
  mobileFavorites: ["/files"],
};

describe("fetchNavLayout", () => {
  beforeEach(() => fetchApi.mockReset());

  it("maps the server document (version -> v) into NavPrefs", async () => {
    fetchApi.mockResolvedValue({
      layout: {
        version: 1,
        sections: [{ id: "books", label: "Books", accent: "#0ab3e6", items: ["/books"], collapsed: false }],
        hidden: [],
        mobileFavorites: ["/files"],
      },
    });
    const result = await fetchNavLayout();
    expect(fetchApi).toHaveBeenCalledWith("/nav-layout", { signal: undefined });
    expect(result?.v).toBe(1);
    expect(result?.sections[0].items).toEqual(["/books"]);
    expect(result?.sections[0].accent).toBe("#0ab3e6");
  });

  it("returns null when the server has no stored layout", async () => {
    fetchApi.mockResolvedValue({ layout: null });
    expect(await fetchNavLayout()).toBeNull();
  });

  it("returns null when the stored layout is malformed", async () => {
    fetchApi.mockResolvedValue({ layout: { version: 9, sections: [] } });
    expect(await fetchNavLayout()).toBeNull();
  });
});

describe("saveNavLayout", () => {
  beforeEach(() => fetchApi.mockReset());

  it("PUTs the layout document with version mirrored from v", async () => {
    fetchApi.mockResolvedValue({ layout: null });
    await saveNavLayout(prefs);
    const [path, init] = fetchApi.mock.calls[0];
    expect(path).toBe("/nav-layout");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body)).toEqual({
      version: 1,
      sections: prefs.sections,
      hidden: prefs.hidden,
      mobileFavorites: prefs.mobileFavorites,
    });
  });
});
