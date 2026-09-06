import { describe, expect, it } from "vitest";
import { COLLECTION_RULE_FIELD as FIELD } from "$lib/api/generated/codes";
import { quantityFromInput, quantityUnitsForRule } from "./rule-quantities";

describe("rule quantity display units", () => {
  it.each([
    ["1.5", 60, 90], ["1.25", 1024 ** 2, 1310720],
    ["44.1", 1000, 44100], ["0", 3600, 0], ["-2", 1, -2],
    ["", 60, null], [" ", 1, null], ["invalid", 1, null], ["Infinity", 1, null],
  ])("converts %s at multiplier %s without defaulting incomplete drafts to zero", (input, multiplier, expected) => {
    expect(quantityFromInput(input as string, multiplier as number)).toBe(expected);
  });
  it("retains the unscaled API unit as the initial choice", () => {
    for (const field of [FIELD.duration, FIELD.fileSize, FIELD.bitRate, FIELD.sampleRate, FIELD.width, FIELD.height, FIELD.rating]) {
      expect(quantityUnitsForRule(field)[0].multiplier).toBe(1);
    }
  });
});
