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
  [ACQUISITION_STATUS.failed]: "Failed",
  [ACQUISITION_STATUS.cancelled]: "Cancelled",
  [ACQUISITION_STATUS.manualImportRequired]: "Manual import",
};

/** Returns the user-facing label for a persisted acquisition status. */
export function acquisitionStatusLabel(status: AcquisitionStatusCode): string {
  return ACQUISITION_STATUS_LABELS[status] ?? status;
}

/** Statuses that represent active background progress and should drive live polling. */
export const ACTIVE_ACQUISITION_STATUSES: readonly AcquisitionStatusCode[] = [
  ACQUISITION_STATUS.pending,
  ACQUISITION_STATUS.searching,
  ACQUISITION_STATUS.queued,
  ACQUISITION_STATUS.downloading,
  ACQUISITION_STATUS.importing,
];
