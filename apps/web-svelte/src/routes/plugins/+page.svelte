<script lang="ts">
  import { onMount } from "svelte";
  import InstalledPluginsTab from "./InstalledPluginsTab.svelte";
  import PluginPageShell from "./PluginPageShell.svelte";
  import PrismediaCommunityTab from "./PrismediaCommunityTab.svelte";
  import StashCommunityIndexTab from "./StashCommunityIndexTab.svelte";
  import type { StashScraperRow } from "./StashCommunityIndexTab.svelte";
  import type { PluginTabDefinition, PluginsTab } from "./plugin-page-types";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    fetchPluginProviders,
    fetchStashScrapers,
    installPlugin,
    removePlugin,
    savePluginAuth,
    updatePlugin,
    type PluginProvider,
    type StashScraperListing,
  } from "$lib/api/plugins";

  const nsfw = useNsfw();
  const isSfw = $derived(nsfw.mode === "off");

  let tab = $state<PluginsTab>("installed");
  let loading = $state(true);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);

  let pluginProviders = $state<PluginProvider[]>([]);
  let providerInstallingId = $state<string | null>(null);
  let providerRemovingId = $state<string | null>(null);
  let providerUpdatingId = $state<string | null>(null);
  let authExpandedFor = $state<string | null>(null);
  let authValues = $state<Record<string, string>>({});
  let authSavingFor = $state<string | null>(null);

  let stashScrapers = $state<StashScraperListing[]>([]);
  let stashIndexLoading = $state(false);
  let stashIndexLoaded = $state(false);

  let prismediaLoading = $state(false);
  let prismediaLoaded = $state(false);

  function flashMessage(value: string) {
    message = value;
    setTimeout(() => {
      if (message === value) message = null;
    }, 3000);
  }

  function upsertProvider(provider: PluginProvider) {
    pluginProviders = pluginProviders.some((row) => row.id === provider.id)
      ? pluginProviders.map((row) => (row.id === provider.id ? provider : row))
      : [...pluginProviders, provider];
  }

  async function loadInstalled() {
    error = null;
    try {
      pluginProviders = await fetchPluginProviders();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load plugins";
    } finally {
      loading = false;
    }
  }

  async function loadStashIndex() {
    stashIndexLoading = true;
    error = null;
    try {
      stashScrapers = await fetchStashScrapers();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to fetch Stash community index";
    } finally {
      stashIndexLoaded = true;
      stashIndexLoading = false;
    }
  }

  async function loadPrismediaIndex() {
    prismediaLoading = true;
    error = null;
    try {
      pluginProviders = await fetchPluginProviders();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to fetch plugin catalog";
    } finally {
      prismediaLoaded = true;
      prismediaLoading = false;
    }
  }

  onMount(() => {
    void loadInstalled();
  });

  $effect(() => {
    if (isSfw && tab === "stash-index") tab = "installed";
  });

  $effect(() => {
    if (tab === "stash-index" && !stashIndexLoaded && !stashIndexLoading) void loadStashIndex();
    if (tab === "prismedia-index" && !prismediaLoaded && !prismediaLoading) void loadPrismediaIndex();
  });

  async function handleProviderInstall(plugin: PluginProvider) {
    providerInstallingId = plugin.id;
    error = null;
    try {
      const installed = await installPlugin(plugin.id);
      upsertProvider(installed);
      flashMessage(`Installed ${installed.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${plugin.name}`;
    } finally {
      providerInstallingId = null;
    }
  }

  async function handleStashInstall(providerId: string) {
    providerInstallingId = providerId;
    error = null;
    try {
      const installed = await installPlugin(providerId);
      upsertProvider(installed);
      flashMessage(`Installed ${installed.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${providerId}`;
    } finally {
      providerInstallingId = null;
    }
  }

  async function handleProviderUpdate(plugin: PluginProvider) {
    providerUpdatingId = plugin.id;
    error = null;
    try {
      const updated = await updatePlugin(plugin.id);
      upsertProvider(updated);
      flashMessage(`Updated ${updated.name} -> v${updated.version}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to update ${plugin.name}`;
    } finally {
      providerUpdatingId = null;
    }
  }

  async function handleProviderRemove(plugin: PluginProvider) {
    providerRemovingId = plugin.id;
    error = null;
    try {
      await removePlugin(plugin.id);
      upsertProvider({ ...plugin, installed: false, enabled: false });
      flashMessage(`Removed ${plugin.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to remove ${plugin.name}`;
    } finally {
      providerRemovingId = null;
    }
  }

  function toggleProviderAuthExpanded(pluginId: string) {
    const key = `prismedia:${pluginId}`;
    authExpandedFor = authExpandedFor === key ? null : key;
    authValues = {};
  }

  function closeAuthForm() {
    authExpandedFor = null;
    authValues = {};
  }

  async function handleProviderSaveAuth(
    plugin: PluginProvider,
    valuesOverride?: Record<string, string | null>,
  ) {
    authSavingFor = `prismedia:${plugin.id}`;
    error = null;
    try {
      const values: Record<string, string | null> = valuesOverride ?? {};
      if (!valuesOverride) {
        for (const field of plugin.auth) {
          const value = authValues[`prismedia:${plugin.id}:${field.key}`]?.trim();
          if (value) values[field.key] = value;
        }
      }
      await savePluginAuth(plugin.id, values);
      pluginProviders = await fetchPluginProviders();
      flashMessage(`Saved credentials for ${plugin.name}`);
      closeAuthForm();
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to save credentials for ${plugin.name}`;
    } finally {
      authSavingFor = null;
    }
  }

  const prismediaProviders = $derived(
    pluginProviders.filter(
      (plugin) => !plugin.id.startsWith("stash-") && (!isSfw || !plugin.isNsfw),
    ),
  );
  const visibleInstalledProviders = $derived(
    pluginProviders.filter((plugin) => plugin.installed && (!isSfw || !plugin.isNsfw)),
  );
  const installedProviderIds = $derived(
    new Set(pluginProviders.filter((plugin) => plugin.installed).map((plugin) => plugin.id)),
  );
  const stashScraperRows = $derived<StashScraperRow[]>(
    stashScrapers.map((scraper) => ({
      providerId: scraper.providerId,
      name: scraper.name,
      version: scraper.version,
      installed: installedProviderIds.has(scraper.providerId),
    })),
  );

  const visibleTabs = $derived<PluginTabDefinition[]>(
    (
      [
        { key: "installed", label: "Installed", count: visibleInstalledProviders.length, nsfw: false },
        {
          key: "prismedia-index",
          label: "Prismedia Community",
          count: prismediaProviders.length || null,
          nsfw: false,
        },
        {
          key: "stash-index",
          label: "Stash Community",
          count: stashScrapers.length || null,
          nsfw: true,
        },
      ] as PluginTabDefinition[]
    ).filter((definition) => !isSfw || !definition.nsfw),
  );
</script>

<svelte:head>
  <title>Plugins · Prismedia</title>
</svelte:head>

<PluginPageShell
  {loading}
  {error}
  {message}
  {tab}
  {visibleTabs}
  onDismissError={() => (error = null)}
  onTabChange={(nextTab) => (tab = nextTab)}
>
  {#if tab === "installed"}
    <InstalledPluginsTab
      bind:authValues
      {authExpandedFor}
      {authSavingFor}
      {isSfw}
      onAuthCancel={closeAuthForm}
      onProviderAuthToggle={toggleProviderAuthExpanded}
      onProviderInstall={(plugin) => void handleProviderInstall(plugin)}
      onProviderRemove={(plugin) => void handleProviderRemove(plugin)}
      onProviderSaveAuth={(plugin) => void handleProviderSaveAuth(plugin)}
      onProviderUpdate={(plugin) => void handleProviderUpdate(plugin)}
      {providerInstallingId}
      {providerRemovingId}
      {providerUpdatingId}
      providers={visibleInstalledProviders}
    />
  {/if}

  {#if tab === "prismedia-index"}
    <PrismediaCommunityTab
      {authSavingFor}
      installingId={providerInstallingId}
      loaded={prismediaLoaded}
      loading={prismediaLoading}
      onInstall={(plugin) => void handleProviderInstall(plugin)}
      onRefresh={() => void loadPrismediaIndex()}
      onSaveAuth={(plugin, values) => void handleProviderSaveAuth(plugin, values)}
      plugins={prismediaProviders}
    />
  {/if}

  {#if tab === "stash-index" && !isSfw}
    <StashCommunityIndexTab
      entries={stashScraperRows}
      installingId={providerInstallingId}
      loaded={stashIndexLoaded}
      loading={stashIndexLoading}
      onInstall={(providerId) => void handleStashInstall(providerId)}
      onRefresh={() => void loadStashIndex()}
    />
  {/if}
</PluginPageShell>
