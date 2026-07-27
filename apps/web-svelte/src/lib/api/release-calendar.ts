import { listReleaseCalendar } from "$lib/api/generated/prismedia";
import type { ReleaseCalendarEvent } from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

/** Loads the typed release milestones visible in an inclusive calendar range. */
export async function fetchReleaseCalendar(
  start: string,
  end: string,
  options?: RequestOptions,
): Promise<ReleaseCalendarEvent[]> {
  return unwrapGenerated(
    await listReleaseCalendar({ start, end }, requestInit(options)),
    "Failed to load the release calendar",
  );
}
