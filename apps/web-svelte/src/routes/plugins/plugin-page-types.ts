export type PluginsTab = "installed" | "prismedia-index" | "stash-index" | "stashbox";

export type PluginTabDefinition = {
  key: PluginsTab;
  label: string;
  count: number | null;
  nsfw: boolean;
};
