import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE, ENTITY_KIND } from "$lib/api/generated/codes";
import {
  profileSupportsReleaseDate,
  releaseTimingOptionsFor,
} from "./acquisition-profile-release-timing";

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
});
