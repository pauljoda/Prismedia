import type { LedStatus } from "@prismedia/ui-svelte";
import { CONNECTION_STATUS, IDENTIFY_ACTION } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
import { labelForEntityKind } from "$lib/entities/entity-codes";
import { capabilityLabels } from "$lib/integrations/connection-labels";
import { labelForIdentifyAction } from "$lib/plugins/plugin-capabilities";
import { MEDIA_FAMILIES, mediaFamilyForKind, mediaFamilyOrder, type MediaFamily } from "./media-families";

/** One media family a plugin can light up, and what it does there. */
export interface PluginLight {
  family: MediaFamily;
  /** Display names of the entity kinds inside the family the plugin covers. */
  kinds: string[];
  /** Quiet, ordered operation labels: identify actions first, then integration capabilities. */
  operations: string[];
}

/** Identify actions read in the same order everywhere: search first, then direct lookups. */
const ACTION_ORDER: readonly string[] = [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId, IDENTIFY_ACTION.lookupUrl];

function actionRank(action: string): number {
  const index = ACTION_ORDER.indexOf(action);
  return index < 0 ? ACTION_ORDER.length : index;
}

function pushUnique(list: string[], value: string) {
  if (!list.includes(value)) list.push(value);
}

/**
 * Folds a plugin's identify support and integration capabilities into media families, in spectrum
 * order. A plugin that searches movies and looks up seasons reads as two bands, not seven chips.
 */
export function pluginLights(plugin: PluginProvider): PluginLight[] {
  const lights = new Map<string, PluginLight>();
  const lightFor = (kind: string): PluginLight => {
    const family = mediaFamilyForKind(kind);
    let light = lights.get(family.key);
    if (!light) {
      light = { family, kinds: [], operations: [] };
      lights.set(family.key, light);
    }
    pushUnique(light.kinds, labelForEntityKind(kind));
    return light;
  };

  for (const support of plugin.supports) {
    const light = lightFor(support.entityKind);
    const ordered = [...support.actions].sort((left, right) => actionRank(left) - actionRank(right));
    for (const action of ordered) pushUnique(light.operations, labelForIdentifyAction(action));
  }
  for (const capability of plugin.integration?.capabilities ?? []) {
    const label = capabilityLabels[capability.kind] ?? capability.kind;
    for (const kind of capability.entityKinds) pushUnique(lightFor(kind).operations, label);
  }

  return [...lights.values()].sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family));
}

/** How healthy a plugin is, in one LED and a short label. */
export interface PluginHealth {
  led: LedStatus;
  label: string;
}

export function pluginHealth(plugin: PluginProvider, connections: readonly ConnectionResponse[]): PluginHealth {
  if (!plugin.installed) return { led: "idle", label: "Available" };
  if (!plugin.enabled) return { led: "idle", label: "Disabled" };
  if (plugin.missingAuthKeys.length > 0) return { led: "warning", label: "Needs keys" };
  const broken = connections.some(
    (connection) =>
      connection.enabled &&
      (connection.status === CONNECTION_STATUS.unavailable || connection.status === CONNECTION_STATUS.identityChanged),
  );
  if (broken) return { led: "error", label: "Connection down" };
  return { led: "phosphor", label: "Ready" };
}

/** Coverage across the library: how many lit plugins serve each named family. */
export interface FamilyCoverage {
  family: MediaFamily;
  sources: string[];
}

export function familyCoverage(plugins: readonly PluginProvider[]): FamilyCoverage[] {
  const coverage = new Map<string, string[]>(MEDIA_FAMILIES.map((family) => [family.key, []]));
  for (const plugin of plugins) {
    for (const light of pluginLights(plugin)) {
      const sources = coverage.get(light.family.key);
      if (sources) pushUnique(sources, plugin.name);
    }
  }
  return MEDIA_FAMILIES.map((family) => ({ family, sources: coverage.get(family.key) ?? [] }));
}

/**
 * The beam across the top of a plugin card: neutral light on the left dispersing into exactly the
 * families this plugin serves.
 */
export function pluginBeam(lights: readonly PluginLight[]): string {
  const neutral = "color-mix(in oklab, var(--color-text-primary) 26%, transparent)";
  if (lights.length === 0) return `linear-gradient(90deg, ${neutral}, transparent)`;
  const start = 46;
  const span = 100 - start;
  const stops = lights.flatMap((light, index) => {
    const from = start + (span * index) / lights.length;
    const to = start + (span * (index + 1)) / lights.length;
    return [`${light.family.accent.primary} ${from.toFixed(1)}%`, `${light.family.accent.secondary} ${to.toFixed(1)}%`];
  });
  return `linear-gradient(90deg, ${neutral} 0%, color-mix(in oklab, var(--color-text-primary) 12%, transparent) ${start - 8}%, ${stops.join(", ")})`;
}
