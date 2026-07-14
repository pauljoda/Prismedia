import { appShellSections } from "$lib/components/app-shell-sections";
import type { AppRouteId } from "$lib/app-routes";
import { colors } from "@prismedia/ui-svelte";

/**
 * A single navigable destination known to the app. The catalog is the static,
 * code-owned source of truth: routes, their labels, icons, and the section they
 * belong to by default. User customization only ever rearranges, regroups,
 * hides, or favorites these items — it never invents or renames routes.
 */
export interface NavCatalogItem {
  /** Stable identity used by all persisted customization. */
  href: AppRouteId;
  /** Display label, always sourced from the catalog (not user-editable). */
  label: string;
  /** Icon key resolved through {@link appShellNavIconMap}. */
  icon: string;
  /** Section this item ships in by default. */
  defaultSectionId: string;
  /** Seeded label for {@link defaultSectionId}, used when recreating sections. */
  defaultSectionLabel: string;
  /** Seeded spectrum color for the section label and active navigation state. */
  defaultSectionAccent: string;
}

/**
 * Persisted, per-device navigation customization. Stored as the `prismedia-nav`
 * cookie. References items by {@link NavCatalogItem.href} only.
 */
export interface NavPrefs {
  v: 1;
  /** User-defined sections in display order; `items` are ordered hrefs. */
  sections: { id: string; label: string; items: string[]; collapsed?: boolean; accent?: string }[];
  /** Hrefs hidden from normal (non-edit) rendering. */
  hidden: string[];
  /** Ordered hrefs shown in the mobile bottom bar (max 4). Mobile-only. */
  mobileFavorites: string[];
}

/** One item as resolved for rendering: catalog data plus current hidden state. */
export interface ResolvedNavItem {
  href: AppRouteId;
  label: string;
  icon: string;
  hidden: boolean;
  accent: string;
}

/** One section as resolved for rendering. */
export interface ResolvedNavSection {
  id: string;
  label: string;
  accent: string;
  items: ResolvedNavItem[];
  /** Whether the section is collapsed (items hidden) in the expanded sidebar. */
  collapsed: boolean;
}

/** Largest number of items the mobile bottom bar can show. */
export const MAX_MOBILE_FAVORITES = 4;

/**
 * Flatten the seeded {@link appShellSections} into a catalog keyed by href.
 * Order is preserved so newly added routes append predictably.
 */
export function buildNavCatalog(): NavCatalogItem[] {
  const catalog: NavCatalogItem[] = [];
  for (const section of appShellSections) {
    for (const item of section.items) {
      catalog.push({
        href: item.href,
        label: item.label,
        icon: item.icon,
        defaultSectionId: section.id,
        defaultSectionLabel: section.kicker,
        defaultSectionAccent: section.accent,
      });
    }
  }
  return catalog;
}

/** Seeded customization: the catalog's own grouping, nothing hidden. */
export function defaultNavPrefs(catalog: NavCatalogItem[] = buildNavCatalog()): NavPrefs {
  const sections: NavPrefs["sections"] = [];
  for (const item of catalog) {
    let section = sections.find((s) => s.id === item.defaultSectionId);
    if (!section) {
      section = {
        id: item.defaultSectionId,
        label: item.defaultSectionLabel,
        accent: item.defaultSectionAccent,
        items: [],
      };
      sections.push(section);
    }
    section.items.push(item.href);
  }
  return { v: 1, sections, hidden: [], mobileFavorites: [...DEFAULT_MOBILE_FAVORITES] };
}

/** The bottom-4 the app shipped with before customization existed. */
export const DEFAULT_MOBILE_FAVORITES = ["/files", "/videos", "/galleries", "/people"];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === "string") : [];
}

function asHexColor(value: unknown): string | undefined {
  return typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value) ? value.toLowerCase() : undefined;
}

/**
 * Permissively validate an arbitrary value (the server layout document) into
 * {@link NavPrefs}. Returns `null` when the shape is unusable so callers fall back
 * to seeded defaults rather than throwing. Accepts either the persisted `version`
 * key or the in-memory `v` key. Unknown sections/items are kept verbatim and
 * reconciled against the live catalog later by {@link resolveNav}.
 */
