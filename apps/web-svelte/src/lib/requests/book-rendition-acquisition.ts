import {
  ACQUISITION_STATUS,
  BOOK_RENDITION,
  type BookRenditionCode,
} from "$lib/api/generated/codes";
import type { AcquisitionDetail, MonitorView } from "$lib/api/generated/model";

export interface BookRenditionOwnership {
  ebook: boolean;
  audiobook: boolean;
}

export interface BookRenditionRow {
  rendition: BookRenditionCode;
  owned: boolean;
  acquisition: AcquisitionDetail | null;
  monitor: MonitorView | null;
}

const RENDITION_ORDER: readonly BookRenditionCode[] = [
  BOOK_RENDITION.ebook,
  BOOK_RENDITION.audiobook,
];

/** Builds the two stable Book rendition slots from their independently persisted request stories. */
export function bookRenditionRows(
  acquisitions: readonly AcquisitionDetail[],
  monitors: readonly MonitorView[],
  ownership: BookRenditionOwnership,
): BookRenditionRow[] {
  return RENDITION_ORDER.map((rendition) => ({
    rendition,
    owned: ownership[rendition],
    acquisition: newestByRendition(acquisitions, rendition, (item) => item.summary.updatedAt),
    monitor: newestByRendition(monitors, rendition, (item) => item.updatedAt),
  }));
}

/**
 * A missing rendition can start a fresh request when it has no request story, or when its newest
 * story is terminal history. Active, reviewable, and failed acquisitions stay owned by their
 * existing AcquisitionPanel actions.
 */
export function bookRenditionCanRequest(row: BookRenditionRow): boolean {
  if (row.owned) return false;
  if (!row.acquisition) return row.monitor === null;
  return row.acquisition.summary.status === ACQUISITION_STATUS.cancelled
    || row.acquisition.summary.status === ACQUISITION_STATUS.imported;
}

function newestByRendition<T extends AcquisitionDetail | MonitorView>(
  items: readonly T[],
  rendition: BookRenditionCode,
  updatedAt: (item: T) => string,
): T | null {
  return items
    .filter((item) => normalizedRendition(item) === rendition)
    .toSorted((left, right) => updatedAt(right).localeCompare(updatedAt(left)))[0] ?? null;
}

function normalizedRendition(item: AcquisitionDetail | MonitorView): BookRenditionCode {
  const value = Object.hasOwn(item, "summary")
    ? (item as AcquisitionDetail).summary.bookRendition
    : (item as MonitorView).bookRendition;
  return value === BOOK_RENDITION.audiobook ? BOOK_RENDITION.audiobook : BOOK_RENDITION.ebook;
}
