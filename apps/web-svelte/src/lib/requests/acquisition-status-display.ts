import type { Component } from "svelte";
import {
  Bookmark,
  CalendarClock,
  CircleAlert,
  CircleCheck,
  CircleX,
  CloudDownload,
  FolderInput,
  Hourglass,
  LoaderCircle,
  PackageCheck,
  Search,
  TriangleAlert,
} from "@lucide/svelte";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
import { acquisitionStatusLabel } from "$lib/requests/acquisition-status";

/** Semantic tone for a compact acquisition-status indicator (badge, chip, or roll-up row). */
export type AcquisitionLifecycleTone =
  | "downloading"
  | "searching"
  | "queued"
  | "cleanup"
  | "attention"
  | "failed"
  | "done"
  | "muted";

export type AcquisitionDisplayTone = AcquisitionLifecycleTone | "wanted";

/** Compact display for one acquisition status: a short label, an icon, and a tone for colouring. */
export interface AcquisitionStatusDisplay {
  label: string;
  icon: Component;
  tone: AcquisitionDisplayTone;
}

/**
 * Maps an acquisition status code to its compact display (short label + icon + tone). Shared by the
 * thumbnail's wanted badge and the entity acquisition card's child roll-up so a season, book, or album
 * reads the same everywhere. A null status is a plain "Wanted" placeholder with no acquisition;
 * an unknown status is locked as "Updating" until the generated client understands it.
 */
export function acquisitionStatusDisplay(status: string | null | undefined): AcquisitionStatusDisplay {
  if (status == null) return { label: "Wanted", icon: Bookmark, tone: "wanted" };
  return { label: acquisitionStatusLabel(status), ...acquisitionStatusVisual(status) };
}

/** Shared lifecycle icon and tone for thumbnails, acquisition lists, and child activity. */
export function acquisitionStatusVisual(status: string): { icon: Component; tone: AcquisitionLifecycleTone } {
  switch (status) {
    case ACQUISITION_STATUS.waitingForRelease:
    case ACQUISITION_STATUS.manualSearchRequired:
      return { icon: CalendarClock, tone: "queued" };
    case ACQUISITION_STATUS.searching:
      return { icon: Search, tone: "searching" };
    case ACQUISITION_STATUS.pending:
      return { icon: Hourglass, tone: "searching" };
    case ACQUISITION_STATUS.awaitingSelection:
      return { icon: Search, tone: "attention" };
    case ACQUISITION_STATUS.queued:
    case ACQUISITION_STATUS.waitingForDownloadClient:
      return { icon: Hourglass, tone: "queued" };
    case ACQUISITION_STATUS.downloading:
      return { icon: CloudDownload, tone: "downloading" };
    case ACQUISITION_STATUS.downloaded:
      return { icon: PackageCheck, tone: "downloading" };
    case ACQUISITION_STATUS.importing:
      return { icon: FolderInput, tone: "downloading" };
    case ACQUISITION_STATUS.stopping:
      return { icon: LoaderCircle, tone: "cleanup" };
    case ACQUISITION_STATUS.imported:
      return { icon: CircleCheck, tone: "done" };
    case ACQUISITION_STATUS.failed:
      return { icon: CircleAlert, tone: "failed" };
    case ACQUISITION_STATUS.manualImportRequired:
      return { icon: TriangleAlert, tone: "attention" };
    case ACQUISITION_STATUS.cancelled:
      return { icon: CircleX, tone: "muted" };
    default:
      return { icon: LoaderCircle, tone: "cleanup" };
  }
}
