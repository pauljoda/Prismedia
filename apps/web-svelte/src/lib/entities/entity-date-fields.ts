import {
  ENTITY_DATE_TYPE,
  ENTITY_KIND,
  type EntityDateTypeCode,
} from "$lib/entities/entity-codes";

/** One canonical semantic date presented in the shared Entity metadata editor. */
export interface EntityDateFieldDefinition {
  code: EntityDateTypeCode;
  label: string;
  helper: string;
}

const FIELD_BY_CODE: Record<EntityDateTypeCode, EntityDateFieldDefinition> = {
  [ENTITY_DATE_TYPE.announcement]: {
    code: ENTITY_DATE_TYPE.announcement,
    label: "Announcement",
    helper: "When the work was publicly announced.",
  },
  [ENTITY_DATE_TYPE.premiere]: {
    code: ENTITY_DATE_TYPE.premiere,
    label: "Premiere",
    helper: "The work's first public premiere.",
  },
  [ENTITY_DATE_TYPE.theatricalRelease]: {
    code: ENTITY_DATE_TYPE.theatricalRelease,
    label: "Theatrical release",
    helper: "When the work opens in cinemas.",
  },
  [ENTITY_DATE_TYPE.streamingRelease]: {
    code: ENTITY_DATE_TYPE.streamingRelease,
    label: "Streaming release",
    helper: "When the work becomes available through a streaming service.",
  },
  [ENTITY_DATE_TYPE.digitalRelease]: {
    code: ENTITY_DATE_TYPE.digitalRelease,
    label: "Digital release",
    helper: "When digital purchase or rental becomes available.",
  },
  [ENTITY_DATE_TYPE.physicalRelease]: {
    code: ENTITY_DATE_TYPE.physicalRelease,
    label: "Physical release",
    helper: "When physical media becomes available.",
  },
  [ENTITY_DATE_TYPE.air]: {
    code: ENTITY_DATE_TYPE.air,
    label: "Air date",
    helper: "When this programme or episode airs.",
  },
  [ENTITY_DATE_TYPE.firstAir]: {
    code: ENTITY_DATE_TYPE.firstAir,
    label: "First air date",
    helper: "When this series first airs.",
  },
  [ENTITY_DATE_TYPE.lastAir]: {
    code: ENTITY_DATE_TYPE.lastAir,
    label: "Last air date",
    helper: "The most recent or final air date.",
  },
  [ENTITY_DATE_TYPE.publication]: {
    code: ENTITY_DATE_TYPE.publication,
    label: "Publication",
    helper: "When this written work is published.",
  },
  [ENTITY_DATE_TYPE.release]: {
    code: ENTITY_DATE_TYPE.release,
    label: "General release",
    helper: "Use when no more specific release milestone applies.",
  },
  [ENTITY_DATE_TYPE.birth]: {
    code: ENTITY_DATE_TYPE.birth,
    label: "Birth",
    helper: "The person's birth date.",
  },
  [ENTITY_DATE_TYPE.death]: {
    code: ENTITY_DATE_TYPE.death,
    label: "Death",
    helper: "The person's death date.",
  },
  [ENTITY_DATE_TYPE.careerStart]: {
    code: ENTITY_DATE_TYPE.careerStart,
    label: "Career start",
    helper: "When the person or group became active.",
  },
  [ENTITY_DATE_TYPE.careerEnd]: {
    code: ENTITY_DATE_TYPE.careerEnd,
    label: "Career end",
    helper: "When the person or group stopped being active.",
  },
};

const MOVIE_DATES = [
  ENTITY_DATE_TYPE.announcement,
  ENTITY_DATE_TYPE.premiere,
  ENTITY_DATE_TYPE.theatricalRelease,
  ENTITY_DATE_TYPE.streamingRelease,
  ENTITY_DATE_TYPE.digitalRelease,
  ENTITY_DATE_TYPE.physicalRelease,
  ENTITY_DATE_TYPE.release,
] as const;

const TELEVISION_DATES = [
  ENTITY_DATE_TYPE.announcement,
  ENTITY_DATE_TYPE.premiere,
  ENTITY_DATE_TYPE.air,
  ENTITY_DATE_TYPE.firstAir,
  ENTITY_DATE_TYPE.lastAir,
  ENTITY_DATE_TYPE.streamingRelease,
  ENTITY_DATE_TYPE.digitalRelease,
  ENTITY_DATE_TYPE.release,
] as const;

const BOOK_DATES = [
  ENTITY_DATE_TYPE.announcement,
  ENTITY_DATE_TYPE.publication,
  ENTITY_DATE_TYPE.digitalRelease,
  ENTITY_DATE_TYPE.physicalRelease,
  ENTITY_DATE_TYPE.release,
] as const;

const AUDIO_DATES = [
  ENTITY_DATE_TYPE.announcement,
  ENTITY_DATE_TYPE.release,
  ENTITY_DATE_TYPE.digitalRelease,
  ENTITY_DATE_TYPE.physicalRelease,
] as const;

const PERSON_DATES = [
  ENTITY_DATE_TYPE.birth,
  ENTITY_DATE_TYPE.death,
  ENTITY_DATE_TYPE.careerStart,
  ENTITY_DATE_TYPE.careerEnd,
] as const;

/** Canonical dates relevant to one Entity kind, in the order shown in the edit pane. */
export function entityDateFieldsForKind(kind: string): EntityDateFieldDefinition[] {
  const codes: readonly EntityDateTypeCode[] = kind === ENTITY_KIND.movie
    ? MOVIE_DATES
    : kind === ENTITY_KIND.videoSeries || kind === ENTITY_KIND.videoSeason || kind === ENTITY_KIND.video
      ? TELEVISION_DATES
      : kind === ENTITY_KIND.book || kind === ENTITY_KIND.bookVolume || kind === ENTITY_KIND.bookChapter
        ? BOOK_DATES
        : kind === ENTITY_KIND.audioLibrary || kind === ENTITY_KIND.audioTrack || kind === ENTITY_KIND.musicArtist
          ? AUDIO_DATES
          : kind === ENTITY_KIND.person
            ? PERSON_DATES
            : [ENTITY_DATE_TYPE.announcement, ENTITY_DATE_TYPE.release];

  return codes.map((code) => FIELD_BY_CODE[code]);
}

/** Friendly name for a canonical date code. */
export function entityDateTypeLabel(code: string): string {
  return FIELD_BY_CODE[code as EntityDateTypeCode]?.label
    ?? code.replaceAll("-", " ").replace(/^\w/, (value) => value.toUpperCase());
}
