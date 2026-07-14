import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import { entityAccentForKind } from "./entity-accent";

describe("entityAccentForKind", () => {
  it("assigns the muted material spectrum to every entity family", () => {
    expect(entityAccentForKind(ENTITY_KIND.video)).toMatchObject({ primary: "#b3484d", secondary: "#b76337" });
    expect(entityAccentForKind(ENTITY_KIND.movie)).toMatchObject({ primary: "#b76337", secondary: "#9e873b" });
    expect(entityAccentForKind(ENTITY_KIND.videoSeries)).toMatchObject({ primary: "#9e873b", secondary: "#4d925d" });
    expect(entityAccentForKind(ENTITY_KIND.gallery)).toMatchObject({ primary: "#4d925d", secondary: "#3b869c" });
    expect(entityAccentForKind(ENTITY_KIND.book)).toMatchObject({ primary: "#3b869c", secondary: "#536fb0" });
    expect(entityAccentForKind(ENTITY_KIND.image)).toMatchObject({ primary: "#536fb0", secondary: "#775ca5" });
    expect(entityAccentForKind(ENTITY_KIND.audio)).toMatchObject({ primary: "#775ca5", secondary: "#9a4f9d" });
    expect(entityAccentForKind(ENTITY_KIND.collection)).toMatchObject({ primary: "#9a4f9d", secondary: "#b3484d" });
  });

  it("keeps related structural kinds on their parent family color", () => {
    expect(entityAccentForKind(ENTITY_KIND.videoSeason)).toEqual(entityAccentForKind(ENTITY_KIND.videoSeries));
    expect(entityAccentForKind(ENTITY_KIND.bookChapter)).toEqual(entityAccentForKind(ENTITY_KIND.book));
    expect(entityAccentForKind(ENTITY_KIND.audioTrack)).toEqual(entityAccentForKind(ENTITY_KIND.audio));
  });
});
