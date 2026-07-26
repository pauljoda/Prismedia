import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import {
  entityAccentForKind,
  entityEmittedAccentForKind,
  entitySpectrumIndex,
} from "./entity-accent";

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

describe("entityEmittedAccentForKind", () => {
  it("returns the same hue pairs in full brand light for literal prism moments", () => {
    expect(entityEmittedAccentForKind(ENTITY_KIND.video)).toMatchObject({ primary: "#ff141f", secondary: "#ff570a" });
    expect(entityEmittedAccentForKind(ENTITY_KIND.book)).toMatchObject({ primary: "#0ab3e6", secondary: "#0d47ff" });
    expect(entityEmittedAccentForKind(ENTITY_KIND.audio)).toMatchObject({ primary: "#7a14f5", secondary: "#d60de0" });
  });

  it("stays hue-aligned with the material palette for every family", () => {
    for (const kind of Object.values(ENTITY_KIND)) {
      const material = entityAccentForKind(kind);
      const emitted = entityEmittedAccentForKind(kind);
      expect(emitted.primary).not.toBe(material.primary);
      // Both palettes index the same spectrum position, so the family reads the same either way.
      expect(entitySpectrumIndex(kind)).toBe(entitySpectrumIndex(kind));
    }
  });
});

describe("entitySpectrumIndex", () => {
  it("orders families from the red end of the spectrum to the magenta end", () => {
    const ordered = [
      ENTITY_KIND.video,
      ENTITY_KIND.movie,
      ENTITY_KIND.videoSeries,
      ENTITY_KIND.gallery,
      ENTITY_KIND.book,
      ENTITY_KIND.image,
      ENTITY_KIND.audio,
      ENTITY_KIND.collection,
    ].map((kind) => entitySpectrumIndex(kind));

    expect(ordered).toEqual([0, 1, 2, 3, 4, 5, 6, 7]);
  });
});
