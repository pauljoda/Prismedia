import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityThumbnailCard, EntityThumbnailMetaItem } from "$lib/entities/entity-thumbnail";
import type { EntityKind } from "$lib/api/generated/model";

const PROVIDER_PRIORITY = ["tmdb", "imdb", "tvdb", "musicbrainz", "stash"] as const;

export interface IdentifySearchCandidateView {
  externalIds?: Record<string, string> | null;
  title: string;
  year?: number | string | null;
  overview?: string | null;
  posterUrl?: string | null;
  popularity?: number | string | null;
}

function normalizeProviderEntries(candidate: IdentifySearchCandidateView): Array<[string, string]> {
  return Object.entries(candidate.externalIds ?? {})
    .filter((entry): entry is [string, string] => Boolean(entry[0]) && Boolean(entry[1]))
    .sort(([leftProvider], [rightProvider]) => {
      const leftIndex = PROVIDER_PRIORITY.indexOf(leftProvider.toLowerCase() as (typeof PROVIDER_PRIORITY)[number]);
      const rightIndex = PROVIDER_PRIORITY.indexOf(rightProvider.toLowerCase() as (typeof PROVIDER_PRIORITY)[number]);
      if (leftIndex !== -1 || rightIndex !== -1) {
        return (leftIndex === -1 ? Number.MAX_SAFE_INTEGER : leftIndex) -
          (rightIndex === -1 ? Number.MAX_SAFE_INTEGER : rightIndex);
      }
      return leftProvider.localeCompare(rightProvider);
    });
}

function slugTitle(title: string): string {
  const slug = title
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return slug || "candidate";
}

function formatPopularity(value: number): string {
  const rounded = value >= 10 ? value.toFixed(1) : value.toFixed(2);
  return rounded.replace(/\.0+$/, "").replace(/(\.\d*[1-9])0+$/, "$1");
}

/** Builds a stable local-only identity for a provider search candidate. */
export function identifyCandidateKey(candidate: IdentifySearchCandidateView, index: number): string {
  const providerEntry = normalizeProviderEntries(candidate)[0];
  if (providerEntry) {
    return `${providerEntry[0]}:${providerEntry[1]}`;
  }

  return `candidate:${slugTitle(candidate.title)}:${candidate.year ?? "unknown"}:${index}`;
}

function candidateAspectRatio(entityKind: string): EntityThumbnailCard["aspectRatio"] {
  if (entityKind === "studio") return "wide";
  if (entityKind === "person") return { width: 4, height: 5 };
  return "poster";
}

/** Converts an identify search result into the shared list thumbnail view model. */
export function identifyCandidateToThumbnailCard(
  candidate: IdentifySearchCandidateView,
  entityKind: string,
  index: number,
): EntityThumbnailCard {
  const providerEntries = normalizeProviderEntries(candidate);
  const meta: EntityThumbnailMetaItem[] = providerEntries.slice(0, 3).map(([provider, id]) => ({
    icon: "count",
    label: `${provider}: ${id}`,
  }));

  if (typeof candidate.popularity === "number" && Number.isFinite(candidate.popularity) && candidate.popularity > 0) {
    meta.push({
      icon: "count",
      label: `pop ${formatPopularity(candidate.popularity)}`,
    });
  }

  return {
    aspectRatio: candidateAspectRatio(entityKind),
    cover: candidate.posterUrl ? { src: candidate.posterUrl, alt: candidate.title } : null,
    entity: {
      id: identifyCandidateKey(candidate, index),
      kind: entityKind as EntityKind,
      title: candidate.title,
      parentEntityId: null,
      sortOrder: null,
      capabilities: [],
      childrenByKind: [],
      relationships: [],
    },
    hover: { kind: THUMBNAIL_HOVER_KIND.none },
    meta,
    subtitle: candidate.year ? String(candidate.year) : providerEntries[0]?.[0] ?? "Search result",
  };
}
