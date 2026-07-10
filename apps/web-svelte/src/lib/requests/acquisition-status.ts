import {
  ACQUISITION_STATUS,
  type AcquisitionStatusCode,
} from "$lib/api/generated/codes";

const ACQUISITION_STATUS_LABELS: Record<AcquisitionStatusCode, string> = {
  [ACQUISITION_STATUS.pending]: "Pending",
  [ACQUISITION_STATUS.searching]: "Searching",
  [ACQUISITION_STATUS.awaitingSelection]: "Choose release",
  [ACQUISITION_STATUS.queued]: "Queued",
  [ACQUISITION_STATUS.downloading]: "Downloading",
  [ACQUISITION_STATUS.downloaded]: "Downloaded",
  [ACQUISITION_STATUS.importing]: "Importing",
  [ACQUISITION_STATUS.imported]: "Imported",
  [ACQUISITION_STATUS.stopping]: "Cleaning up",
  [ACQUISITION_STATUS.failed]: "Failed",
  [ACQUISITION_STATUS.cancelled]: "Cancelled",
  [ACQUISITION_STATUS.manualImportRequired]: "Manual import",
};

/** True when a runtime value belongs to the generated acquisition lifecycle. */
export function acquisitionStatusIsKnown(status: string): status is AcquisitionStatusCode {
  return Object.hasOwn(ACQUISITION_STATUS_LABELS, status);
}

/** Returns the user-facing label for a persisted acquisition status, failing closed on version skew. */
export function acquisitionStatusLabel(status: string): string {
  return acquisitionStatusIsKnown(status) ? ACQUISITION_STATUS_LABELS[status] : "Updating";
}

/** Statuses that represent active background progress and should drive live polling. */
export const ACTIVE_ACQUISITION_STATUSES: readonly AcquisitionStatusCode[] = [
  ACQUISITION_STATUS.pending,
  ACQUISITION_STATUS.searching,
  ACQUISITION_STATUS.queued,
  ACQUISITION_STATUS.downloading,
  ACQUISITION_STATUS.downloaded,
  ACQUISITION_STATUS.importing,
  ACQUISITION_STATUS.stopping,
];

/**
 * True while a status can still change without user input. Unknown values poll as a safe deployment-
 * skew fallback, while every action surface remains locked until the generated client understands them.
 */
export function acquisitionStatusShouldPoll(status: string | null | undefined): boolean {
  if (status == null) return false;
  if (!acquisitionStatusIsKnown(status)) return true;
  return ACTIVE_ACQUISITION_STATUSES.includes(status);
}
