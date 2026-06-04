import { browser } from "$app/environment";
import { createContext } from "$lib/utils/context";
import {
  buildNavCatalog,
  defaultNavPrefs,
  resolveFavorites,
  resolveNav,
  MAX_MOBILE_FAVORITES,
  type NavCatalogItem,
  type NavPrefs,
  type ResolvedNavItem,
  type ResolvedNavSection,
} from "$lib/nav/nav-catalog";
import { fetchNavLayout, saveNavLayout } from "$lib/api/nav-layout";

const ctx = createContext<NavCustomizationStore>("NavCustomization");

/** Debounce window for coalescing rapid edits (e.g. drag-and-drop) into one save. */
const SAVE_DEBOUNCE_MS = 400;

/** Build a section id that does not collide with any existing one. */
function uniqueSectionId(existing: NavPrefs["sections"], label: string): string {
  const base =
    label
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "section";
  let candidate = base;
  let n = 2;
  while (existing.some((s) => s.id === candidate)) {
    candidate = `${base}-${n}`;
    n += 1;
  }
  return candidate;
}

/**
 * Reactive navigation customization. Holds the saved {@link NavPrefs} and the
 * transient edit-mode flag, derives render-ready sections/favorites from the static
 * catalog, and exposes mutations that persist to the server (shared across devices)
 * on every change. The layout is seeded from code defaults, then hydrated once from
 * the server on boot via {@link hydrateFromServer}.
 */
export class NavCustomizationStore {
  /** Static, code-owned catalog of all known routes. */
  readonly catalog: NavCatalogItem[] = buildNavCatalog();

  prefs = $state<NavPrefs>(defaultNavPrefs());
  editing = $state(false);

  /** Sections resolved for rendering (includes hidden items, flagged). */
  resolvedSections = $derived<ResolvedNavSection[]>(resolveNav(this.catalog, this.prefs));
  /** Up to four visible items for the mobile bottom bar. */
  resolvedFavorites = $derived<ResolvedNavItem[]>(
    resolveFavorites(this.catalog, this.prefs, this.resolvedSections),
  );

