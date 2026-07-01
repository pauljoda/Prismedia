import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import {
  commitRequest as commitRequestRequest,
  getRequestDetail as getRequestDetailRequest,
  searchRequests as searchRequestsRequest,
} from "$lib/api/generated/prismedia";
import type { RequestCommitResponse } from "$lib/api/generated/model";
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

/**
 * Commits a reviewed request: the server creates the wanted library entity/entities up front (the
 * author container plus each picked book, a standalone book, or picked series volumes) and starts one
 * acquisition per requested book. Already-owned / already-in-flight picks are reported per item.
 */
export async function commitRequest(params: {
  kind: RequestMediaKindCode;
  externalId: string;
  selectedChildIds: string[];
}): Promise<RequestCommitResponse> {
  return unwrapGenerated(
    await commitRequestRequest({
      kind: params.kind,
      externalId: params.externalId,
      selectedChildIds: params.selectedChildIds,
    }),
    "Failed to commit the request",
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
