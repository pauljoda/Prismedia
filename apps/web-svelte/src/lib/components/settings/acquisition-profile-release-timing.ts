import {
  ENTITY_DATE_TYPE,
  ENTITY_KIND,
  type EntityDateTypeCode,
} from "$lib/api/generated/codes";
import { releaseDateLabel } from "$lib/calendar/release-calendar";

const RELEASE_DATES_BY_PROFILE_KIND: Readonly<Record<string, readonly EntityDateTypeCode[]>> = {
  [ENTITY_KIND.movie]: [
    ENTITY_DATE_TYPE.premiere,
    ENTITY_DATE_TYPE.theatricalRelease,
    ENTITY_DATE_TYPE.streamingRelease,
    ENTITY_DATE_TYPE.digitalRelease,
    ENTITY_DATE_TYPE.physicalRelease,
    ENTITY_DATE_TYPE.release,
  ],
  [ENTITY_KIND.videoSeries]: [
    ENTITY_DATE_TYPE.premiere,
    ENTITY_DATE_TYPE.air,
    ENTITY_DATE_TYPE.firstAir,
    ENTITY_DATE_TYPE.streamingRelease,
    ENTITY_DATE_TYPE.digitalRelease,
    ENTITY_DATE_TYPE.release,
  ],
  [ENTITY_KIND.book]: [
    ENTITY_DATE_TYPE.publication,
    ENTITY_DATE_TYPE.digitalRelease,
    ENTITY_DATE_TYPE.physicalRelease,
    ENTITY_DATE_TYPE.release,
  ],
  [ENTITY_KIND.audioLibrary]: [
    ENTITY_DATE_TYPE.release,
    ENTITY_DATE_TYPE.digitalRelease,
    ENTITY_DATE_TYPE.physicalRelease,
  ],
};

export function releaseTimingOptionsFor(kind: string) {
  return [
    { value: "", label: "Immediately" },
    ...(RELEASE_DATES_BY_PROFILE_KIND[kind] ?? []).map((value) => ({
      value,
      label: `After ${releaseDateLabel(value).toLocaleLowerCase()}`,
    })),
  ];
}

export function profileSupportsReleaseDate(kind: string, type: string): boolean {
  return (RELEASE_DATES_BY_PROFILE_KIND[kind] ?? []).includes(type as EntityDateTypeCode);
}

export function releaseTimingLabel(type: string | null | undefined): string {
  return type ? `after ${releaseDateLabel(type).toLocaleLowerCase()}` : "immediately";
}
