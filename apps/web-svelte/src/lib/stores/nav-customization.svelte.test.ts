import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const saveNavLayout = vi.hoisted(() => vi.fn());
const fetchNavLayout = vi.hoisted(() => vi.fn());
vi.mock("$lib/api/nav-layout", () => ({ saveNavLayout, fetchNavLayout }));

import { NavCustomizationStore } from "./nav-customization.svelte";
import type { NavPrefs } from "$lib/nav/nav-catalog";

describe("NavCustomizationStore", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    saveNavLayout.mockReset().mockResolvedValue(undefined);
    fetchNavLayout.mockReset();
  });
  afterEach(() => vi.useRealTimers());

  it("seeds the refreshed defaults including the Books section", () => {
    const store = new NavCustomizationStore();
    expect(store.prefs.sections.map((s) => s.id)).toContain("books");
  });

  it("debounces edits into a single server save", async () => {
    const store = new NavCustomizationStore();
    store.toggleHidden("/images");
    store.toggleHidden("/videos");
    expect(saveNavLayout).not.toHaveBeenCalled();
    await vi.runAllTimersAsync();
    expect(saveNavLayout).toHaveBeenCalledTimes(1);
    expect(saveNavLayout.mock.calls[0][0].hidden).toContain("/images");
  });

  it("hydrates from the server without re-persisting", async () => {
    const serverPrefs: NavPrefs = {
      v: 1,
      sections: [{ id: "custom", label: "Custom", items: ["/books"] }],
      hidden: [],
      mobileFavorites: [],
    };
    fetchNavLayout.mockResolvedValue(serverPrefs);
    const store = new NavCustomizationStore();
    await store.hydrateFromServer();
    expect(store.prefs.sections.map((s) => s.id)).toEqual(["custom"]);
    await vi.runAllTimersAsync();
    expect(saveNavLayout).not.toHaveBeenCalled();
  });

  it("does not clobber a user edit with a late server hydrate", async () => {
    fetchNavLayout.mockResolvedValue({
      v: 1,
      sections: [{ id: "custom", label: "Custom", items: ["/books"] }],
      hidden: [],
      mobileFavorites: [],
    });
    const store = new NavCustomizationStore();
    store.toggleHidden("/images"); // marks dirty
    await store.hydrateFromServer();
    expect(store.prefs.sections.map((s) => s.id)).not.toEqual(["custom"]);
  });
});
