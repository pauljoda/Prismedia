import {
  type EntityDateTypeCode,
  ENTITY_KIND_DEFINITIONS,
} from "$lib/api/generated/codes";
import { isEntityKindCode } from "$lib/entities/entity-codes";
import { releaseDateLabel } from "$lib/calendar/release-calendar";

function releaseDatesFor(kind: string): readonly EntityDateTypeCode[] {
  return isEntityKindCode(kind)
    ? ENTITY_KIND_DEFINITIONS[kind].acquisitionProfile?.supportedReleaseDateTypes ?? []
    : [];
}

export function releaseTimingOptionsFor(kind: string) {
  return [
    { value: "", label: "Immediately" },
    ...releaseDatesFor(kind).map((value) => ({
      value,
      label: `After ${releaseDateLabel(value).toLocaleLowerCase()}`,
    })),
  ];
}

export function profileSupportsReleaseDate(kind: string, type: string): boolean {
  return releaseDatesFor(kind).includes(type as EntityDateTypeCode);
}

export function releaseTimingLabel(type: string | null | undefined): string {
  return type ? `after ${releaseDateLabel(type).toLocaleLowerCase()}` : "immediately";
}
