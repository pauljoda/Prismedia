import type { Component } from "svelte";
import { Bookmark, CircleAlert, CloudDownload, Hourglass, Search, TriangleAlert } from "@lucide/svelte";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";

/** Semantic tone for a compact acquisition-status indicator (badge, chip, or roll-up row). */
export type AcquisitionDisplayTone = "downloading" | "searching" | "queued" | "attention" | "failed" | "wanted";

/** Compact display for one acquisition status: a short label, an icon, and a tone for colouring. */
export interface AcquisitionStatusDisplay {
  label: string;
  icon: Component;
  tone: AcquisitionDisplayTone;
}

/**
 * Maps an acquisition status code to its compact display (short label + icon + tone). Shared by the
 * thumbnail's wanted badge and the entity acquisition card's child roll-up so a season, book, or album
 * reads the same everywhere. A null/unknown status is a plain "Wanted" placeholder with no acquisition.
 */
export function acquisitionStatusDisplay(status: string | null | undefined): AcquisitionStatusDisplay {
  switch (status) {
    case ACQUISITION_STATUS.searching:
    case ACQUISITION_STATUS.pending:
      return { label: "Searching", icon: Search, tone: "searching" };
    case ACQUISITION_STATUS.awaitingSelection:
      return { label: "Review", icon: Search, tone: "attention" };
    case ACQUISITION_STATUS.queued:
      return { label: "Queued", icon: Hourglass, tone: "queued" };
    case ACQUISITION_STATUS.downloading:
    case ACQUISITION_STATUS.downloaded:
    case ACQUISITION_STATUS.importing:
      return { label: "Downloading", icon: CloudDownload, tone: "downloading" };
    case ACQUISITION_STATUS.failed:
      return { label: "Failed", icon: CircleAlert, tone: "failed" };
    case ACQUISITION_STATUS.manualImportRequired:
      return { label: "Action", icon: TriangleAlert, tone: "attention" };
    default:
      return { label: "Wanted", icon: Bookmark, tone: "wanted" };
  }
}
