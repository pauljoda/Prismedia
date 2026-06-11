import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import {
  deleteRequestService,
  getRequestDetail as getRequestDetailRequest,
  getRequestServiceOptions,
  listRequestServices,
  saveRequestService,
  searchRequests as searchRequestsRequest,
  submitRequest as submitRequestRequest,
  testRequestService,
  updateRequestService,
} from "$lib/api/generated/prismedia";
import type {
  RequestConnectionTestResponse,
  RequestDetailResponse,
  RequestSearchResponse,
  RequestServiceInstanceSaveRequest,
  RequestServiceInstanceSummary,
  RequestServiceOptionsResponse,
  RequestSubmitRequest,
  RequestSubmitResponse,
} from "$lib/requests/request-model";
import { unwrapGenerated } from "$lib/api/generated-response";

export async function fetchRequestServices(): Promise<RequestServiceInstanceSummary[]> {
  return unwrapGenerated(await listRequestServices(), "Failed to load request services");
}

export async function saveRequestServiceInstance(
  payload: RequestServiceInstanceSaveRequest,
): Promise<RequestServiceInstanceSummary> {
  const request = payload.id
    ? updateRequestService(payload.id, payload)
    : saveRequestService(payload);
  return unwrapGenerated(await request, "Failed to save request service");
}

export async function deleteRequestServiceInstance(id: string): Promise<void> {
  unwrapGenerated(await deleteRequestService(id), "Failed to delete request service", [204]);
}

export async function fetchRequestServiceOptions(id: string): Promise<RequestServiceOptionsResponse> {
  return unwrapGenerated(await getRequestServiceOptions(id), "Failed to load request service options");
}

export async function testRequestServiceInstance(id: string): Promise<RequestConnectionTestResponse> {
  return unwrapGenerated(await testRequestService(id), "Failed to test request service");
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
