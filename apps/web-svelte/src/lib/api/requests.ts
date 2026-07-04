import type { MonitorPresetCode, RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
import {
  commitEntityRequest as commitEntityRequestRequest,
  commitRequest as commitRequestRequest,
  getRequestDetail as getRequestDetailRequest,
  removeWanted as removeWantedRequest,
  searchRequests as searchRequestsRequest,
  syncContainerRequest as syncContainerRequestRequest,
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
 * The request-time choices — which library to import into and which quality profile to score releases
 * with — ride along; null falls back to the kind's defaults server-side.
 */
export async function commitRequest(params: {
  kind: RequestMediaKindCode;
  externalId: string;
  selectedChildIds: string[];
  targetLibraryRootId?: string | null;
  profileId?: string | null;
  /**
   * The monitoring preset for a container request (a series, an author, an artist). Recorded on the
   * container monitor to govern whether future syncs auto-monitor newly discovered works; when
   * selectedChildIds is empty it also derives which existing children to request now.
   */
  preset?: MonitorPresetCode | null;
}): Promise<RequestCommitResponse> {
  return unwrapGenerated(
    await commitRequestRequest({
      kind: params.kind,
      externalId: params.externalId,
      selectedChildIds: params.selectedChildIds,
      targetLibraryRootId: params.targetLibraryRootId ?? null,
      profileId: params.profileId ?? null,
      preset: params.preset ?? undefined,
    }),
    "Failed to commit the request",
  );
}

/**
 * Requests an existing library entity by id — a wanted placeholder's "Search for release". The server
 * resolves the entity's kind and provider identity itself and starts the auto-grabbing acquisition.
 */
export async function commitEntityRequest(entityId: string): Promise<RequestCommitResponse> {
  return unwrapGenerated(await commitEntityRequestRequest({ entityId }), "Failed to search for a release");
}

/**
 * Removes wanted placeholders: each is deleted (any in-flight download torn down) and blacklisted from
 * container discovery so a followed author/artist sweep never resurrects it. Explicitly requesting the
 * same work again clears its blacklist entry.
 */
export async function removeWantedEntities(entityIds: string[]): Promise<void> {
  unwrapGenerated(await removeWantedRequest({ entityIds }), "Failed to remove wanted items");
}

/** Immediately re-syncs a followed author/artist from its provider — the manual counterpart to the daily sweep. */
export async function syncContainerRequest(entityId: string): Promise<void> {
  unwrapGenerated(await syncContainerRequestRequest({ entityId }), "Failed to check for new works", [204]);
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
