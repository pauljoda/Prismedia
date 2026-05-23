import { describe, expect, it } from "vitest";
import { buildStudioEditPatch, buildTagEditPatch } from "./entity-edit-patch";

describe("entity edit patch builders", () => {
  it("normalizes studio edit fields for the update API", () => {
    expect(
      buildStudioEditPatch({
        name: "  Studio One  ",
        description: "  ",
        aliases: " S1, Studio 1 ",
        url: " https://example.test ",
        parentId: "",
        favorite: true,
        isNsfw: false,
      }),
    ).toEqual({
      name: "Studio One",
      description: null,
      aliases: "S1, Studio 1",
      url: "https://example.test",
      parentId: null,
      favorite: true,
      isNsfw: false,
    });
  });

  it("normalizes tag edit fields for the update API", () => {
    expect(
      buildTagEditPatch({
        name: "  Moody  ",
        description: " Atmospheric ",
        aliases: "",
        favorite: false,
        isNsfw: true,
        ignoreAutoTag: true,
      }),
    ).toEqual({
      name: "Moody",
      description: "Atmospheric",
      aliases: null,
      favorite: false,
      isNsfw: true,
      ignoreAutoTag: true,
    });
  });
});
