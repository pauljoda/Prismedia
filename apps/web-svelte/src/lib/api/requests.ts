import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import {
  getRequestDetail as getRequestDetailRequest,
  searchRequests as searchRequestsRequest,
} from "$lib/api/generated/prismedia";
import type { RequestDetailResponse, RequestSearchResponse } from "$lib/requests/request-model";
import { unwrapGenerated } from "$lib/api/generated-response";

export async function searchRequests(params: {
  query: string;
  kinds?: RequestMediaKindCode[];
  sources?: RequestProviderKindCode[];
  hideNsfw?: boolean;
}): Promise<RequestSearchResponse> {
  return unwrapGenerated(
    await searchRequestsRequest({
      query: params.query,
      kinds: params.kinds,
      sources: params.sources,
      hideNsfw: params.hideNsfw,
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
      serviceId: params.serviceId || undefined,
    }),
    "Failed to load request detail",
  );
}
