import { COLLECTION_RULE_OPERATOR as OP } from "$lib/api/generated/codes";
import { COLLECTION_RULE_FIELDS, type CollectionConditionValue, type CollectionOperator, type CollectionRuleFieldDef, type CollectionRuleGroup, type CollectionRuleCondition } from "./models";

/** Operators whose meaning is complete without a comparison value. */
export function isNullaryOperator(operator: CollectionOperator): boolean {
  return operator === OP.isNull || operator === OP.isNotNull || operator === OP.isTrue || operator === OP.isFalse;
}

/** Reset an edited field or operator without carrying an incompatible previous value. */
export function defaultConditionValue(field: CollectionRuleFieldDef, operator: CollectionOperator): CollectionConditionValue {
  if (isNullaryOperator(operator)) return null;
  if (operator === OP.between) return field.fieldType === "date" ? ["", ""] : [0, 0];
  if (operator === OP.in || operator === OP.notIn) return [];
  if (field.fieldType === "number") return 0;
  if (field.fieldType === "boolean") return true;
  return "";
}

/** Preserve entered values while switching comparisons that use the same input shape. */
export function conditionValueForOperator(
  field: CollectionRuleFieldDef,
  previous: CollectionOperator,
  next: CollectionOperator,
  value: CollectionConditionValue,
): CollectionConditionValue {
  if (isNullaryOperator(previous) || isNullaryOperator(next)) return defaultConditionValue(field, next);
  if ((previous === OP.between) !== (next === OP.between)) return defaultConditionValue(field, next);
  const previousMultiple = previous === OP.in || previous === OP.notIn;
  const nextMultiple = next === OP.in || next === OP.notIn;
  return previousMultiple === nextMultiple ? value : defaultConditionValue(field, next);
}

export const collectionFieldOptions = COLLECTION_RULE_FIELDS.map(field => ({ value: field.field, label: field.label }));

/** A preview must describe complete conditions, including every saved nested group. */
export function rulesReadyForPreview(group: CollectionRuleGroup): boolean {
  return group.children.length > 0 && group.children.every(child =>
    child.type === "group" ? rulesReadyForPreview(child) : conditionReady(child));
}

function conditionReady(condition: CollectionRuleCondition): boolean {
  const field = COLLECTION_RULE_FIELDS.find(field => field.field === condition.field);
  if (!field || !field.operators.includes(condition.operator)) return false;
  if (isNullaryOperator(condition.operator)) return true;
  const value = condition.value;
  if (value == null) return false;
  if (condition.operator === OP.between) {
    if (!Array.isArray(value) || value.length !== 2) return false;
    return field.fieldType === "date"
      ? value.every(item => typeof item === "string" && item.trim() && !Number.isNaN(Date.parse(item)))
      : value.every(item => item !== "" && Number.isFinite(Number(item)));
  }
  if (condition.operator === OP.in || condition.operator === OP.notIn) {
    return Array.isArray(value) && value.length > 0 && value.every(item => String(item).trim().length > 0);
  }
  if (field.fieldType === "number") return typeof value === "number" && Number.isFinite(value);
  if (field.fieldType === "date") return typeof value === "string" && !Number.isNaN(Date.parse(value));
  return typeof value === "string" ? value.trim().length > 0 : typeof value === "number" || typeof value === "boolean";
}
