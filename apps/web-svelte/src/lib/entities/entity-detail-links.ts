import type { EntityDetailLink } from "./entity-detail";
import { hasProvider } from "./entity-detail-edit";

/** Compare complete destinations without discarding meaningful paths, queries, or fragments. */
function destination(url: string | null): string | null {
  if (!url) return null;
  try { return new URL(url).href; } catch { return url; }
}

/** Provider rows already link to their source; do not repeat that destination as a website row. */
export function groupEntityLinks(links: EntityDetailLink[]): { label: string; links: EntityDetailLink[] }[] {
  const providers = links.filter(hasProvider);
  const providerDestinations = new Set(providers.map((link) => destination(link.url)).filter(Boolean));
  const websites = links.filter((link) => !hasProvider(link) && (!link.url || !providerDestinations.has(destination(link.url))));
  return [
    { label: "Provider IDs", links: providers },
    { label: "Websites", links: websites },
  ].filter((group) => group.links.length > 0);
}

/** Compact website identity; the complete destination remains in the row and its link. */
export function linkHostname(link: EntityDetailLink): string {
  try { return new URL(link.url ?? link.label).hostname.replace(/^www\./i, ""); }
  catch { return link.label; }
}