  /** Set once the user has made an edit, so a late server hydrate never clobbers it. */
  #dirty = false;
  #saveTimer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Load the server-persisted layout once on boot. No-ops if the user has already
   * edited, if a layout fails to load, or if the server has nothing stored (the
   * seeded defaults stay in place). Never re-persists what it loads.
   */
  async hydrateFromServer() {
    if (this.#dirty) return;
    let loaded: NavPrefs | null;
    try {
      loaded = await fetchNavLayout();
    } catch {
      return;
    }
    if (!loaded || this.#dirty) return;
    this.prefs = loaded;
  }

  /** Replace prefs and schedule a debounced save to the server. */
  private commit(next: NavPrefs) {
    this.prefs = next;
    this.#dirty = true;
    if (!browser) return;
    if (this.#saveTimer) clearTimeout(this.#saveTimer);
    this.#saveTimer = setTimeout(() => {
      this.#saveTimer = null;
      void saveNavLayout(this.prefs).catch(() => {});
    }, SAVE_DEBOUNCE_MS);
  }

  private mapSections(fn: (section: NavPrefs["sections"][number]) => NavPrefs["sections"][number]) {
    this.commit({ ...this.prefs, sections: this.prefs.sections.map(fn) });
  }

  toggleEdit() {
    this.editing = !this.editing;
  }

  setEditing(value: boolean) {
    this.editing = value;
  }

  renameSection(id: string, label: string) {
    this.mapSections((s) => (s.id === id ? { ...s, label } : s));
  }

  /** Toggle whether a section is collapsed (items hidden) in the expanded sidebar; persists. */
  toggleSectionCollapsed(id: string) {
    this.mapSections((s) => (s.id === id ? { ...s, collapsed: !s.collapsed } : s));
  }

  /** Append a new empty section and return its generated id. */
  addSection(label: string): string {
    const trimmed = label.trim() || "New Section";
    const id = uniqueSectionId(this.prefs.sections, trimmed);
    this.commit({
      ...this.prefs,
      sections: [...this.prefs.sections, { id, label: trimmed, items: [] }],
    });
    return id;
  }

  /** Remove a section, spilling its items into the previous (or first) section. */
  removeSection(id: string) {
    const idx = this.prefs.sections.findIndex((s) => s.id === id);
    if (idx === -1 || this.prefs.sections.length <= 1) return;
    const sections = this.prefs.sections.map((s) => ({ ...s, items: [...s.items] }));
    const [removed] = sections.splice(idx, 1);
    const target = sections[idx - 1] ?? sections[0];
    if (target) target.items.push(...removed.items);
    this.commit({ ...this.prefs, sections });
  }

  /**
   * Replace the entire section structure at once (desktop drag-and-drop, which
   * owns a live local copy while dragging and commits the whole layout). Labels
   * and item order are taken verbatim; hidden/favorites are untouched.
   */
  setLayout(sections: NavPrefs["sections"]) {
    this.commit({ ...this.prefs, sections: sections.map((s) => ({ ...s, items: [...s.items] })) });
  }

  /** Reorder sections to the given id order (desktop drag-and-drop). */
  setSectionOrder(orderedIds: string[]) {
    const byId = new Map(this.prefs.sections.map((s) => [s.id, s] as const));
    const next = orderedIds.map((id) => byId.get(id)).filter((s): s is NavPrefs["sections"][number] => !!s);
    for (const s of this.prefs.sections) if (!next.includes(s)) next.push(s);
    this.commit({ ...this.prefs, sections: next });
  }

  /** Nudge a section up (-1) or down (+1) in order (mobile buttons). */
  moveSectionByOffset(id: string, offset: number) {
    const idx = this.prefs.sections.findIndex((s) => s.id === id);
    if (idx === -1) return;
    const target = idx + offset;
    if (target < 0 || target >= this.prefs.sections.length) return;
    const sections = [...this.prefs.sections];
    [sections[idx], sections[target]] = [sections[target], sections[idx]];
    this.commit({ ...this.prefs, sections });
  }

  /**
   * Replace a section's ordered item hrefs (desktop drag-and-drop finalize).
   * Any href that lands here is removed from every other section to keep a
   * route in exactly one place.
   */
  setSectionItems(sectionId: string, hrefs: string[]) {
    const incoming = new Set(hrefs);
    this.commit({
      ...this.prefs,
      sections: this.prefs.sections.map((s) => {
        if (s.id === sectionId) return { ...s, items: hrefs };
        return { ...s, items: s.items.filter((h) => !incoming.has(h)) };
      }),
    });
  }

  /** Nudge an item up/down within its section (mobile buttons). */
  moveItemWithinSection(sectionId: string, href: string, offset: number) {
    this.mapSections((s) => {
      if (s.id !== sectionId) return s;
      const idx = s.items.indexOf(href);
      const target = idx + offset;
      if (idx === -1 || target < 0 || target >= s.items.length) return s;
      const items = [...s.items];
      [items[idx], items[target]] = [items[target], items[idx]];
      return { ...s, items };
    });
  }

  /** Move an item to the end of another section (mobile section picker). */
  moveItemToSection(href: string, toSectionId: string) {
    this.commit({
      ...this.prefs,
      sections: this.prefs.sections.map((s) => {
        if (s.id === toSectionId) {
          return s.items.includes(href) ? s : { ...s, items: [...s.items, href] };
        }
        return { ...s, items: s.items.filter((h) => h !== href) };
      }),
    });
  }

  toggleHidden(href: string) {
    const hidden = this.prefs.hidden.includes(href)
      ? this.prefs.hidden.filter((h) => h !== href)
      : [...this.prefs.hidden, href];
    // Hiding a favorite drops it from the bottom bar.
    const mobileFavorites = hidden.includes(href)
      ? this.prefs.mobileFavorites.filter((h) => h !== href)
      : this.prefs.mobileFavorites;
    this.commit({ ...this.prefs, hidden, mobileFavorites });
  }

  /** Whether an href is currently a mobile favorite. */
  isFavorite(href: string): boolean {
    return this.prefs.mobileFavorites.includes(href);
  }

  /** Whether the favorites bar is already full. */
  get favoritesFull(): boolean {
    return this.prefs.mobileFavorites.length >= MAX_MOBILE_FAVORITES;
  }

  /**
   * Toggle an href as a mobile favorite. Returns false (no-op) when trying to
   * add a fifth favorite, so callers can surface a "4 max" hint.
   */
  toggleFavorite(href: string): boolean {
    if (this.prefs.mobileFavorites.includes(href)) {
      this.commit({
        ...this.prefs,
        mobileFavorites: this.prefs.mobileFavorites.filter((h) => h !== href),
      });
      return true;
    }
    if (this.favoritesFull) return false;
    this.commit({ ...this.prefs, mobileFavorites: [...this.prefs.mobileFavorites, href] });
    return true;
  }

  /**
   * Nudge a favorite left (-1) or right (+1) in the bottom-bar order. Operates
   * on the resolved display order (materializing any backfilled favorites) so
   * the chosen order always persists exactly as shown in the preview.
   */
  moveFavorite(href: string, offset: number) {
    const order: string[] = this.resolvedFavorites.map((f) => f.href);
    const idx = order.indexOf(href);
    const target = idx + offset;
    if (idx === -1 || target < 0 || target >= order.length) return;
    [order[idx], order[target]] = [order[target], order[idx]];
    this.commit({ ...this.prefs, mobileFavorites: order });
  }

  /** Restore the seeded layout. */
  reset() {
    this.commit(defaultNavPrefs());
  }
}

export function provideNavCustomization() {
  const store = ctx.provide(new NavCustomizationStore());
  if (browser) void store.hydrateFromServer();
  return store;
}

export const useNavCustomization = ctx.use;
