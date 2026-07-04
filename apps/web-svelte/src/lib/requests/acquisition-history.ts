import { ACQUISITION_HISTORY_EVENT, type AcquisitionHistoryEventCode } from "$lib/api/generated/codes";
import type { BadgeVariant } from "@prismedia/ui-svelte";

/** Human-readable label for an acquisition history event. */
export function acquisitionHistoryEventLabel(event: AcquisitionHistoryEventCode): string {
  return ACQUISITION_HISTORY_EVENT_LABEL[event] ?? event;
}

const ACQUISITION_HISTORY_EVENT_LABEL: Record<AcquisitionHistoryEventCode, string> = {
  [ACQUISITION_HISTORY_EVENT.grabbed]: "Grabbed",
  [ACQUISITION_HISTORY_EVENT.imported]: "Imported",
  [ACQUISITION_HISTORY_EVENT.importFailed]: "Import failed",
  [ACQUISITION_HISTORY_EVENT.downloadFailed]: "Download failed",
  [ACQUISITION_HISTORY_EVENT.blocklisted]: "Blocklisted",
  [ACQUISITION_HISTORY_EVENT.upgraded]: "Upgraded",
  [ACQUISITION_HISTORY_EVENT.removed]: "Removed",
};

/**
 * Badge treatment for an event: success for the two happy outcomes (imported, upgraded), error for the
 * three failure/removal events, accent for the in-motion grab. Colour never carries meaning alone — the
 * event label sits inside the badge.
 */
export function acquisitionHistoryEventVariant(event: AcquisitionHistoryEventCode): BadgeVariant {
  switch (event) {
    case ACQUISITION_HISTORY_EVENT.imported:
    case ACQUISITION_HISTORY_EVENT.upgraded:
      return "success";
    case ACQUISITION_HISTORY_EVENT.importFailed:
    case ACQUISITION_HISTORY_EVENT.downloadFailed:
    case ACQUISITION_HISTORY_EVENT.removed:
      return "error";
    case ACQUISITION_HISTORY_EVENT.blocklisted:
      return "warning";
    default:
      return "accent";
  }
}
