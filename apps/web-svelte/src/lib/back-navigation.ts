/**
 * Resolve where the back button should navigate given the `from` query
 * param (added by `buildHrefWithFrom` on list pages). Falls back when
 * the param is missing or appears to point at an unsafe cross-origin URL.
 */
export function getBackHref(
  searchParams: URLSearchParams | URL["searchParams"] | null | undefined,
  fallback: string,
): string {
  const raw = searchParams?.get("from");
  if (!raw) return fallback;
  // Only accept same-origin relative paths.
  if (raw.startsWith("/") && !raw.startsWith("//")) return raw;
  return fallback;
}

export function buildHrefWithFrom(href: string, from: string): string {
  if (!from) return href;
  const sep = href.includes("?") ? "&" : "?";
  return `${href}${sep}from=${encodeURIComponent(from)}`;
}
