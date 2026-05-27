<script lang="ts">
  // TODO(refactor): split into per-tab routes (`installed/`,
  // `prismedia-index/`, `stash-index/`, `stashbox/`) under a tab-shell
  // `+layout.svelte`. Deferred because each tab carries install /
  // uninstall / auth-key / endpoint-edit flows that need browser QA
  // before the seams are safe to cut.
  import { onMount } from "svelte";
  import InstalledPluginsTab from "./InstalledPluginsTab.svelte";
  import PluginPageShell from "./PluginPageShell.svelte";
  import PrismediaCommunityTab from "./PrismediaCommunityTab.svelte";
  import StashCommunityIndexTab from "./StashCommunityIndexTab.svelte";
  import StashBoxEndpointsTab from "./StashBoxEndpointsTab.svelte";
  import type { PluginTabDefinition, PluginsTab } from "./plugin-page-types";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    fetchCommunityIndex,
    fetchInstalledScrapers,
    fetchPluginUpdates,
    fetchPluginProviders,
    fetchStashBoxEndpoints,
    installPrismediaPlugin,
    installPlugin,
    installScraper,
    removePlugin,
    savePluginAuthKey,
    savePluginAuth,
    uninstallScraper,
    uninstallPlugin,
    toggleScraper,
    togglePlugin,
    createStashBoxEndpoint,
    updateStashBoxEndpoint,
    deleteStashBoxEndpoint,
    testStashBoxEndpoint,
    type InstalledPlugin,
    type PluginUpdateStatus,
    type CommunityIndexEntry,
    type PluginProvider,
    type ScraperPackage,
    type StashBoxEndpoint,
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
  let pluginUpdates = $state<Record<string, PluginUpdateStatus>>({});
  let updatingPluginId = $state<string | null>(null);
  let checkingUpdates = $state(false);
  let authExpandedFor = $state<string | null>(null);
  let authValues = $state<Record<string, string>>({});
  let authSavingFor = $state<string | null>(null);

  let indexEntries = $state<CommunityIndexEntry[]>([]);
  let indexLoading = $state(false);
  let indexLoaded = $state(false);
  let installingId = $state<string | null>(null);

  let prismediaLoading = $state(false);
  let prismediaLoaded = $state(false);

  let stashBoxEndpoints = $state<StashBoxEndpoint[]>([]);
  let showStashBoxForm = $state(false);
  let editingStashBox = $state<StashBoxEndpoint | null>(null);
  let sbName = $state("");
  let sbEndpoint = $state("");
  let sbApiKey = $state("");
  let sbSaving = $state(false);
  let sbTesting = $state<string | null>(null);
  let sbTestResult = $state<{ id: string; valid: boolean; error?: string } | null>(null);

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
      const rows = await fetchPluginUpdates({ refresh });
      const map: Record<string, PluginUpdateStatus> = {};
      for (const row of rows) map[row.pluginId] = row;
      pluginUpdates = map;
    } catch {
      /* registry unreachable — ignore */
    } finally {
      checkingUpdates = false;
    }
  }

  async function loadInstalled() {
    try {
      const [scrapersRes, endpointsRes, providers] = await Promise.all([
        fetchInstalledScrapers().catch(() => ({ packages: [] as ScraperPackage[] })),
        fetchStashBoxEndpoints().catch(() => ({ endpoints: [] as StashBoxEndpoint[] })),
        fetchPluginProviders().catch(() => [] as PluginProvider[]),
      ]);
      installed = scrapersRes.packages;
      stashBoxEndpoints = endpointsRes.endpoints;
      installedPlugins = [];
      pluginProviders = providers;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load plugins";
    } finally {
      loading = false;
    }
  }

  async function loadStashIndex(force = false) {
    indexLoading = true;
    error = null;
    try {
      const res = await fetchCommunityIndex(force);
      indexEntries = res.entries;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to fetch community index";
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
    if (isSfw && (tab === "stash-index" || tab === "stashbox")) tab = "installed";
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
      indexEntries = indexEntries.map((e) =>
        e.id === pkg.packageId ? { ...e, installed: false } : e,
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove";
    }
  }

  async function handleScraperInstall(packageId: string) {
    installingId = packageId;
    error = null;
    try {
      await installScraper(packageId);
      flashMessage(`Installed ${packageId}`);
      await loadInstalled();
      indexEntries = indexEntries.map((e) =>
        e.id === packageId ? { ...e, installed: true } : e,
      );
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${packageId}`;
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

  function openAddStashBox() {
    editingStashBox = null;
    sbName = "";
    sbEndpoint = "";
    sbApiKey = "";
    sbTestResult = null;
    showStashBoxForm = true;
  }

  function openEditStashBox(ep: StashBoxEndpoint) {
    editingStashBox = ep;
    sbName = ep.name;
    sbEndpoint = ep.endpoint;
    sbApiKey = "";
    sbTestResult = null;
    showStashBoxForm = true;
  }

  async function saveStashBox() {
    sbSaving = true;
    error = null;
    try {
      if (editingStashBox) {
        const updates: { name?: string; endpoint?: string; apiKey?: string } = {};
        if (sbName !== editingStashBox.name) updates.name = sbName;
        if (sbEndpoint !== editingStashBox.endpoint) updates.endpoint = sbEndpoint;
        if (sbApiKey) updates.apiKey = sbApiKey;
        const updated = await updateStashBoxEndpoint(editingStashBox.id, updates);
        stashBoxEndpoints = stashBoxEndpoints.map((e) => (e.id === updated.id ? updated : e));
      } else {
        if (!sbApiKey) {
          error = "API key is required";
          sbSaving = false;
          return;
        }
        const created = await createStashBoxEndpoint({
          name: sbName,
          endpoint: sbEndpoint,
          apiKey: sbApiKey,
        });
        stashBoxEndpoints = [...stashBoxEndpoints, created];
      }
      showStashBoxForm = false;
      flashMessage(editingStashBox ? "Endpoint updated." : "Endpoint added.");
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to save";
    } finally {
      sbSaving = false;
    }
  }

  async function testEndpoint(ep: StashBoxEndpoint) {
    sbTesting = ep.id;
    sbTestResult = null;
    try {
      const r = await testStashBoxEndpoint(ep.id);
      sbTestResult = { id: ep.id, ...r };
    } catch {
      sbTestResult = { id: ep.id, valid: false, error: "Request failed" };
    } finally {
      sbTesting = null;
    }
  }

  async function toggleEndpointEnabled(ep: StashBoxEndpoint) {
    try {
      await updateStashBoxEndpoint(ep.id, { enabled: !ep.enabled });
      stashBoxEndpoints = stashBoxEndpoints.map((e) =>
        e.id === ep.id ? { ...e, enabled: !e.enabled } : e,
      );
      flashMessage(`${ep.name} ${ep.enabled ? "disabled" : "enabled"}.`);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to toggle";
    }
  }

  async function deleteEndpoint(ep: StashBoxEndpoint) {
    try {
      await deleteStashBoxEndpoint(ep.id);
      stashBoxEndpoints = stashBoxEndpoints.filter((e) => e.id !== ep.id);
      flashMessage(`${ep.name} removed.`);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to remove";
    }
  }

  /* ─── Derived filtering ───────────────────────────────────── */

  const visibleScrapers = $derived(isSfw ? installed.filter((p) => !p.isNsfw) : installed);
  const visibleInstalledPlugins = $derived(
    isSfw ? installedPlugins.filter((p) => !p.isNsfw) : installedPlugins,
  );
  const visibleProviderPlugins = $derived(pluginProviders);
  const visibleInstalledProviders = $derived(visibleProviderPlugins.filter((plugin) => plugin.installed));

  const videoCount = $derived(
    visibleScrapers.filter((pkg) => {
      const caps = pkg.capabilities as Record<string, boolean> | null;
      return !!caps && (caps.sceneByURL || caps.sceneByFragment || caps.sceneByName);
    }).length,
  );
  const performerCount = $derived(
    visibleScrapers.filter((pkg) => {
      const caps = pkg.capabilities as Record<string, boolean> | null;
      return !!caps && (caps.performerByURL || caps.performerByName || caps.performerByFragment);
    }).length,
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
          count: visibleProviderPlugins.length || null,
          nsfw: false,
        },
        {
          key: "stash-index",
          label: "Stash Community",
          count: indexEntries.length || null,
          nsfw: true,
        },
        {
          key: "stashbox",
          label: "StashBox Endpoints",
          count: stashBoxEndpoints.length,
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
  {isSfw}
  {loading}
  {error}
  {message}
  {tab}
  {visibleTabs}
  {installedCount}
  {videoCount}
  {performerCount}
  stashBoxCount={stashBoxEndpoints.length}
  prismediaCount={visibleProviderPlugins.length}
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
        onScraperRemove={(pkg) => void handleUninstall(pkg)}
        onScraperToggle={(pkg) => void handleToggle(pkg)}
        {pluginUpdates}
        {providerInstallingId}
        {providerRemovingId}
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
        plugins={visibleProviderPlugins}
      />
    {/if}
    <!-- STASH COMMUNITY INDEX TAB -->
    {#if tab === "stash-index" && !isSfw}
      <StashCommunityIndexTab
        entries={indexEntries}
        {installingId}
        loaded={indexLoaded}
        loading={indexLoading}
        onInstall={(id) => void handleScraperInstall(id)}
        onRefresh={() => void loadStashIndex(true)}
      />
    {/if}

    <!-- STASHBOX ENDPOINTS TAB -->
    {#if tab === "stashbox" && !isSfw}
      <StashBoxEndpointsTab
        bind:apiKey={sbApiKey}
        bind:endpoint={sbEndpoint}
        bind:name={sbName}
        bind:showForm={showStashBoxForm}
        editingEndpoint={editingStashBox}
        endpoints={stashBoxEndpoints}
        onAdd={openAddStashBox}
        onDelete={(endpoint) => void deleteEndpoint(endpoint)}
        onEdit={openEditStashBox}
        onSave={() => void saveStashBox()}
        onTest={(endpoint) => void testEndpoint(endpoint)}
        onToggleEnabled={(endpoint) => void toggleEndpointEnabled(endpoint)}
        saving={sbSaving}
        testingId={sbTesting}
        testResult={sbTestResult}
      />
    {/if}
</PluginPageShell>
