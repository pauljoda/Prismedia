import { MONITOR_PRESET } from "$lib/api/generated/codes";
import type { MonitorPresetCode } from "$lib/api/generated/codes";

/**
 * UI-only sentinel for the preset Select when the user has hand-edited the checkbox selection so it no
 * longer matches any preset. It is NOT a backend enum member — a custom selection commits its explicit
 * child ids (with the last real preset, if any, still recorded on the monitor).
 */
export const MONITOR_PRESET_CUSTOM = "custom" as const;

/** The value the preset Select holds: a real backend preset code, or the UI-only "custom" sentinel. */
export type MonitorPresetSelectValue = MonitorPresetCode | typeof MONITOR_PRESET_CUSTOM;

/** One preset option for the Select: its code plus the user-facing label and one-line scope description. */
export interface MonitorPresetOption {
  value: MonitorPresetCode;
  label: string;
  description: string;
}

/**
 * The presets offered while selecting direct children in request review, in a stable, user-legible order.
 * Mirrors the backend <c>MonitorPreset</c> enum; the labels/descriptions live here (display text, not
 * codes) while the code values come from the generated <c>MONITOR_PRESET</c> constants.
 */
export const MONITOR_PRESET_OPTIONS: MonitorPresetOption[] = [
  { value: MONITOR_PRESET.all, label: "All current and future", description: "Request every current item and automatically monitor new ones." },
  { value: MONITOR_PRESET.missing, label: "Missing now", description: "Request every missing current item without adding future ones." },
  { value: MONITOR_PRESET.future, label: "Future only", description: "Request nothing now and automatically monitor newly discovered items." },
  { value: MONITOR_PRESET.none, label: "Manual selection", description: "Request only the items you select and add nothing automatically." },
];

/** The default preset the request dialog opens on — fill the gaps that exist right now. */
export const DEFAULT_MONITOR_PRESET: MonitorPresetCode = MONITOR_PRESET.missing;

/** One selectable child (a season, a volume) reduced to what the preset deriver needs. */
export interface MonitorPresetChild {
  id: string;
  /** Whether the child can be requested (an already-owned child is not requestable — a "missing" skip). */
  requestable: boolean;
}

/**
 * The child ids a preset pre-checks — the client-side mirror of the backend
 * <c>MonitorPresetSelection.Resolve</c>, so the checkboxes a user sees match what the commit would derive.
 * All selects every requestable child; Missing does too (the server dedupes owned ones, and here a
 * non-requestable child is already owned); Future and None pre-check nothing.
 */
export function resolvePresetSelection(preset: MonitorPresetCode, children: MonitorPresetChild[]): string[] {
  const requestable = children.filter((child) => child.requestable);
  switch (preset) {
    case MONITOR_PRESET.all:
    case MONITOR_PRESET.missing:
      return requestable.map((child) => child.id);
    default:
      return [];
  }
}

/**
 * The preset whose derived selection exactly equals the given selection, or the "custom" sentinel when
 * none matches — lets a manual checkbox edit flip the Select to "Custom" and a preset choice snap it back.
 * Compared as sets so order never matters.
 */
export function presetForSelection(children: MonitorPresetChild[], selectedIds: string[]): MonitorPresetSelectValue {
  const selected = new Set(selectedIds);
  for (const option of MONITOR_PRESET_OPTIONS) {
    const derived = resolvePresetSelection(option.value, children);
    if (derived.length === selected.size && derived.every((id) => selected.has(id))) {
      return option.value;
    }
  }
  return MONITOR_PRESET_CUSTOM;
}
