import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { isDeletableMediaKind } from "$lib/api/entity-deletion";

describe("entity deletion kind policy", () => {
  it("uses the generated registry policy including generic audio", () => {
    expect(isDeletableMediaKind(ENTITY_KIND.audio)).toBe(true);
    expect(isDeletableMediaKind(ENTITY_KIND.videoSeries)).toBe(true);
    expect(isDeletableMediaKind(ENTITY_KIND.bookChapter)).toBe(false);
    expect(isDeletableMediaKind(ENTITY_KIND.bookPage)).toBe(false);
    expect(isDeletableMediaKind(ENTITY_KIND.collection)).toBe(false);
  });
});
