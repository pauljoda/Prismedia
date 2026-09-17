import { INTEGRATION_TRANSFER_MODE, INTEGRATION_TRANSFER_PHASE, SOURCE_ACQUISITION_STATE, type IntegrationTransferModeCode, type IntegrationTransferPhaseCode } from "$lib/api/generated/codes";
import type { IntegrationTransferResponse } from "$lib/api/generated/model";

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

/** Names the action by what Prismedia can actually stop. Source preparation remains owned by its app. */
export function transferCancelLabel(mode: IntegrationTransferModeCode): string {
  return mode === INTEGRATION_TRANSFER_MODE.sourceRequest ? "Stop import"
    : mode === INTEGRATION_TRANSFER_MODE.remoteExecutor ? "Cancel request" : "Cancel download";
}

/** A locally stopped source import must never imply that its upstream download was cancelled. */
export function transferStatusLabel(transfer: Pick<IntegrationTransferResponse, "mode" | "phase" | "cancellationRequested" | "sourceState" | "sourceProgress">): string {
  if (transfer.mode === INTEGRATION_TRANSFER_MODE.sourceRequest && transfer.phase === INTEGRATION_TRANSFER_PHASE.cancelled) return "Import stopped";
  if (transfer.mode === INTEGRATION_TRANSFER_MODE.sourceRequest && transfer.phase === INTEGRATION_TRANSFER_PHASE.awaitingRemote) {
    if (transfer.sourceState === SOURCE_ACQUISITION_STATE.queued) return "Queued in source";
    if (transfer.sourceState === SOURCE_ACQUISITION_STATE.downloading) return transfer.sourceProgress != null
      ? `Source downloading · ${Math.round(Number(transfer.sourceProgress) * 100)}%` : "Downloading in source";
  }
  return transfer.cancellationRequested && !isTransferTerminal(transfer.phase) ? "Cancellation requested" : transferPhaseLabels[transfer.phase];
}

/** Failed source preparation must be retried in its owning app; Prismedia's action only observes again. */
export function transferRetryLabel(transfer: IntegrationTransferResponse): string {
  return transfer.mode === INTEGRATION_TRANSFER_MODE.sourceRequest && transfer.sourceState === SOURCE_ACQUISITION_STATE.failed
    ? "Check again" : transfer.cancellationRequested ? "Retry cancellation" : "Retry import";
}

/** Preserves source diagnostics even when no transport error has occurred. */
export function transferProblem(transfer: IntegrationTransferResponse): string | null {
  return transfer.lastError || transfer.sourceProblem || null;
}
