import { INTEGRATION_TRANSFER_PHASE, type IntegrationTransferPhaseCode } from "$lib/api/generated/codes";

/** Distinguishes remote execution, verified local import, and receipt delivery. */
export const transferPhaseLabels: Record<IntegrationTransferPhaseCode, string> = {
  [INTEGRATION_TRANSFER_PHASE.pendingSubmission]: "Queued",
  [INTEGRATION_TRANSFER_PHASE.submissionUncertain]: "Checking submission",
  [INTEGRATION_TRANSFER_PHASE.awaitingRemote]: "Running remotely",
  [INTEGRATION_TRANSFER_PHASE.awaitingArtifacts]: "Checking outputs",
  [INTEGRATION_TRANSFER_PHASE.transferring]: "Downloading and verifying",
  [INTEGRATION_TRANSFER_PHASE.importing]: "Importing",
  [INTEGRATION_TRANSFER_PHASE.awaitingAcknowledgement]: "Imported · sending receipt",
  [INTEGRATION_TRANSFER_PHASE.completed]: "Imported",
  [INTEGRATION_TRANSFER_PHASE.needsReview]: "Needs review",
  [INTEGRATION_TRANSFER_PHASE.failed]: "Failed",
  [INTEGRATION_TRANSFER_PHASE.cancelled]: "Cancelled",
};

/** Terminal operations cannot be retried into another acquisition. */
export function isTransferTerminal(phase: IntegrationTransferPhaseCode): boolean {
  return phase === INTEGRATION_TRANSFER_PHASE.completed || phase === INTEGRATION_TRANSFER_PHASE.failed || phase === INTEGRATION_TRANSFER_PHASE.cancelled;
}
