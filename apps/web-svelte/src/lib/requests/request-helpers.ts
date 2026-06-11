import type { RequestProviderKindCode } from "$lib/api/generated/codes";
import type {
  RequestDetailResponse,
  RequestServiceInstanceSummary,
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
  return detail.children
    .filter((child) => child.requestable)
    .map((child) => child.id);
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
