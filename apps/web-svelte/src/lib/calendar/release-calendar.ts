import { ENTITY_DATE_TYPE, type EntityDateTypeCode } from "$lib/api/generated/codes";
import type { ReleaseCalendarEvent } from "$lib/api/generated/model";
import { resolveEntityHref } from "$lib/entities/entity-codes";

export const CALENDAR_DAY_VISIBLE_EVENT_LIMIT = 3;

export const RELEASE_DATE_LABELS: Record<EntityDateTypeCode, string> = {
  [ENTITY_DATE_TYPE.announcement]: "Announcement",
  [ENTITY_DATE_TYPE.premiere]: "Premiere",
  [ENTITY_DATE_TYPE.theatricalRelease]: "Theatrical release",
  [ENTITY_DATE_TYPE.streamingRelease]: "Streaming release",
  [ENTITY_DATE_TYPE.digitalRelease]: "Digital / VOD release",
  [ENTITY_DATE_TYPE.physicalRelease]: "Physical release",
  [ENTITY_DATE_TYPE.air]: "Air date",
  [ENTITY_DATE_TYPE.firstAir]: "First air date",
  [ENTITY_DATE_TYPE.lastAir]: "Last air date",
  [ENTITY_DATE_TYPE.publication]: "Publication",
  [ENTITY_DATE_TYPE.release]: "General release",
  [ENTITY_DATE_TYPE.birth]: "Birth",
  [ENTITY_DATE_TYPE.death]: "Death",
  [ENTITY_DATE_TYPE.careerStart]: "Career start",
  [ENTITY_DATE_TYPE.careerEnd]: "Career end",
};

export interface MonthGridRange {
  start: string;
  end: string;
  days: string[];
}

export interface CalendarDayEventSlice<T> {
  visible: T[];
  hiddenCount: number;
}

/** Keeps month cells bounded while retaining the total needed for an explicit overflow action. */
export function calendarDayEventSlice<T>(events: T[]): CalendarDayEventSlice<T> {
  return {
    visible: events.slice(0, CALENDAR_DAY_VISIBLE_EVENT_LIMIT),
    hiddenCount: Math.max(0, events.length - CALENDAR_DAY_VISIBLE_EVENT_LIMIT),
  };
}

/** Adds structural context to generic child titles such as "Season 15". */
export function releaseCalendarEventTitle(
  event: Pick<ReleaseCalendarEvent, "title" | "parentTitle">,
): string {
  return event.parentTitle ? `${event.parentTitle} · ${event.title}` : event.title;
}

/** Resolves nested calendar entries through their structural parent when their route requires it. */
export function releaseCalendarEventHref(
  event: Pick<ReleaseCalendarEvent, "entityId" | "kind" | "parentEntityId" | "parentKind">,
): string | undefined {
  const parent = event.parentEntityId && event.parentKind
    ? { id: event.parentEntityId, kind: event.parentKind }
    : undefined;
  return resolveEntityHref(event.kind, event.entityId, parent);
}

/** Formats a local calendar day without the UTC conversion that can shift dates near midnight. */
export function localDateKey(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, "0");
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

/** Parses an API DateOnly value as a stable local-noon day. */
export function parseLocalDate(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year, month - 1, day, 12);
}

/** Inclusive Sunday-to-Saturday grid around one month, suitable for the calendar API range. */
export function monthGridRange(month: Date): MonthGridRange {
  const first = new Date(month.getFullYear(), month.getMonth(), 1, 12);
  const last = new Date(month.getFullYear(), month.getMonth() + 1, 0, 12);
  const start = new Date(first);
  start.setDate(first.getDate() - first.getDay());
  const end = new Date(last);
  end.setDate(last.getDate() + (6 - last.getDay()));

  const days: string[] = [];
  for (const cursor = new Date(start); cursor <= end; cursor.setDate(cursor.getDate() + 1)) {
    days.push(localDateKey(cursor));
  }
  return { start: localDateKey(start), end: localDateKey(end), days };
}

/** Stable label for a generated date type, including a safe fallback for deployment skew. */
export function releaseDateLabel(type: string): string {
  return RELEASE_DATE_LABELS[type as EntityDateTypeCode]
    ?? type.replaceAll("-", " ").replace(/\b\w/g, (value) => value.toUpperCase());
}
