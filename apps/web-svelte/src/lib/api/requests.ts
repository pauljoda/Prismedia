import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import {
  getRequestDetail as getRequestDetailRequest,
  listRequestServices,
  searchRequests as searchRequestsRequest,
  submitRequest as submitRequestRequest,
} from "$lib/api/generated/prismedia";
import type {
  RequestDetailResponse,
  RequestSearchResponse,
  RequestServiceInstanceSummary,
  RequestSubmitRequest,
  RequestSubmitResponse,
} from "$lib/requests/request-model";
import { unwrapGenerated } from "$lib/api/generated-response";

export async function fetchRequestServices(): Promise<RequestServiceInstanceSummary[]> {
  return unwrapGenerated(await listRequestServices(), "Failed to load request services");
}

export async function searchRequests(params: {
  query: string;
  kinds?: RequestMediaKindCode[];
  sources?: RequestProviderKindCode[];
}): Promise<RequestSearchResponse> {
  return unwrapGenerated(
    await searchRequestsRequest({
      query: params.query,
      kinds: params.kinds,
      sources: params.sources,
    }),
    "Failed to search request providers",
  );
}

export async function fetchRequestDetail(params: {
  source: RequestProviderKindCode;
  kind: RequestMediaKindCode;
  externalId: string;
  serviceId?: string | null;
}): Promise<RequestDetailResponse> {
  return unwrapGenerated(
    await getRequestDetailRequest(params.source, params.kind, params.externalId, {
      serviceId: params.serviceId ?? undefined,
    }),
    "Failed to load request detail",
  );
}

export async function submitRequest(payload: RequestSubmitRequest): Promise<RequestSubmitResponse> {
  return unwrapGenerated(await submitRequestRequest(payload), "Failed to submit request");
}
