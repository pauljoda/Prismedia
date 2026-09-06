import { CUSTOM_FORMAT_CONDITION_TYPE } from "$lib/api/generated/codes";
import type { CustomFormatConditionView } from "$lib/api/generated/model";

/** Builds the same editable regex condition used by Advanced, safely escaping literal user text. */
export function literalTitleCondition(text: string): CustomFormatConditionView {
  const value = text.trim().split(/\s+/).map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("[\\s._-]+");
  return { type: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, value, negate: false, required: true };
}
