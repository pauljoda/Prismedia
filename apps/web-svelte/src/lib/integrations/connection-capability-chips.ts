import { INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, PluginIntegrationCapability } from "$lib/api/generated/model";
import { labelForEntityKind } from "$lib/entities/entity-codes";

/** A user-facing capability grouped with the media families it actually covers. */
export interface ConnectionCapabilityChip {
  key: string;
  label: string;
  entityKinds: string[];
  title: string;
}

/** The compact capability and media-family summary shown on a connected source. */
export interface ConnectionCapabilitySummary {
  capabilities: ConnectionCapabilityChip[];
  entityKinds: Array<{ code: string; label: string }>;
}

const ACTION_ORDER = ["Search", "Request", "Library", "Import", "Browse"] as const;
type ActionLabel = (typeof ACTION_ORDER)[number];

/**
 * Converts admitted connection operations into concise labels for the Request source card.
 * Only enabled capabilities with effective operations contribute, so stale or disabled
 * capability declarations never become fictional UI affordances.
 */
export function summarizeConnectionCapabilities(
  connection: Pick<ConnectionResponse, "enabledCapabilities" | "effectiveCapabilities">,
): ConnectionCapabilitySummary {
  const enabled = new Set(connection.enabledCapabilities);
  const actions = new Map<ActionLabel, Set<string>>();

  for (const capability of connection.effectiveCapabilities) {
    if (!enabled.has(capability.kind)) continue;

    const entityKinds = new Set(capability.entityKinds);
    for (const action of actionsForCapability(capability)) {
      const covered = actions.get(action) ?? new Set<string>();
      for (const entityKind of entityKinds) covered.add(entityKind);
      actions.set(action, covered);
    }
  }

  const capabilities = ACTION_ORDER
    .filter((label) => actions.has(label))
    .map((label) => {
      const entityKinds = [...(actions.get(label) ?? [])].sort((left, right) =>
        labelForEntityKind(left).localeCompare(labelForEntityKind(right)),
      );
      return {
        key: label,
        label,
        entityKinds,
        title: `${label}: ${entityKinds.map(labelForEntityKind).join(", ")}`,
      };
    });

  const entityKinds = [...new Set(capabilities.flatMap((capability) => capability.entityKinds))]
    .map((code) => ({ code, label: labelForEntityKind(code) }))
    .sort((left, right) => left.label.localeCompare(right.label));

  return { capabilities, entityKinds };
}

function actionsForCapability(capability: PluginIntegrationCapability): ActionLabel[] {
  const has = (operation: (typeof capability.operations)[number]) =>
    capability.operations.includes(operation);

  switch (capability.kind) {
    case PLUGIN_CAPABILITY.catalogDiscovery:
      return [
        ...(has(INTEGRATION_OPERATION.search) ? ["Search" as const] : []),
        ...(has(INTEGRATION_OPERATION.browse) ? ["Browse" as const] : []),
      ];
    case PLUGIN_CAPABILITY.acquisitionSource:
      return [
        ...(has(INTEGRATION_OPERATION.requestSource) ? ["Request" as const] : []),
        ...(has(INTEGRATION_OPERATION.resolve) ? ["Import" as const] : []),
      ];
    case PLUGIN_CAPABILITY.externalManager:
      return [
        ...(has(INTEGRATION_OPERATION.discoverManaged)
          ? ["Search" as const]
          : []),
        ...(has(INTEGRATION_OPERATION.ensureManaged) || has(INTEGRATION_OPERATION.requestManaged)
          ? ["Request" as const]
          : []),
      ];
    case PLUGIN_CAPABILITY.connectedLibrary:
      return [
        ...(has(INTEGRATION_OPERATION.searchLibrary) || has(INTEGRATION_OPERATION.getLibraryItem)
          ? ["Library" as const]
          : []),
      ];
    case PLUGIN_CAPABILITY.transferExecutor:
      return has(INTEGRATION_OPERATION.submit) ? ["Import"] : [];
    default:
      return [];
  }
}
