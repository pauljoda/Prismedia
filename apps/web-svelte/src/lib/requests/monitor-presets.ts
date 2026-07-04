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
 * The presets offered at request time and in the Season Pass editor, in a stable, user-legible order.
 * Mirrors the backend <c>MonitorPreset</c> enum; the labels/descriptions live here (display text, not
 * codes) while the code values come from the generated <c>MONITOR_PRESET</c> constants.
 */
export const MONITOR_PRESET_OPTIONS: MonitorPresetOption[] = [
  { value: MONITOR_PRESET.all, label: "All", description: "Request every season now and auto-monitor future ones." },
  { value: MONITOR_PRESET.missing, label: "Missing", description: "Request every season not already in the library." },
  { value: MONITOR_PRESET.future, label: "Future only", description: "Request nothing now; monitor future seasons." },
  { value: MONITOR_PRESET.firstSeason, label: "First season", description: "Request only the first season." },
  { value: MONITOR_PRESET.latestSeason, label: "Latest season", description: "Request only the latest season." },
  { value: MONITOR_PRESET.pilot, label: "Pilot", description: "Request only the first season (the pilot)." },
  { value: MONITOR_PRESET.none, label: "None", description: "Request nothing; monitor nothing new." },
];

/** The default preset the request dialog opens on — fill the gaps that exist right now. */
export const DEFAULT_MONITOR_PRESET: MonitorPresetCode = MONITOR_PRESET.missing;

/** One selectable child (a season, a volume) reduced to what the preset deriver needs. */
export interface MonitorPresetChild {
  id: string;
  /** Season/volume ordering number when known; drives first/latest/pilot. */
  number: number | null;
  /** Whether the child can be requested (an already-owned child is not requestable — a "missing" skip). */
  requestable: boolean;
}

/**
 * The child ids a preset pre-checks — the client-side mirror of the backend
 * <c>MonitorPresetSelection.Resolve</c>, so the checkboxes a user sees match what the commit would derive.
 * All selects every requestable child; Missing does too (the server dedupes owned ones, and here a
 * non-requestable child is already owned); First/Latest/Pilot pick the single lowest/highest-numbered
 * requestable child; Future and None pre-check nothing.
 */
export function resolvePresetSelection(preset: MonitorPresetCode, children: MonitorPresetChild[]): string[] {
  const requestable = children.filter((child) => child.requestable);
  switch (preset) {
    case MONITOR_PRESET.all:
    case MONITOR_PRESET.missing:
      return requestable.map((child) => child.id);
    case MONITOR_PRESET.firstSeason:
    case MONITOR_PRESET.pilot:
      return extreme(requestable, true);
    case MONITOR_PRESET.latestSeason:
      return extreme(requestable, false);
    default:
      return [];
  }
}

/** The single lowest- (or highest-) numbered child as a one-element id list; empty when none carry a number. */
function extreme(children: MonitorPresetChild[], lowest: boolean): string[] {
  let chosen: MonitorPresetChild | null = null;
  for (const child of children) {
    if (child.number === null) continue;
    if (chosen?.number == null || (lowest ? child.number < chosen.number : child.number > chosen.number)) {
      chosen = child;
    }
  }
  return chosen ? [chosen.id] : [];
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
