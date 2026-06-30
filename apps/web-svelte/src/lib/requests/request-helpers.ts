import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";

/** Display names for request provider families. */
export const REQUEST_PROVIDER_LABELS: Record<string, string> = {
  [REQUEST_PROVIDER_KIND.plugin]: "Plugin",
};

/** Singular display names for request media kinds. */
export const REQUEST_KIND_LABELS: Record<string, string> = {
  [REQUEST_MEDIA_KIND.book]: "Book",
  [REQUEST_MEDIA_KIND.author]: "Author",
};

/** Plural display names for request media kinds, used by filters and section headings. */
export const REQUEST_KIND_LABELS_PLURAL: Record<string, string> = {
  [REQUEST_MEDIA_KIND.book]: "Books",
  [REQUEST_MEDIA_KIND.author]: "Authors",
};

/** "In Library"-style label for items already tracked. */
export function trackedLabel(source: RequestProviderKindCode): string {
  return `In ${REQUEST_PROVIDER_LABELS[source] ?? source}`;
}

/**
 * CSS aspect-ratio for a request result's artwork. Books and authors render as a 2:3 portrait poster.
 */
export function thumbnailAspectForKind(_kind: RequestMediaKindCode): string {
  return "2 / 3";
}

export function inferRequestSourceForKind(kind: RequestMediaKindCode): RequestProviderKindCode | null {
  // Books and authors are fulfilled by the Prismedia plugin acquisition path; this is the fallback when a
  // detail URL is opened without an explicit source (the Discover flow always sets one, but a back nav may not).
  if (kind === REQUEST_MEDIA_KIND.book || kind === REQUEST_MEDIA_KIND.author) return REQUEST_PROVIDER_KIND.plugin;
  return null;
}

export function numericValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}
