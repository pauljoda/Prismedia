export type PluginsTab = "installed" | "prismedia-index" | "stash-index";

export type PluginTabDefinition = {
  key: PluginsTab;
  label: string;
  count: number | null;
  nsfw: boolean;
};
