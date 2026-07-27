import { IDENTIFY_ACTION, type IdentifyActionCode } from "$lib/api/generated/codes";
import type { PluginEntitySupport } from "$lib/api/generated/model";
import { entityAccentForKind, entitySpectrumIndex, type EntityAccent } from "$lib/entities/entity-accent";
import { labelForEntityKind } from "$lib/entities/entity-codes";

/**
 * Short human labels for what a plugin can do to an entity family. The raw codes are wire
 * vocabulary, so they never reach the interface.
 */
const ACTION_LABELS: Readonly<Record<IdentifyActionCode, string>> = {
  [IDENTIFY_ACTION.search]: "Search",
  [IDENTIFY_ACTION.lookupId]: "ID",
  [IDENTIFY_ACTION.lookupUrl]: "URL",
};

/** Display order for actions, so every chip lists its abilities the same way. */
const ACTION_ORDER: readonly IdentifyActionCode[] = [
  IDENTIFY_ACTION.search,
  IDENTIFY_ACTION.lookupId,
  IDENTIFY_ACTION.lookupUrl,
];

/** One entity family a plugin supports, ready to render. */
export interface PluginCapability {
  entityKind: string;
  /** The family's display name, never its code. */
  label: string;
  /** Muted material pair identifying the family. */
  accent: EntityAccent;
  /** Ordered action labels, e.g. `["Search", "ID"]`. */
  actionLabels: string[];
  /** Whether the plugin can run a user-facing search for this family. */
  searchable: boolean;
}

/** Label for a single identify action code, falling back to the code when unrecognized. */
export function labelForIdentifyAction(action: string): string {
  return ACTION_LABELS[action as IdentifyActionCode] ?? action;
}

/**
 * Normalizes a plugin's declared support into renderable capabilities: real family labels, the
 * family's accent pair, and ordered action labels.
 *
 * Families are sorted along the prism spectrum so a plugin's chips read in the same order every
 * time and match how families are ordered everywhere else in the app.
 */
export function pluginCapabilities(
  supports: readonly PluginEntitySupport[],
): PluginCapability[] {
  return supports
    .map((support) => {
      const actions = new Set(support.actions);
      const ordered = ACTION_ORDER.filter((action) => actions.has(action));
      // Preserve anything the backend added that the frontend does not know about yet, rather
      // than silently dropping a capability the plugin really has.
      const extra = support.actions.filter(
        (action) => !ACTION_ORDER.includes(action as IdentifyActionCode),
      );

      return {
        entityKind: support.entityKind,
        label: labelForEntityKind(support.entityKind),
        accent: entityAccentForKind(support.entityKind),
        actionLabels: [...ordered, ...extra].map(labelForIdentifyAction),
        searchable: actions.has(IDENTIFY_ACTION.search),
      };
    })
    .sort(
      (left, right) =>
        entitySpectrumIndex(left.entityKind) - entitySpectrumIndex(right.entityKind) ||
        left.label.localeCompare(right.label),
    );
}

/**
 * A one-line summary of everything a plugin covers, for a collapsed row or a tooltip.
 * Returns an empty string when the plugin declares no support at all.
 */
export function summarizeCapabilities(capabilities: readonly PluginCapability[]): string {
  if (capabilities.length === 0) return "";
  return capabilities.map((capability) => capability.label).join(", ");
}
