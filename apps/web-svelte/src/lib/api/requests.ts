import type { RequestMediaKindCode } from "$lib/api/generated/codes";
import {
  commitEntityRequest as commitEntityRequestRequest,
  commitReviewedRequest as commitReviewedRequestRequest,
  commitMissingChildrenRequest,
  removeWanted as removeWantedRequest,
  reviewEntityRequest as reviewEntityRequestRequest,
  reviewRequest as reviewRequestRequest,
  searchRequestsByPlugin as searchRequestsByPluginRequest,
  syncContainerRequest as syncContainerRequestRequest,
} from "$lib/api/generated/prismedia";
import type {
  ExternalIdentity,
  MissingChildrenCommitResponse,
  RequestCommitResponse,
  RequestReviewResponse,
  RequestSearchResponse,
  ReviewedRequestCommitRequest,
  WantedRemovalResponse,
} from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** Runs one manifest-schema search through the exact plugin selected in Discover. */
export async function searchRequestsByPlugin(params: {
  kind: RequestMediaKindCode;
  pluginId: string;
  fields: Record<string, string>;
  hideNsfw?: boolean;
}): Promise<RequestSearchResponse> {
  return unwrapGenerated(
    await searchRequestsByPluginRequest(
      { kind: params.kind, pluginId: params.pluginId, fields: params.fields },
      { hideNsfw: params.hideNsfw },
    ),
    "Failed to search the selected plugin",
  );
}

/** Loads the canonical, unflattened proposal through the exact plugin that produced a result. */
export async function reviewRequest(params: {
  kind: RequestMediaKindCode;
  pluginId: string;
  externalIdentity: ExternalIdentity;
  hideNsfw?: boolean;
}): Promise<RequestReviewResponse> {
  return unwrapGenerated(
    await reviewRequestRequest(
      {
        kind: params.kind,
        pluginId: params.pluginId,
        externalIdentity: params.externalIdentity,
      },
      { hideNsfw: params.hideNsfw },
    ),
    "Failed to load the request review",
  );
}

/** Routes an existing Entity's stored identities to a capable plugin and returns its canonical review. */
export async function reviewEntityRequest(
  entityId: string,
  kind: RequestMediaKindCode,
  hideNsfw?: boolean,
): Promise<RequestReviewResponse> {
  return unwrapGenerated(
    await reviewEntityRequestRequest({ entityId, kind }, { hideNsfw }),
    "Failed to load the entity request review",
  );
}

/** Commits only proposal ids from a freshly revision-validated review. */
export async function commitReviewedRequest(
  request: ReviewedRequestCommitRequest,
  hideNsfw?: boolean,
): Promise<RequestCommitResponse> {
  return unwrapGenerated(
    await commitReviewedRequestRequest(request, { hideNsfw }),
    "Failed to commit the reviewed request",
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
 * Requests every still-wanted child under an entity — a season's missing episodes — each as its own
 * monitored, auto-grabbing acquisition. Returns how many gaps are now covered and how many exist.
 */
export async function requestMissingChildren(entityId: string): Promise<MissingChildrenCommitResponse> {
  return unwrapGenerated(await commitMissingChildrenRequest({ entityId }), "Failed to search for missing items");
}

/**
 * Removes wanted placeholders: each is deleted (any in-flight download torn down) and blacklisted from
 * container discovery so a followed author/artist sweep never resurrects it. Explicitly requesting the
 * same work again clears its blacklist entry.
 */
export async function removeWantedEntities(entityIds: string[]): Promise<WantedRemovalResponse> {
  return unwrapGenerated<WantedRemovalResponse>(
    await removeWantedRequest({ entityIds }),
    "Failed to remove wanted items",
  );
}

/** Immediately re-syncs a followed author/artist from its provider — the manual counterpart to the daily sweep. */
export async function syncContainerRequest(entityId: string): Promise<void> {
  unwrapGenerated(await syncContainerRequestRequest({ entityId }), "Failed to check for new works", [204]);
}
