import { CUSTOM_FORMAT_CONDITION_TYPE } from "$lib/api/generated/codes";
import type { AcquisitionRulePresetView, CustomFormatConditionView, EntityKind } from "$lib/api/generated/model";

/** Starter rules that fit one acquisition profile kind, as the server declares for each preset. */
export function presetsForProfileKind(presets: AcquisitionRulePresetView[], kind: EntityKind): AcquisitionRulePresetView[] {
  return presets.filter((preset) => preset.profileKinds.includes(kind));
}

/** Builds the same editable regex condition used by Advanced, safely escaping literal user text. */
export function literalTitleCondition(text: string): CustomFormatConditionView {
  const value = text.trim().split(/\s+/).map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("[\\s._-]+");
  return { type: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, value, negate: false, required: true };
}
