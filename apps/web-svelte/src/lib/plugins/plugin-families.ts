import type { LedStatus } from "@prismedia/ui-svelte";
import { CONNECTION_STATUS, IDENTIFY_ACTION, type IdentifyActionCode } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
import { labelForEntityKind } from "$lib/entities/entity-codes";
import { MEDIA_FAMILIES, mediaFamilyForKind, mediaFamilyOrder, type MediaFamily } from "$lib/entities/media-families";
import { capabilityLabels } from "$lib/integrations/connection-labels";

/** One media family a plugin serves, and what it does there. */
export interface PluginFamilySupport {
  family: MediaFamily;
  /** Display names of the entity kinds inside the family the plugin covers. */
  kinds: string[];
  /** Ordered operation labels: identify actions first, then integration capabilities. */
  operations: string[];
}

/** Short labels for identify actions. The codes are wire vocabulary and never reach the interface. */
const ACTION_LABELS: Readonly<Record<IdentifyActionCode, string>> = {
  [IDENTIFY_ACTION.search]: "Search",
  [IDENTIFY_ACTION.lookupId]: "ID",
  [IDENTIFY_ACTION.lookupUrl]: "URL",
};

/** Identify actions read in the same order everywhere: search first, then direct lookups. */
const ACTION_ORDER: readonly string[] = [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId, IDENTIFY_ACTION.lookupUrl];

/** Label for one identify action, falling back to the code for an action the frontend does not know yet. */
export function labelForIdentifyAction(action: string): string {
  return ACTION_LABELS[action as IdentifyActionCode] ?? action;
}

/** Identify operations lead in action order; integration capabilities follow in declaration order. */
function operationRank(label: string): number {
  const index = ACTION_ORDER.findIndex((action) => labelForIdentifyAction(action) === label);
  return index < 0 ? ACTION_ORDER.length : index;
}

function pushUnique(list: string[], value: string) {
  if (!list.includes(value)) list.push(value);
}

/**
 * Folds a plugin's identify support and integration capabilities into media families, in spectrum
 * order. A plugin that searches movies and looks up seasons reads as two families, not seven chips.
 */
export function pluginFamilies(plugin: PluginProvider): PluginFamilySupport[] {
  const families = new Map<string, PluginFamilySupport>();
  const supportFor = (kind: string): PluginFamilySupport => {
    const family = mediaFamilyForKind(kind);
    let support = families.get(family.key);
    if (!support) {
      support = { family, kinds: [], operations: [] };
      families.set(family.key, support);
    }
    pushUnique(support.kinds, labelForEntityKind(kind));
    return support;
  };

  for (const declared of plugin.supports) {
    const support = supportFor(declared.entityKind);
    for (const action of declared.actions) pushUnique(support.operations, labelForIdentifyAction(action));
  }
  for (const capability of plugin.integration?.capabilities ?? []) {
    const label = capabilityLabels[capability.kind] ?? capability.kind;
    for (const kind of capability.entityKinds) pushUnique(supportFor(kind).operations, label);
  }

  for (const support of families.values()) {
    support.operations.sort((left, right) => operationRank(left) - operationRank(right));
  }
  return [...families.values()].sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family));
}

/** Something about a plugin the administrator has to act on. */
export interface PluginAttention {
  led: LedStatus;
  label: string;
}

/**
 * The one state worth showing on a plugin card, or null when the plugin simply works. A working
 * plugin carries no status word: its families and connections already say what it does.
 */
export function pluginAttention(
  plugin: PluginProvider,
  connections: readonly ConnectionResponse[],
): PluginAttention | null {
  if (!plugin.enabled) return { led: "idle", label: "Disabled" };
  if (plugin.missingAuthKeys.length > 0) return { led: "warning", label: "Needs keys" };
  const down = connections.some((connection) => connectionNeedsAttention(connection));
  if (down) return { led: "error", label: "Connection down" };
  return null;
}

/** An enabled connection that cannot currently be used. */
export function connectionNeedsAttention(connection: ConnectionResponse): boolean {
  return (
    connection.enabled &&
    (connection.status === CONNECTION_STATUS.unavailable || connection.status === CONNECTION_STATUS.identityChanged)
  );
}

/** Which enabled plugins serve one named media family. */
export interface FamilyCoverage {
  family: MediaFamily;
  sources: string[];
}

/** Coverage across every named family, in spectrum order, from the plugins that are enabled. */
export function familyCoverage(plugins: readonly PluginProvider[]): FamilyCoverage[] {
  const coverage = new Map<string, string[]>(MEDIA_FAMILIES.map((family) => [family.key, []]));
  for (const plugin of plugins) {
    if (!plugin.enabled) continue;
    for (const support of pluginFamilies(plugin)) {
      const sources = coverage.get(support.family.key);
      if (sources) pushUnique(sources, plugin.name);
    }
  }
  return MEDIA_FAMILIES.map((family) => ({ family, sources: coverage.get(family.key) ?? [] }));
}

/** True when the plugin serves the named family. */
export function pluginServesFamily(plugin: PluginProvider, familyKey: string): boolean {
  return pluginFamilies(plugin).some((support) => support.family.key === familyKey);
}

/**
 * The hairline across the top of a plugin card: neutral light on the left dispersing into exactly
 * the families this plugin serves.
 */
export function pluginBeam(families: readonly PluginFamilySupport[]): string {
  const neutral = "color-mix(in oklab, var(--color-text-primary) 26%, transparent)";
  if (families.length === 0) return `linear-gradient(90deg, ${neutral}, transparent)`;
  const start = 46;
  const span = 100 - start;
  const stops = families.flatMap((support, index) => {
    const from = start + (span * index) / families.length;
    const to = start + (span * (index + 1)) / families.length;
    return [`${support.family.accent.primary} ${from.toFixed(1)}%`, `${support.family.accent.secondary} ${to.toFixed(1)}%`];
  });
  return `linear-gradient(90deg, ${neutral} 0%, color-mix(in oklab, var(--color-text-primary) 12%, transparent) ${start - 8}%, ${stops.join(", ")})`;
}
