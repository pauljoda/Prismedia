export type PluginsTab = "installed" | "prismedia-index" | "stash-index" | "stashbox";

export type PluginTabDefinition = {
  key: PluginsTab;
  label: string;
  count: number | null;
  nsfw: boolean;
};

export interface PluginEntitySupport {
  entityKind: string;
  actions: string[];
}

export interface PluginAuthField {
  key: string;
  label: string;
  required: boolean;
  url?: string | null;
}

export interface PluginProviderSummary {
  id: string;
  name: string;
  version: string;
  installed: boolean;
  enabled: boolean;
  isNsfw: boolean;
  supports: PluginEntitySupport[];
  auth: PluginAuthField[];
  missingAuthKeys: string[];
}
