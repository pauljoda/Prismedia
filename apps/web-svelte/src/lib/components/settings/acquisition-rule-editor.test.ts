import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionRulePresetView } from "$lib/api/generated/model";
import { literalTitleCondition, presetsForProfileKind } from "./acquisition-rule-editor";

describe("Starter rules", () => {
  it("offers each profile only the presets that fit its kind", () => {
    const preset = (name: string, profileKinds: AcquisitionRulePresetView["profileKinds"]): AcquisitionRulePresetView =>
      ({ name, description: name, suggestedScore: 100, conditions: [], profileKinds });
    const presets = [
      preset("English audio", [ENTITY_KIND.book, ENTITY_KIND.movie]),
      preset("H.265 / HEVC", [ENTITY_KIND.movie]),
      preset("M4B audiobook", [ENTITY_KIND.book]),
    ];

    expect(presetsForProfileKind(presets, ENTITY_KIND.book).map((item) => item.name)).toEqual(["English audio", "M4B audiobook"]);
    expect(presetsForProfileKind(presets, ENTITY_KIND.movie).map((item) => item.name)).toEqual(["English audio", "H.265 / HEVC"]);
  });
});

describe("Basic title rules", () => {
  it("keeps regex characters literal while accepting release separators", () => {
    const condition = literalTitleCondition("Director's Cut (2024)+");
    const pattern = new RegExp(condition.value, "i");
    expect(pattern.test("Movie.Director's-Cut_(2024)+.1080p")).toBe(true);
    expect(pattern.test("Movie Director's Cut 202444")).toBe(false);
    expect(condition.required).toBe(true);
  });
  it("does not treat user-supplied expressions as executable patterns", () => {
    const condition = literalTitleCondition("(a+)+$");
    expect(new RegExp(condition.value).test("aaaaaaaa!")).toBe(false);
    expect(new RegExp(condition.value).test("(a+)+$")).toBe(true);
  });
});
