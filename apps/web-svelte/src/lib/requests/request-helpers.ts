import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND, REQUEST_RATING_SOURCE } from "$lib/api/generated/codes";
import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import type {
  RequestDetailResponse,
  RequestRatingValue,
  RequestServiceInstanceSummary,
  RequestServiceOptionsResponse,
  RequestSubmitRequest,
} from "./request-model";

export interface RequestSubmitFormState {
  qualityProfileId: number | null;
  rootFolderPath: string | null;
  metadataProfileId: number | null;
  monitored: boolean;
  searchNow: boolean;
  selectedChildIds: string[];
}

/** Display names for request provider families. */
export const REQUEST_PROVIDER_LABELS: Record<string, string> = {
  [REQUEST_PROVIDER_KIND.radarr]: "Radarr",
  [REQUEST_PROVIDER_KIND.sonarr]: "Sonarr",
  [REQUEST_PROVIDER_KIND.lidarr]: "Lidarr",
  [REQUEST_PROVIDER_KIND.plugin]: "Plugin",
};

/** Singular display names for request media kinds. */
export const REQUEST_KIND_LABELS: Record<string, string> = {
  [REQUEST_MEDIA_KIND.movie]: "Movie",
  [REQUEST_MEDIA_KIND.series]: "Series",
  [REQUEST_MEDIA_KIND.artist]: "Artist",
  [REQUEST_MEDIA_KIND.album]: "Album",
  [REQUEST_MEDIA_KIND.book]: "Book",
  [REQUEST_MEDIA_KIND.plugin]: "Plugin",
};

/** Plural display names for request media kinds, used by filters and section headings. */
export const REQUEST_KIND_LABELS_PLURAL: Record<string, string> = {
  [REQUEST_MEDIA_KIND.movie]: "Movies",
  [REQUEST_MEDIA_KIND.series]: "Series",
  [REQUEST_MEDIA_KIND.artist]: "Artists",
  [REQUEST_MEDIA_KIND.album]: "Albums",
  [REQUEST_MEDIA_KIND.book]: "Books",
  [REQUEST_MEDIA_KIND.plugin]: "Plugins",
};

/** Display names for request rating sources. */
export const REQUEST_RATING_SOURCE_LABELS: Record<string, string> = {
  [REQUEST_RATING_SOURCE.tmdb]: "TMDB",
  [REQUEST_RATING_SOURCE.imdb]: "IMDb",
  [REQUEST_RATING_SOURCE.rottenTomatoes]: "RT",
  [REQUEST_RATING_SOURCE.metacritic]: "MC",
};

/** Formats a rating in its native scale: percent sources as "60%", ten-point as "6.9". */
export function requestRatingDisplay(rating: RequestRatingValue): string {
  const value = numericValue(rating.value) ?? 0;
  return numericValue(rating.scale) === 100 ? `${Math.round(value)}%` : value.toFixed(1);
}

/** "In Radarr"-style label for items already tracked by the upstream service. */
export function trackedLabel(source: RequestProviderKindCode): string {
  return `In ${REQUEST_PROVIDER_LABELS[source] ?? source}`;
}

export function selectDefaultService(
  services: RequestServiceInstanceSummary[],
  kind: RequestProviderKindCode,
): RequestServiceInstanceSummary | null {
  const matching = services.filter((service) => service.kind === kind);
  return matching.find((service) => service.isDefault) ?? matching[0] ?? null;
}

export function defaultSelectedChildIds(detail: RequestDetailResponse): string[] {
  if (detail.source === REQUEST_PROVIDER_KIND.lidarr) return [];

  // Tracked items mirror the upstream monitoring state so an update submits
  // exactly what the checkboxes show; new items preselect everything requestable.
  if (detail.tracked) {
    return detail.children
      .filter((child) => child.monitored === true)
      .map((child) => child.id);
  }

  return detail.children
    .filter((child) => child.requestable)
    .map((child) => child.id);
}

/**
 * CSS aspect-ratio for a request result's artwork. Music kinds use square cover art;
 * everything else uses the standard 2:3 poster shape.
 */
export function thumbnailAspectForKind(kind: RequestMediaKindCode): string {
  return kind === REQUEST_MEDIA_KIND.artist || kind === REQUEST_MEDIA_KIND.album ? "1 / 1" : "2 / 3";
}

export function inferRequestSourceForKind(kind: RequestMediaKindCode): RequestProviderKindCode | null {
  if (kind === REQUEST_MEDIA_KIND.movie) return REQUEST_PROVIDER_KIND.radarr;
  if (kind === REQUEST_MEDIA_KIND.series) return REQUEST_PROVIDER_KIND.sonarr;
  if (kind === REQUEST_MEDIA_KIND.artist || kind === REQUEST_MEDIA_KIND.album) return REQUEST_PROVIDER_KIND.lidarr;
  // Books are fulfilled by the Prismedia plugin path; this is the fallback when a book detail URL is opened
  // without an explicit source (the Discover flow always sets one, but a direct/back nav may not).
  if (kind === REQUEST_MEDIA_KIND.book) return REQUEST_PROVIDER_KIND.plugin;
  return null;
}

export function optionDefaultsForService(
  service: RequestServiceInstanceSummary,
  options: RequestServiceOptionsResponse,
): Pick<RequestSubmitFormState, "qualityProfileId" | "rootFolderPath" | "metadataProfileId" | "searchNow"> {
  const defaultQualityProfileId = numericValue(service.defaultQualityProfileId);
  const defaultMetadataProfileId = numericValue(service.defaultMetadataProfileId);
  return {
    qualityProfileId:
      findNumericOption(options.qualityProfiles, defaultQualityProfileId) ??
      numericValue(options.qualityProfiles[0]?.id),
    rootFolderPath:
      options.rootFolders.find((option) => option.path === service.defaultRootFolderPath)?.path ??
      options.rootFolders[0]?.path ??
      service.defaultRootFolderPath,
    metadataProfileId:
      findNumericOption(options.metadataProfiles, defaultMetadataProfileId) ??
      numericValue(options.metadataProfiles[0]?.id),
    searchNow: service.searchOnRequest,
  };
}

export function buildRequestSubmitPayload(
  detail: RequestDetailResponse,
  service: RequestServiceInstanceSummary,
  form: RequestSubmitFormState,
): RequestSubmitRequest {
  return {
    serviceId: service.id,
    source: detail.source,
    kind: detail.kind,
    externalId: detail.externalId,
    title: detail.title,
    qualityProfileId: form.qualityProfileId,
    rootFolderPath: form.rootFolderPath,
    metadataProfileId: form.metadataProfileId,
    monitored: form.monitored,
    searchNow: form.searchNow,
    selectedChildIds: form.selectedChildIds,
  };
}

export function numericValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function findNumericOption(
  options: RequestServiceOptionsResponse["qualityProfiles"],
  value: number | null,
): number | null {
  if (value === null) return null;
  return options.some((option) => numericValue(option.id) === value) ? value : null;
}
