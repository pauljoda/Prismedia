import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import type {
  RequestDetailResponse,
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

export function selectDefaultService(
  services: RequestServiceInstanceSummary[],
  kind: RequestProviderKindCode,
): RequestServiceInstanceSummary | null {
  const matching = services.filter((service) => service.kind === kind);
  return matching.find((service) => service.isDefault) ?? matching[0] ?? null;
}

export function defaultSelectedChildIds(detail: RequestDetailResponse): string[] {
  if (detail.source === REQUEST_PROVIDER_KIND.lidarr) return [];

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
