import type { EntityDetailLink } from "./entity-detail";
import { externalIdValue, hasProvider } from "./entity-detail-edit";

/** One visible destination, retaining every provider identifier that resolves to it. */
export interface EntityLinkRow {
  key: string;
  link: EntityDetailLink;
  identifiers: { provider: string; value: string }[];
}

/** Compare complete destinations without discarding meaningful paths, queries, or fragments. */
function destination(url: string | null): string | null {
  if (!url) return null;
  try { return new URL(url).href; } catch { return url; }
}

function destinationRows(links: EntityDetailLink[]): EntityLinkRow[] {
  const rows = new Map<string, EntityLinkRow>();
  for (const link of links) {
    const key = JSON.stringify([destination(link.url), ...(link.url ? [] : [link.provider, link.label])]);
    const row = rows.get(key) ?? { key, link, identifiers: [] };
    if (hasProvider(link)) {
      const value = externalIdValue(link.label, link.provider);
      if (!row.identifiers.some((id) => id.provider === link.provider && id.value === value)) {
        row.identifiers.push({ provider: link.provider, value });
      }
    }
    rows.set(key, row);
  }
  return [...rows.values()];
}

/** Group presentation by destination without changing the source links used by the editor. */
export function groupEntityLinks(links: EntityDetailLink[]): { label: string; rows: EntityLinkRow[] }[] {
  const providers = links.filter(hasProvider);
  const providerDestinations = new Set(providers.map((link) => destination(link.url)).filter(Boolean));
  const websites = links.filter((link) => !hasProvider(link) && (!link.url || !providerDestinations.has(destination(link.url))));
  return [
    { label: "Provider IDs", rows: destinationRows(providers) },
    { label: "Websites", rows: destinationRows(websites) },
  ].filter((group) => group.rows.length > 0);
}

/** Compact website identity; the complete destination remains in the row and its link. */
export function linkHostname(link: EntityDetailLink): string {
  try { return new URL(link.url ?? link.label).hostname.replace(/^www\./i, ""); }
  catch { return link.label; }
}
