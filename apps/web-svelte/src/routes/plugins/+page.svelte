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
    fetchInstalledScrapers,
    fetchPluginUpdates,
    fetchPluginProviders,
    fetchStashScrapers,
    installPrismediaPlugin,
    installPlugin,
    removePlugin,
    savePluginAuthKey,
    savePluginAuth,
    uninstallScraper,
    uninstallPlugin,
    toggleScraper,
    togglePlugin,
    updatePlugin,
    type InstalledPlugin,
    type PluginUpdateStatus,
    type StashScraperListing,
    type PluginProvider,
    type ScraperPackage,
  } from "$lib/api/plugins";

  const nsfw = useNsfw();
  const isSfw = $derived(nsfw.mode === "off");

  /* ─── State ───────────────────────────────────────────────── */

  let tab = $state<PluginsTab>("installed");
  let loading = $state(true);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);

  let installed = $state<ScraperPackage[]>([]);
  let installedPlugins = $state<InstalledPlugin[]>([]);
  let pluginProviders = $state<PluginProvider[]>([]);
  let providerInstallingId = $state<string | null>(null);
  let providerRemovingId = $state<string | null>(null);
  let providerUpdatingId = $state<string | null>(null);
  let pluginUpdates = $state<Record<string, PluginUpdateStatus>>({});
  let updatingPluginId = $state<string | null>(null);
  let checkingUpdates = $state(false);
  let authExpandedFor = $state<string | null>(null);
  let authValues = $state<Record<string, string>>({});
  let authSavingFor = $state<string | null>(null);

  let stashScrapers = $state<StashScraperListing[]>([]);
  let indexLoading = $state(false);
  let indexLoaded = $state(false);
  let installingId = $state<string | null>(null);

  let prismediaLoading = $state(false);
  let prismediaLoaded = $state(false);

  function flashMessage(msg: string) {
    message = msg;
    setTimeout(() => {
      if (message === msg) message = null;
    }, 3000);
  }

  /* ─── Data loading ────────────────────────────────────────── */

  async function loadPluginUpdates(refresh = false) {
    checkingUpdates = true;
    try {
      const [rows, providers] = await Promise.all([
        fetchPluginUpdates({ refresh }).catch(() => [] as PluginUpdateStatus[]),
        fetchPluginProviders(),
      ]);
      const map: Record<string, PluginUpdateStatus> = {};
      for (const row of rows) map[row.pluginId] = row;
      pluginUpdates = map;
      pluginProviders = providers;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to check plugin updates";
    } finally {
      checkingUpdates = false;
    }
  }

  async function loadInstalled() {
    try {
      const [scrapersRes, providers] = await Promise.all([
        fetchInstalledScrapers().catch(() => ({ packages: [] as ScraperPackage[] })),
        fetchPluginProviders().catch(() => [] as PluginProvider[]),
      ]);
      installed = scrapersRes.packages;
      installedPlugins = [];
      pluginProviders = providers;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load plugins";
    } finally {
      loading = false;
    }
  }

  async function loadStashIndex(_force = false) {
    indexLoading = true;
    error = null;
    try {
      stashScrapers = await fetchStashScrapers();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to fetch Stash community index";
    } finally {
      indexLoaded = true;
      indexLoading = false;
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

  // In SFW mode, force tab away from NSFW tabs.
  $effect(() => {
    if (isSfw && tab === "stash-index") tab = "installed";
  });

  // Auto-load indices when switching tabs.
  $effect(() => {
    if (tab === "stash-index" && !indexLoaded && !indexLoading) void loadStashIndex();
    if (tab === "prismedia-index" && !prismediaLoaded && !prismediaLoading) void loadPrismediaIndex();
  });

  /* ─── Actions ─────────────────────────────────────────────── */

  async function handleToggle(pkg: ScraperPackage) {
    try {
      const updated = await toggleScraper(pkg.id, !pkg.enabled);
      installed = installed.map((p) => (p.id === updated.id ? updated : p));
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to toggle";
    }
  }

  async function handleUninstall(pkg: ScraperPackage) {
    error = null;
    try {
      await uninstallScraper(pkg.id);
      flashMessage(`Removed ${pkg.name}`);
      await loadInstalled();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove";
    }
  }

  async function handleScraperInstall(providerId: string) {
    installingId = providerId;
    error = null;
    try {
      const provider = await installPlugin(providerId);
      pluginProviders = pluginProviders.some((row) => row.id === provider.id)
        ? pluginProviders.map((row) => (row.id === provider.id ? provider : row))
        : [...pluginProviders, provider];
      flashMessage(`Installed ${provider.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${providerId}`;
    } finally {
      installingId = null;
    }
  }

  async function handlePluginUpdate(plugin: InstalledPlugin) {
    const update = pluginUpdates[plugin.pluginId];
    if (!update || !update.updateAvailable || !update.zipUrl) return;
    updatingPluginId = plugin.id;
    error = null;
    try {
      await installPrismediaPlugin(plugin.pluginId, {
        zipUrl: update.zipUrl,
        sha256: update.sha256 || undefined,
      });
      flashMessage(`Updated ${plugin.name} → v${update.availableVersion}`);
      await loadInstalled();
      await loadPluginUpdates(true);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to update plugin";
    } finally {
      updatingPluginId = null;
    }
  }

  async function handlePluginToggle(plugin: InstalledPlugin) {
    try {
      await togglePlugin(plugin.id, !plugin.enabled);
      installedPlugins = installedPlugins.map((p) =>
        p.id === plugin.id ? { ...p, enabled: !p.enabled } : p,
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to toggle";
    }
  }

  async function handlePluginRemove(plugin: InstalledPlugin) {
    try {
      await uninstallPlugin(plugin.id);
      flashMessage(`Removed ${plugin.name}`);
      installedPlugins = installedPlugins.filter((p) => p.id !== plugin.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove";
    }
  }

  function toggleInstalledPluginAuthExpanded(pluginId: string) {
    if (authExpandedFor === pluginId) {
      authExpandedFor = null;
      authValues = {};
    } else {
      authExpandedFor = pluginId;
      authValues = {};
    }
  }

  function closeAuthForm() {
    authExpandedFor = null;
    authValues = {};
  }

  async function handleInstalledPluginSaveAuth(plugin: InstalledPlugin) {
    if (!plugin.authFields) return;
    authSavingFor = plugin.id;
    try {
      let saved = 0;
      for (const field of plugin.authFields) {
        const value = authValues[field.key]?.trim();
        if (!value) continue;
        await savePluginAuthKey(plugin.id, field.key, value);
        saved += 1;
      }
      if (saved > 0) {
        flashMessage(`Saved ${saved} credential${saved === 1 ? "" : "s"} for ${plugin.name}`);
        installedPlugins = installedPlugins.map((p) =>
          p.id === plugin.id ? { ...p, authStatus: "ok" as const } : p,
        );
      }
      authExpandedFor = null;
      authValues = {};
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to save credentials";
    } finally {
      authSavingFor = null;
    }
  }

  async function handleProviderInstall(plugin: PluginProvider) {
    providerInstallingId = plugin.id;
    error = null;
    try {
      const installed = await installPlugin(plugin.id);
      pluginProviders = pluginProviders.map((row) => (row.id === installed.id ? installed : row));
      flashMessage(`Installed ${installed.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${plugin.name}`;
    } finally {
      providerInstallingId = null;
    }
  }

  async function handleProviderUpdate(plugin: PluginProvider) {
    providerUpdatingId = plugin.id;
    error = null;
    try {
      const updated = await updatePlugin(plugin.id);
      pluginProviders = pluginProviders.map((row) => (row.id === updated.id ? updated : row));
      flashMessage(`Updated ${updated.name} -> v${updated.version}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to update ${plugin.name}`;
    } finally {
      providerUpdatingId = null;
    }
  }

  async function handleRemove(plugin: PluginProvider) {
    providerRemovingId = plugin.id;
    error = null;
    try {
      await removePlugin(plugin.id);
      pluginProviders = pluginProviders.map((row) =>
        row.id === plugin.id ? { ...row, installed: false, enabled: false } : row,
      );
      flashMessage(`Removed ${plugin.name}`);
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to remove ${plugin.name}`;
    } finally {
      providerRemovingId = null;
    }
  }

  function toggleProviderAuthExpanded(pluginId: string) {
    const key = `prismedia:${pluginId}`;
    if (authExpandedFor === key) {
      authExpandedFor = null;
      authValues = {};
    } else {
      authExpandedFor = key;
      authValues = {};
    }
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
      authExpandedFor = null;
      authValues = {};
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to save credentials for ${plugin.name}`;
    } finally {
      authSavingFor = null;
    }
  }

  /* ─── Derived filtering ───────────────────────────────────── */

  const visibleScrapers = $derived(isSfw ? installed.filter((p) => !p.isNsfw) : installed);
  const visibleInstalledPlugins = $derived(
    isSfw ? installedPlugins.filter((p) => !p.isNsfw) : installedPlugins,
  );
  // Stash scrapers are providers too, but they belong under the Stash Community tab — keep them
  // out of the Prismedia Community catalog listing. NSFW providers stay hidden in SFW mode.
  const prismediaProviders = $derived(
    pluginProviders.filter(
      (plugin) => !plugin.id.startsWith("stash-") && (!isSfw || !plugin.isNsfw),
    ),
  );
  const visibleInstalledProviders = $derived(
    pluginProviders.filter((plugin) => plugin.installed && (!isSfw || !plugin.isNsfw)),
  );

  // Resolve each available Stash scraper's installed state from the provider catalog.
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

  const installedCount = $derived(
    visibleScrapers.length + visibleInstalledPlugins.length + visibleInstalledProviders.length,
  );

  const visibleTabs = $derived<PluginTabDefinition[]>(
    (
      [
        { key: "installed", label: "Installed", count: installedCount, nsfw: false },
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
    ).filter((t) => !isSfw || !t.nsfw),
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
    <!-- INSTALLED TAB -->
    {#if tab === "installed"}
      <InstalledPluginsTab
        bind:authValues
        {authExpandedFor}
        {authSavingFor}
        {checkingUpdates}
        installedPlugins={visibleInstalledPlugins}
        {isSfw}
        onAuthCancel={closeAuthForm}
        onCheckUpdates={() => void loadPluginUpdates(true)}
        onInstalledPluginAuthToggle={toggleInstalledPluginAuthExpanded}
        onInstalledPluginRemove={(plugin) => void handlePluginRemove(plugin)}
        onInstalledPluginSaveAuth={(plugin) => void handleInstalledPluginSaveAuth(plugin)}
        onInstalledPluginToggle={(plugin) => void handlePluginToggle(plugin)}
        onInstalledPluginUpdate={(plugin) => void handlePluginUpdate(plugin)}
        onProviderAuthToggle={toggleProviderAuthExpanded}
        onProviderInstall={(plugin) => void handleProviderInstall(plugin)}
        onProviderRemove={(plugin) => void handleRemove(plugin)}
        onProviderSaveAuth={(plugin) => void handleProviderSaveAuth(plugin)}
        onProviderUpdate={(plugin) => void handleProviderUpdate(plugin)}
        onScraperRemove={(pkg) => void handleUninstall(pkg)}
        onScraperToggle={(pkg) => void handleToggle(pkg)}
        {pluginUpdates}
        {providerInstallingId}
        {providerRemovingId}
        {providerUpdatingId}
        providers={visibleInstalledProviders}
        scrapers={visibleScrapers}
        {updatingPluginId}
      />
    {/if}

    <!-- PRISMEDIA COMMUNITY INDEX TAB -->
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
    <!-- STASH COMMUNITY INDEX TAB -->
    {#if tab === "stash-index" && !isSfw}
      <StashCommunityIndexTab
        entries={stashScraperRows}
        {installingId}
        loaded={indexLoaded}
        loading={indexLoading}
        onInstall={(providerId) => void handleScraperInstall(providerId)}
        onRefresh={() => void loadStashIndex(true)}
      />
    {/if}
</PluginPageShell>
