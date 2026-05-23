import { page } from "$app/state";

/**
 * Returns the current page location as a string suitable for the `from`
 * query parameter (e.g. `/series?series=abc`). Reactive via $app/state.
 */
export function currentPath(): string {
  const path = page.url.pathname;
  const qs = page.url.search;
  return qs ? `${path}${qs}` : path;
}
