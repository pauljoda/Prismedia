import { ENTITY_DATE_TYPE as DATE } from "$lib/entities/entity-codes";
import type { EntityDetailDate } from "./entity-detail";

// Header emphasis only. Never changes the stored dates, acquisition gates, or calendar order.
const DATE_RANGES = [[DATE.birth, DATE.death], [DATE.firstAir, DATE.lastAir], [DATE.careerStart, DATE.careerEnd]] as const;
const RELEASE_EMPHASIS = [
  DATE.publication, DATE.air, DATE.theatricalRelease, DATE.release, DATE.premiere,
  DATE.streamingRelease, DATE.digitalRelease, DATE.physicalRelease, DATE.announcement,
] as const;

/** Returns one identity-defining milestone, or both ends of a lifespan/broadcast/career range. */
export function summarizeEntityDates(dates: readonly EntityDetailDate[]): EntityDetailDate[] {
  for (const range of DATE_RANGES) {
    const entries = range.flatMap(code => dates.filter(date => date.code === code));
    if (entries.length > 0) return entries;
  }
  for (const code of RELEASE_EMPHASIS) {
    const date = dates.find(date => date.code === code);
    if (date) return [date];
  }
  return dates.slice(0, 1);
}
