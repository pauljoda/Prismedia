import { CONNECTION_STATUS, PLUGIN_CAPABILITY, type ConnectionStatusCode, type PluginCapabilityCode } from "$lib/api/generated/codes";

export const capabilityLabels: Record<PluginCapabilityCode, string> = {
  [PLUGIN_CAPABILITY.metadata]: "Metadata",
  [PLUGIN_CAPABILITY.catalogDiscovery]: "Discovery",
  [PLUGIN_CAPABILITY.acquisitionSource]: "Acquisition sources",
  [PLUGIN_CAPABILITY.transferExecutor]: "Downloads",
  [PLUGIN_CAPABILITY.externalManager]: "Library management",
  [PLUGIN_CAPABILITY.connectedLibrary]: "Connected library",
};

export const connectionStatusLabels: Record<ConnectionStatusCode, string> = {
  [CONNECTION_STATUS.unverified]: "Checking",
  [CONNECTION_STATUS.ready]: "Connected",
  [CONNECTION_STATUS.unavailable]: "Unavailable",
  [CONNECTION_STATUS.identityChanged]: "Application changed",
  [CONNECTION_STATUS.disabled]: "Disabled",
};
