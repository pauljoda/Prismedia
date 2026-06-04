import { fetchApi } from "$lib/api/orval-fetch";
import { normalizeNavPrefs, type NavPrefs } from "$lib/nav/nav-catalog";

/**
 * Server-persisted navigation layout. Mirrors the `.NET` `NavLayoutDocument` contract;
 * `version` corresponds to {@link NavPrefs.v}. The layout is shared across a user's
 * devices — section grouping, order, hidden items, and the mobile dock all live here.
 */
interface NavLayoutDocument {
  version: number;
  sections: NavPrefs["sections"];
  hidden: string[];
  mobileFavorites: string[];
}

interface NavLayoutResponse {
  /** The stored layout, or null when none has been saved (client uses seeded defaults). */
  layout: NavLayoutDocument | null;
}

function toDocument(prefs: NavPrefs): NavLayoutDocument {
  return {
    version: prefs.v,
    sections: prefs.sections,
    hidden: prefs.hidden,
    mobileFavorites: prefs.mobileFavorites,
  };
}

/**
 * Fetch the server-persisted navigation layout. Returns `null` when nothing is stored
 * or the stored document is malformed, so the caller keeps its seeded defaults.
 */
export async function fetchNavLayout(signal?: AbortSignal): Promise<NavPrefs | null> {
  const response = await fetchApi<NavLayoutResponse>("/nav-layout", { signal });
  if (!response?.layout) return null;
  return normalizeNavPrefs(response.layout);
}

/** Persist the navigation layout, replacing any previously stored layout. */
export async function saveNavLayout(prefs: NavPrefs): Promise<void> {
  await fetchApi<NavLayoutResponse>("/nav-layout", {
    method: "PUT",
    body: JSON.stringify(toDocument(prefs)),
  });
}