export function normalizeNavPrefs(parsed: unknown): NavPrefs | null {
  if (!isRecord(parsed)) return null;
  if ((parsed.v ?? parsed.version) !== 1) return null;
  if (!Array.isArray(parsed.sections)) return null;

  const sections: NavPrefs["sections"] = [];
  for (const raw of parsed.sections) {
    if (!isRecord(raw)) continue;
    if (typeof raw.id !== "string" || typeof raw.label !== "string") continue;
    sections.push({
      id: raw.id,
      label: raw.label,
      items: asStringArray(raw.items),
      collapsed: raw.collapsed === true,
      accent: asHexColor(raw.accent),
    });
  }
  if (sections.length === 0) return null;

  return {
    v: 1,
    sections,
    hidden: asStringArray(parsed.hidden),
    mobileFavorites: asStringArray(parsed.mobileFavorites).slice(0, MAX_MOBILE_FAVORITES),
  };
}

/**
 * Reconcile saved {@link NavPrefs} against the live {@link NavCatalogItem}
 * catalog to produce render-ready sections.
 *
 * - Items removed from the catalog are dropped from saved sections.
 * - Catalog items not referenced anywhere (e.g. a newly added route) are
 *   appended to their `defaultSectionId`, recreating that section with its
 *   seeded label if the user deleted it. This keeps new routes visible by
 *   default without any migration.
 */
export function resolveNav(catalog: NavCatalogItem[], prefs: NavPrefs): ResolvedNavSection[] {
  const byHref = new Map(catalog.map((item) => [item.href, item] as const));
  const hidden = new Set(prefs.hidden);
  const referenced = new Set<string>();

  const sections: ResolvedNavSection[] = prefs.sections.map((section) => {
    const seededAccent = catalog.find((item) => item.defaultSectionId === section.id)?.defaultSectionAccent;
    const accent = section.accent ?? seededAccent ?? colors.accent[500];
    const items: ResolvedNavItem[] = [];
    for (const href of section.items) {
      const item = byHref.get(href as AppRouteId);
      if (!item || referenced.has(href)) continue;
      referenced.add(href);
      items.push({
        href: item.href,
        label: item.label,
        icon: item.icon,
        hidden: hidden.has(href),
        accent,
      });
    }
    return {
      id: section.id,
      label: section.label,
      accent,
      items,
      collapsed: section.collapsed === true,
    };
  });

  // Append any catalog item not placed by the saved layout.
  for (const item of catalog) {
    if (referenced.has(item.href)) continue;
    referenced.add(item.href);
    let section = sections.find((s) => s.id === item.defaultSectionId);
    if (!section) {
      section = {
        id: item.defaultSectionId,
        label: item.defaultSectionLabel,
        accent: item.defaultSectionAccent,
        items: [],
        collapsed: false,
      };
      sections.push(section);
    }
    section.items.push({
      href: item.href,
      label: item.label,
      icon: item.icon,
      hidden: hidden.has(item.href),
      accent: section.accent,
    });
  }

  return sections;
}

/**
 * Resolve the mobile bottom-bar favorites: keep saved favorites that still
 * exist and are visible, cap at {@link MAX_MOBILE_FAVORITES}, and backfill from
 * the default set then remaining visible items so the bar is always full.
 */
export function resolveFavorites(
  catalog: NavCatalogItem[],
  prefs: NavPrefs,
  sections: ResolvedNavSection[] = resolveNav(catalog, prefs),
): ResolvedNavItem[] {
  const visible = new Map<string, ResolvedNavItem>();
  for (const section of sections) {
    for (const item of section.items) {
      if (!item.hidden) visible.set(item.href, item);
    }
  }

  const ordered: ResolvedNavItem[] = [];
  const take = (href: string) => {
    const item = visible.get(href);
    if (item && !ordered.some((x) => x.href === href)) ordered.push(item);
  };

  for (const href of prefs.mobileFavorites) take(href);
  for (const href of DEFAULT_MOBILE_FAVORITES) {
    if (ordered.length >= MAX_MOBILE_FAVORITES) break;
    take(href);
  }
  for (const item of visible.values()) {
    if (ordered.length >= MAX_MOBILE_FAVORITES) break;
    take(item.href);
  }

  return ordered.slice(0, MAX_MOBILE_FAVORITES);
}
