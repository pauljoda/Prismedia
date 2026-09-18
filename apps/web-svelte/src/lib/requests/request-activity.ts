import {
  INTEGRATION_TRANSFER_PHASE,
  MANAGED_REQUEST_PHASE,
  MANAGED_TRACKING_STATUS,
} from "$lib/api/generated/codes";
import type {
  IntegrationTransferResponse,
  ManagedRequestResponse,
  ManagedTrackingResponse,
} from "$lib/api/generated/model";

export const REQUEST_ACTIVITY_GROUP = {
  attention: "attention",
  progress: "progress",
  following: "following",
  recent: "recent",
} as const;

export type RequestActivityGroup = (typeof REQUEST_ACTIVITY_GROUP)[keyof typeof REQUEST_ACTIVITY_GROUP];

/** Places persisted transfer work in the section that best describes what the user should do next. */
export function transferActivityGroup(transfer: IntegrationTransferResponse): RequestActivityGroup {
  if (transfer.phase === INTEGRATION_TRANSFER_PHASE.completed || transfer.phase === INTEGRATION_TRANSFER_PHASE.cancelled) {
    return REQUEST_ACTIVITY_GROUP.recent;
  }
  if (transfer.lastError || transfer.phase === INTEGRATION_TRANSFER_PHASE.needsReview || transfer.phase === INTEGRATION_TRANSFER_PHASE.failed) {
    return REQUEST_ACTIVITY_GROUP.attention;
  }
  return REQUEST_ACTIVITY_GROUP.progress;
}

/** Places manager delegation records by intervention, ongoing work, or retained history. */
export function managedRequestActivityGroup(request: ManagedRequestResponse): RequestActivityGroup {
  if (request.phase === MANAGED_REQUEST_PHASE.completed || request.phase === MANAGED_REQUEST_PHASE.cancelled
    || request.phase === MANAGED_REQUEST_PHASE.ownershipReleased || request.phase === MANAGED_REQUEST_PHASE.remoteRemoved) {
    return REQUEST_ACTIVITY_GROUP.recent;
  }
  if (request.reviewRequired || request.problem || request.phase === MANAGED_REQUEST_PHASE.creationUncertain || request.phase === MANAGED_REQUEST_PHASE.rejected) {
    return REQUEST_ACTIVITY_GROUP.attention;
  }
  return REQUEST_ACTIVITY_GROUP.progress;
}

/** Places tracked holdings without confusing remote monitoring with local file availability. */
export function managedTrackingActivityGroup(holding: ManagedTrackingResponse): RequestActivityGroup {
  if (holding.status === MANAGED_TRACKING_STATUS.released || holding.status === MANAGED_TRACKING_STATUS.removed) {
    return REQUEST_ACTIVITY_GROUP.recent;
  }
  if (holding.problem || holding.status === MANAGED_TRACKING_STATUS.needsReview || holding.status === MANAGED_TRACKING_STATUS.stale) {
    return REQUEST_ACTIVITY_GROUP.attention;
  }
  if (holding.status === MANAGED_TRACKING_STATUS.tracking) return REQUEST_ACTIVITY_GROUP.following;
  return REQUEST_ACTIVITY_GROUP.progress;
}

/** A tracked holding supersedes the manager request that created it. */
export function removeTrackedRequests(requests: ManagedRequestResponse[], holdings: ManagedTrackingResponse[]): ManagedRequestResponse[] {
  const trackedIds = new Set(holdings.map(holding => holding.id));
  return requests.filter(request => !trackedIds.has(request.id));
}
