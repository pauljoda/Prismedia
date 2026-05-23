/**
 * Shared client-side writer for list-pref state that must be reflected
 * server-side on the next load function run. Writes the new value to
 * `ui_prefs` and *awaits* the PUT before returning, so callers can then
 * call `invalidate(...)` knowing the server will see the new value on
 * re-run.
 *
 * Use `writeListPrefsAndInvalidate` for the common case; the two steps
 * are split into separate helpers so callers with exotic needs (e.g. a
 * debounce on search text) can compose them differently.
 */

import { invalidate } from "$app/navigation";
import { fetchApi as fetchApi } from "$lib/api/orval-fetch";

export async function writeListPrefs<T>(key: string, value: T): Promise<void> {
  try {
    await fetchApi(`/ui-prefs/${encodeURIComponent(key)}`, {
      method: "PUT",
      body: JSON.stringify({ value }),
    });
  } catch {
    // Best-effort: losing a write silently is better than blocking the
    // UI. Next change will retry.
  }
}

export async function writeListPrefsAndInvalidate<T>(
  key: string,
  value: T,
  invalidateKey: string,
): Promise<void> {
  await writeListPrefs(key, value);
  await invalidate(invalidateKey);
}
