import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE, ENTITY_KIND, ENTITY_KIND_DEFINITIONS } from "$lib/api/generated/codes";
import {
  profileSupportsReleaseDate,
  releaseTimingOptionsFor,
} from "./acquisition-profile-release-timing";
import { profileKindOptions } from "./acquisition-profile-kind";

describe("acquisition profile release timing", () => {
  it("offers movie availability milestones without person-only dates", () => {
    const values = releaseTimingOptionsFor(ENTITY_KIND.movie).map((option) => option.value);

    expect(values).toContain(ENTITY_DATE_TYPE.theatricalRelease);
    expect(values).toContain(ENTITY_DATE_TYPE.streamingRelease);
    expect(values).toContain(ENTITY_DATE_TYPE.physicalRelease);
    expect(values).not.toContain(ENTITY_DATE_TYPE.birth);
  });

  it("keeps profile kinds aligned with their meaningful milestones", () => {
    expect(profileSupportsReleaseDate(ENTITY_KIND.videoSeries, ENTITY_DATE_TYPE.firstAir)).toBe(true);
    expect(profileSupportsReleaseDate(ENTITY_KIND.book, ENTITY_DATE_TYPE.publication)).toBe(true);
    expect(profileSupportsReleaseDate(ENTITY_KIND.audioLibrary, ENTITY_DATE_TYPE.theatricalRelease)).toBe(false);
  });

  it("reads the ordered milestones from the generated acquisition-profile facet", () => {
    const values = releaseTimingOptionsFor(ENTITY_KIND.videoSeries).slice(1).map((option) => option.value);

    expect(values).toEqual(ENTITY_KIND_DEFINITIONS[ENTITY_KIND.videoSeries].acquisitionProfile?.supportedReleaseDateTypes);
  });

  it("keeps profile selectors in their definition-owned display order", () => {
    expect(profileKindOptions).toEqual([
      { value: ENTITY_KIND.book, label: "Books" },
      { value: ENTITY_KIND.movie, label: "Movies" },
      { value: ENTITY_KIND.videoSeries, label: "TV (series)" },
      { value: ENTITY_KIND.audioLibrary, label: "Music (albums)" },
    ]);
  });
});
