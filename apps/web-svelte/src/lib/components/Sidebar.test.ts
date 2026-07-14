import { render } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import Sidebar from "./Sidebar.svelte";

const nsfwMode = vi.hoisted(() => ({ value: "off" }));

vi.mock("$app/state", () => ({
  page: {
    url: new URL("http://localhost/videos/example"),
  },
}));

vi.mock("$lib/version", () => ({
  APP_VERSION: "0.0.0-test",
  fetchReleaseUpdateStatus: vi.fn().mockResolvedValue(null),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: nsfwMode.value }),
}));

vi.mock("$lib/stores/session.svelte", () => ({
  useSession: () => ({
    user: {
      id: "test-admin",
      username: "admin",
      displayName: "Admin",
      role: "admin",
      allowNsfw: true,
      canCreateLibraries: true,
      enabled: true,
    },
    status: "authed",
    isAdmin: true,
    allowNsfw: true,
    canCreateLibraries: true,
    canManageServer: true,
    logout: () => Promise.resolve(),
    refresh: () => Promise.resolve(),
  }),
}));

vi.mock("$lib/stores/nav-customization.svelte", async () => {
  const { buildNavCatalog, resolveNav, defaultNavPrefs } = await import("$lib/nav/nav-catalog");
  const catalog = buildNavCatalog();
  const prefs = defaultNavPrefs(catalog);
  const store = {
    catalog,
    prefs,
    editing: false,
    favoritesFull: false,
    resolvedSections: resolveNav(catalog, prefs),
    resolvedFavorites: [],
    isFavorite: () => false,
    toggleEdit: () => {},
    setEditing: () => {},
    setLayout: () => {},
    renameSection: () => {},
    setSectionAccent: () => {},
    toggleSectionCollapsed: () => {},
    addSection: () => "",
    removeSection: () => {},
    setSectionOrder: () => {},
    moveSectionByOffset: () => {},
    setSectionItems: () => {},
    moveItemWithinSection: () => {},
    moveItemToSection: () => {},
    toggleHidden: () => {},
    toggleFavorite: () => true,
    reset: () => {},
  };
  return {
    useNavCustomization: () => store,
    provideNavCustomization: () => store,
  };
});

vi.mock("./LogoMark.svelte", () => ({
  default: () => "LogoMark",
}));

describe("Sidebar", () => {
  beforeEach(() => {
    nsfwMode.value = "off";
  });

  it("does not expose development routes in navigation", () => {
    const { container } = render(Sidebar, {
      props: {
        collapsed: false,
        onToggle: vi.fn(),
      },
    });

    expect(container).not.toHaveTextContent("Develop");
    expect(container).not.toHaveTextContent("Dev Tools");
    expect(container.querySelector('a[href="/dev"]')).toBeNull();
    expect(container.querySelector('a[href="/design-language"]')).toBeNull();
  });
});
