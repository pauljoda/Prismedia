import { describe, expect, it } from "vitest";
import { COLLECTION_RULE_FIELD as FIELD, COLLECTION_RULE_OPERATOR as OP, COLLECTION_RULE_GROUP_OPERATOR as GROUP } from "$lib/api/generated/codes";
import type { CollectionConditionValue, CollectionRuleGroup, CollectionRuleCondition } from "./models";
import { conditionValueForOperator, rulesReadyForPreview } from "./rule-editor";
import { COLLECTION_RULE_FIELDS } from "./models";

const condition = (patch: Partial<CollectionRuleCondition> = {}): CollectionRuleCondition => ({
  type: "condition", field: FIELD.title, operator: OP.contains, value: "cats", entityTypes: [], ...patch,
});
const group = (children: CollectionRuleGroup["children"]): CollectionRuleGroup => ({
  type: "group", operator: GROUP.and, children,
});

describe("collection rule readiness", () => {
  it.each([
    [FIELD.title, OP.contains, OP.notContains, "cats"],
    [FIELD.duration, OP.greaterThan, OP.lessThan, 900],
    [FIELD.resolution, OP.in, OP.notIn, ["1080p"]],
    [FIELD.date, OP.greaterThan, OP.lessThan, "2026-01-01"],
  ] as const)("preserves compatible values when changing %s comparisons", (field, previous, next, value) => {
    const definition = COLLECTION_RULE_FIELDS.find(item => item.field === field)!;
    expect(conditionValueForOperator(definition, previous, next, value as CollectionConditionValue)).toEqual(value);
  });
  it("clears values only when the new comparison needs a different shape", () => {
    const definition = COLLECTION_RULE_FIELDS.find(item => item.field === FIELD.duration)!;
    expect(conditionValueForOperator(definition, OP.greaterThan, OP.isNull, 900)).toBeNull();
    expect(conditionValueForOperator(definition, OP.greaterThan, OP.between, 900)).toEqual([0, 0]);
    expect(conditionValueForOperator(definition, OP.greaterThan, OP.lessThan, null)).toBeNull();
  });
  it("requires conditions and comparison values before previewing", () => {
    expect(rulesReadyForPreview(group([]))).toBe(false);
    expect(rulesReadyForPreview(group([condition({ value: "" })]))).toBe(false);
    expect(rulesReadyForPreview(group([condition({ value: "  " })]))).toBe(false);
    expect(rulesReadyForPreview(group([condition()]))).toBe(true);
  });
  it("checks every nested group instead of silently accepting incomplete nested rules", () => {
    expect(rulesReadyForPreview(group([condition(), group([condition({ value: "" })])]))).toBe(false);
    expect(rulesReadyForPreview(group([group([])]))).toBe(false);
    expect(rulesReadyForPreview(group([group([condition()])]))).toBe(true);
  });
  it("accepts nullary conditions without a value", () => {
    expect(rulesReadyForPreview(group([condition({ field: FIELD.organized, operator: OP.isTrue, value: null })]))).toBe(true);
  });
  it("requires both valid dates and all multi-value comparisons", () => {
    expect(rulesReadyForPreview(group([condition({ field: FIELD.date, operator: OP.between, value: ["2026-01-01", ""] })]))).toBe(false);
    expect(rulesReadyForPreview(group([condition({ field: FIELD.date, operator: OP.between, value: ["2026-01-01", "2026-02-01"] })]))).toBe(true);
    expect(rulesReadyForPreview(group([condition({ field: FIELD.tags, operator: OP.in, value: [] })]))).toBe(false);
  });
  it("rejects unsupported fields and operators", () => {
    expect(rulesReadyForPreview(group([condition({ field: "unknown" })]))).toBe(false);
    expect(rulesReadyForPreview(group([condition({ operator: OP.isTrue })]))).toBe(false);
  });
});
