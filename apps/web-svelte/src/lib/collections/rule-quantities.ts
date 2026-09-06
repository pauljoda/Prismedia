import { COLLECTION_RULE_FIELD as FIELD, type CollectionRuleFieldCode } from "$lib/api/generated/codes";

/** Display-only units. Multipliers convert the selected unit back to the API's base unit. */
export interface RuleQuantityUnit {
  label: string;
  suffix: string;
  multiplier: number;
}

const units: Partial<Record<CollectionRuleFieldCode, readonly RuleQuantityUnit[]>> = {
  [FIELD.duration]: [
    { label: "Seconds", suffix: "sec", multiplier: 1 },
    { label: "Minutes", suffix: "min", multiplier: 60 },
    { label: "Hours", suffix: "hr", multiplier: 3600 },
  ],
  [FIELD.fileSize]: [
    { label: "Bytes", suffix: "B", multiplier: 1 },
    { label: "KiB", suffix: "KiB", multiplier: 1024 },
    { label: "MiB", suffix: "MiB", multiplier: 1024 ** 2 },
    { label: "GiB", suffix: "GiB", multiplier: 1024 ** 3 },
  ],
  [FIELD.bitRate]: [
    { label: "bps", suffix: "bps", multiplier: 1 },
    { label: "kbps", suffix: "kbps", multiplier: 1000 },
  ],
  [FIELD.sampleRate]: [
    { label: "Hz", suffix: "Hz", multiplier: 1 },
    { label: "kHz", suffix: "kHz", multiplier: 1000 },
  ],
  [FIELD.width]: [{ label: "Pixels", suffix: "px", multiplier: 1 }],
  [FIELD.height]: [{ label: "Pixels", suffix: "px", multiplier: 1 }],
};

/** Numeric fields without a physical unit retain their original count or rating scale. */
export function quantityUnitsForRule(field: CollectionRuleFieldCode): readonly RuleQuantityUnit[] {
  return units[field] ?? [{ label: "Value", suffix: "", multiplier: 1 }];
}

/** Empty or invalid drafts stay incomplete instead of silently becoming a zero-valued rule. */
export function quantityFromInput(input: string, multiplier: number): number | null {
  if (!input.trim()) return null;
  const value = Number(input) * multiplier;
  return Number.isFinite(value) ? value : null;
}
