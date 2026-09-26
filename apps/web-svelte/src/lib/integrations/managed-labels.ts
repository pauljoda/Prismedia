import {
  MANAGED_REQUEST_PHASE,
  MANAGED_REQUEST_PHASE_FACTS,
  MANAGED_TRACKING_STATUS,
  MANAGED_TRACKING_STATUS_FACTS,
  type ManagedRequestPhaseCode,
  type ManagedTrackingStatusCode,
} from "$lib/api/generated/codes";

/** One user-facing name per managed request phase, shared by every surface that shows a request. */
export const managedRequestPhaseLabels: Record<ManagedRequestPhaseCode, string> = {
  [MANAGED_REQUEST_PHASE.pendingCreation]: "Request queued",
  [MANAGED_REQUEST_PHASE.creationUncertain]: "Checking request",
  [MANAGED_REQUEST_PHASE.awaitingFiles]: "Waiting for files",
  [MANAGED_REQUEST_PHASE.completed]: "Available in Prismedia",
  [MANAGED_REQUEST_PHASE.rejected]: "Refused by manager",
  [MANAGED_REQUEST_PHASE.cancelled]: "Cancelled",
  [MANAGED_REQUEST_PHASE.ownershipReleased]: "No longer managed",
  [MANAGED_REQUEST_PHASE.remoteRemoved]: "Removed from source",
};

/** One user-facing name per connected-holding status, shared by every surface that shows a holding. */
export const managedTrackingStatusLabels: Record<ManagedTrackingStatusCode, string> = {
  [MANAGED_TRACKING_STATUS.pending]: "Verifying link",
  [MANAGED_TRACKING_STATUS.waitingForFiles]: "Waiting for files",
  [MANAGED_TRACKING_STATUS.tracking]: "Linked",
  [MANAGED_TRACKING_STATUS.needsReview]: "Needs review",
  [MANAGED_TRACKING_STATUS.stale]: "Source unavailable",
  [MANAGED_TRACKING_STATUS.removed]: "Removed from source",
  [MANAGED_TRACKING_STATUS.releasePending]: "Stopping management",
  [MANAGED_TRACKING_STATUS.released]: "No longer managed",
};

/** Creation is unsettled or files have not arrived, so observing again can still advance the request. */
export function isManagedRequestInFlight(phase: ManagedRequestPhaseCode): boolean {
  const facts = MANAGED_REQUEST_PHASE_FACTS[phase];
  return facts.awaitsHolding || facts.awaitsFiles;
}

/** The request still holds its fulfillment reservation, so another request for the same scope would conflict. */
export function managedRequestHoldsFulfillment(phase: ManagedRequestPhaseCode): boolean {
  return MANAGED_REQUEST_PHASE_FACTS[phase].holdsFulfillment;
}

/** The holding is established and usable, so manager controls and ownership release can be offered. */
export function isManagedHoldingEstablished(status: ManagedTrackingStatusCode): boolean {
  return MANAGED_TRACKING_STATUS_FACTS[status].isEstablished;
}
