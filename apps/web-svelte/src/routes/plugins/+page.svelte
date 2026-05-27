<script lang="ts">
  // TODO(refactor): split into per-tab routes (`installed/`,
  // `prismedia-index/`, `stash-index/`, `stashbox/`) under a tab-shell
  // `+layout.svelte`. Deferred because each tab carries install /
  // uninstall / auth-key / endpoint-edit flows that need browser QA
  // before the seams are safe to cut.
  import { onMount } from "svelte";
  import {
    AlertCircle,
    Check,
    Download,
    Film,
    KeyRound,
    Loader2,
    Package,
    RefreshCw,
    Search,
    Sparkles,
    ToggleLeft,
    ToggleRight,
    Trash2,
    Users,
    X,
  } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import PluginPageShell from "./PluginPageShell.svelte";
  import PluginCredentialForm from "./PluginCredentialForm.svelte";
  import PrismediaCommunityTab from "./PrismediaCommunityTab.svelte";
  import StashCommunityIndexTab from "./StashCommunityIndexTab.svelte";
  import StashBoxEndpointsTab from "./StashBoxEndpointsTab.svelte";
  import { authPlaceholder } from "./plugin-auth-format";
  import type { PluginTabDefinition, PluginsTab } from "./plugin-page-types";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { entityTerms } from "$lib/terminology";
  import {
    fetchCommunityIndex,
    fetchInstalledScrapers,
    fetchInstalledPlugins,
    fetchPrismediaPluginIndex,
    fetchPluginUpdates,
    fetchStashBoxEndpoints,
    installPrismediaPlugin,
    installScraper,
    savePluginAuthKey,
    uninstallScraper,
    uninstallPlugin,
    toggleScraper,
    togglePlugin,
    createStashBoxEndpoint,
    updateStashBoxEndpoint,
    deleteStashBoxEndpoint,
    testStashBoxEndpoint,
    type PrismediaPluginIndexEntry,
    type InstalledPlugin,
    type PluginUpdateStatus,
    type CommunityIndexEntry,
    type ScraperPackage,
    type StashBoxEndpoint,
  } from "$lib/api/plugins";
  import {
    fetchPluginProviders,
    installPlugin,
    removePlugin,
    savePluginAuth,
    type PluginProvider as PluginProvider,
  } from "$lib/api/identify";

  /* ─── Capability label map ──────────────────────────────────── */

  const CAPABILITY_META: Record<string, { label: string; category: string }> = {
    sceneByURL: { label: "Video by URL", category: "scene" },
    sceneByFragment: { label: "Video by fragment", category: "scene" },
    sceneByName: { label: "Video by name", category: "scene" },
    sceneByQueryFragment: { label: "Video by query", category: "scene" },
    performerByURL: { label: "Person by URL", category: "performer" },
    performerByName: { label: "Person by name", category: "performer" },
    performerByFragment: { label: "Person by fragment", category: "performer" },
    galleryByURL: { label: "Gallery by URL", category: "gallery" },
    galleryByFragment: { label: "Gallery by fragment", category: "gallery" },
    bookByURL: { label: "Book by URL", category: "book" },
    bookByName: { label: "Book by name", category: "book" },
    bookByFragment: { label: "Book by fragment", category: "book" },
    comicByURL: { label: "Comic by URL", category: "book" },
    comicByName: { label: "Comic by name", category: "book" },
    comicByFragment: { label: "Comic by fragment", category: "book" },
    mangaByURL: { label: "Manga by URL", category: "book" },
    mangaByName: { label: "Manga by name", category: "book" },
    mangaByFragment: { label: "Manga by fragment", category: "book" },
    groupByURL: { label: "Group by URL", category: "group" },
    videoByURL: { label: "Video by URL", category: "scene" },
    videoByName: { label: "Video by name", category: "scene" },
    folderByName: { label: "Series by name", category: "folder" },
    folderCascade: { label: "Episode cascade", category: "folder" },
    audioByURL: { label: "Audio by URL", category: "audio" },
    audioLibraryByName: { label: "Album by name", category: "audio" },
  };

  type CapFilter = "all" | "scene" | "performer";

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
  let capFilter = $state<CapFilter>("all");
  let installedSearch = $state("");

  let indexEntries = $state<CommunityIndexEntry[]>([]);
  let indexLoading = $state(false);
  let indexLoaded = $state(false);
  let installingId = $state<string | null>(null);

  let prismediaEntries = $state<PrismediaPluginIndexEntry[]>([]);
  let prismediaLoading = $state(false);
  let prismediaLoaded = $state(false);
  let prismediaInstallingId = $state<string | null>(null);

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

  async function handlePrismediaInstall(entry: PrismediaPluginIndexEntry) {
    prismediaInstallingId = entry.id;
    error = null;
    try {
      await installPrismediaPlugin(entry.id, {
        localPath: entry.localPath,
        zipUrl: entry.localPath ? undefined : entry.path,
        sha256: entry.sha256 || undefined,
      });
      flashMessage(`Installed ${entry.name}`);
      prismediaEntries = prismediaEntries.map((e) =>
        e.id === entry.id ? { ...e, installed: true } : e,
      );
      await loadInstalled();
    } catch (err) {
      error = err instanceof Error ? err.message : `Failed to install ${entry.name}`;
    } finally {
      prismediaInstallingId = null;
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
      prismediaEntries = prismediaEntries.map((e) =>
        e.id === plugin.pluginId ? { ...e, installed: false } : e,
      );
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

  function matchesProviderCapabilityFilter(plugin: PluginProvider) {
    if (capFilter === "all") return true;
    if (capFilter === "scene") {
      return plugin.supports.some((support) =>
        support.entityKind === "video" || support.entityKind === "video-series",
      );
    }

    return false;
  }

  const filteredScrapers = $derived.by(() => {
    const q = installedSearch.trim().toLowerCase();
    return visibleScrapers.filter((pkg) => {
      if (q && !pkg.name.toLowerCase().includes(q) && !pkg.packageId.toLowerCase().includes(q)) {
        return false;
      }
      if (capFilter === "all") return true;
      const caps = pkg.capabilities as Record<string, boolean> | null;
      if (!caps) return false;
      if (capFilter === "scene")
        return !!(caps.sceneByURL || caps.sceneByFragment || caps.sceneByName || caps.sceneByQueryFragment);
      if (capFilter === "performer")
        return !!(caps.performerByURL || caps.performerByName || caps.performerByFragment);
      return true;
    });
  });

  const filteredProviders = $derived.by(() => {
    const q = installedSearch.trim().toLowerCase();
    return visibleInstalledProviders.filter((plugin) => {
      if (q && !plugin.name.toLowerCase().includes(q) && !plugin.id.toLowerCase().includes(q)) {
        return false;
      }

      return matchesProviderCapabilityFilter(plugin);
    });
  });

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

  function enabledCaps(caps: Record<string, boolean> | null | undefined): string[] {
    if (!caps) return [];
    return Object.entries(caps)
      .filter(([, v]) => v)
      .map(([k]) => k);
  }

  function providerSupportLabels(plugin: PluginProvider): string[] {
    return plugin.supports.map((support) =>
      `${support.entityKind}: ${support.actions.join(", ")}`,
    );
  }
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
      <section class="space-y-2">
        <div class="surface-well flex items-center gap-2 px-3 py-2 flex-wrap">
          <div class="relative">
            <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
            <input
              class="control-input pl-8 w-56 py-1.5 text-sm"
              placeholder="Search installed..."
              bind:value={installedSearch}
            />
            {#if installedSearch}
              <button
                onclick={() => (installedSearch = "")}
                aria-label="Clear search"
                class="absolute right-2 top-1/2 -translate-y-1/2 text-text-disabled hover:text-text-muted"
              >
                <X class="h-3 w-3" />
              </button>
            {/if}
          </div>
          {#if !isSfw}
            <div class="w-px h-4 bg-border-subtle mx-1"></div>
            {#each ["all", "scene", "performer"] as const as filter (filter)}
              <button
                onclick={() => (capFilter = filter)}
                class={"flex items-center gap-1.5 px-2.5 py-1.5 rounded-xs text-xs transition-all duration-fast " +
                  (capFilter === filter
                    ? "bg-accent-950 text-text-accent border border-border-accent"
                    : "text-text-muted hover:text-text-secondary border border-transparent")}
              >
                {#if filter === "all"}
                  <Package class="h-3 w-3" />All
                {:else if filter === "scene"}
                  <Film class="h-3 w-3" />{entityTerms.videos}
                {:else}
                  <Users class="h-3 w-3" />{entityTerms.performers}
                {/if}
              </button>
            {/each}
          {/if}
          <div class="flex-1"></div>
          {#if installedPlugins.length > 0}
            <Button
              variant="ghost"
              size="sm"
              onclick={() => void loadPluginUpdates(true)}
              disabled={checkingUpdates}
              class="h-auto gap-1.5 px-2.5 py-1.5 text-xs"
            >
              {#snippet children()}
                {#if checkingUpdates}
                  <Loader2 class="h-3.5 w-3.5 animate-spin" />
                {:else}
                  <RefreshCw class="h-3.5 w-3.5" />
                {/if}
                Check for updates
              {/snippet}
            </Button>
          {/if}
          <span class="text-mono-sm text-text-disabled">{filteredProviders.length + visibleInstalledPlugins.length + filteredScrapers.length} shown</span>
        </div>

        {#if filteredProviders.length === 0 && visibleInstalledPlugins.length === 0 && filteredScrapers.length === 0}
          <div class="surface-card no-lift p-8 text-center">
            <Package class="h-8 w-8 text-text-disabled mx-auto mb-3" />
            <p class="text-text-muted text-sm">
              {#if visibleScrapers.length === 0 && visibleInstalledPlugins.length === 0 && visibleInstalledProviders.length === 0}
                {isSfw
                  ? "No SFW plugins installed. Browse the Prismedia Community tab to find plugins."
                  : "No plugins installed. Browse the community tabs to get started."}
              {:else}
                No plugins match your filters.
              {/if}
            </p>
          </div>
        {:else}
          <div class="space-y-1">
            {#each filteredProviders as plugin (plugin.id)}
              {@const authExpanded = authExpandedFor === `prismedia:${plugin.id}`}
              {@const hasAuth = plugin.auth.length > 0}
              <div
                class={"surface-card no-lift transition-opacity duration-fast " +
                  (plugin.installed && plugin.enabled ? "" : "opacity-80")}
              >
                <div class="p-4">
                  <div class="flex flex-wrap items-start justify-between gap-3">
                    <div class="min-w-0 flex-1">
                      <div class="flex items-center gap-2.5 flex-wrap">
                        <p class="text-sm font-semibold">{plugin.name}</p>
                        <Badge variant={plugin.installed && plugin.enabled ? "accent" : "default"}>
                          {#snippet children()}{plugin.installed && plugin.enabled ? "Installed" : "Available"}{/snippet}
                        </Badge>
                        {#if plugin.missingAuthKeys.length === 0 && hasAuth}
                          <Badge variant="success">
                            {#snippet children()}<Check class="h-2.5 w-2.5" />Auth OK{/snippet}
                          </Badge>
                        {:else if plugin.missingAuthKeys.length > 0}
                          <Badge variant="warning">
                            {#snippet children()}<AlertCircle class="h-2.5 w-2.5" />Auth Required{/snippet}
                          </Badge>
                        {/if}
                      </div>
                      <p class="text-mono-sm text-text-disabled mt-0.5">
                        {plugin.id} · v{plugin.version} · dotnet-process
                      </p>
                      <div class="flex flex-wrap items-center gap-1.5 mt-2.5">
                        {#each providerSupportLabels(plugin) as label (label)}
                          <span class="tag-chip-default text-[0.6rem] px-1.5 py-0.5">{label}</span>
                        {/each}
                      </div>
                    </div>
                    <div class="flex items-center gap-2 shrink-0">
                      {#if !plugin.installed || !plugin.enabled}
                        <button
                          onclick={() => void handleProviderInstall(plugin)}
                          disabled={providerInstallingId === plugin.id}
                          class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-text-accent transition-colors duration-fast disabled:opacity-40"
                        >
                          {#if providerInstallingId === plugin.id}
                            <Loader2 class="h-3.5 w-3.5 animate-spin" />
                          {:else}
                            <Download class="h-3.5 w-3.5" />
                          {/if}
                          Install
                        </button>
                      {/if}
                      {#if hasAuth}
                        <button
                          onclick={() => toggleProviderAuthExpanded(plugin.id)}
                          class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                            (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                        >
                          <KeyRound class="h-3.5 w-3.5" />
                          {authExpanded ? "Close" : "Configure"}
                        </button>
                      {/if}
                      {#if plugin.installed}
                        <button
                          onclick={() => void handleRemove(plugin)}
                          disabled={providerRemovingId === plugin.id}
                          class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-status-error-text transition-colors duration-fast disabled:opacity-40"
                        >
                          {#if providerRemovingId === plugin.id}
                            <Loader2 class="h-3.5 w-3.5 animate-spin" />
                          {:else}
                            <Trash2 class="h-3.5 w-3.5" />
                          {/if}
                          Remove
                        </button>
                      {/if}
                    </div>
                  </div>
                </div>

                {#if authExpanded}
                  <PluginCredentialForm
                    bind:values={authValues}
                    fields={plugin.auth}
                    getPlaceholder={(field) =>
                      plugin.missingAuthKeys.includes(field.key)
                        ? "Required"
                        : "Saved - enter a new value to replace"}
                    getValueKey={(field) => `prismedia:${plugin.id}:${field.key}`}
                    inputIdPrefix={`plugin-auth-${plugin.id}`}
                    onCancel={closeAuthForm}
                    onSave={() => void handleProviderSaveAuth(plugin)}
                    saving={authSavingFor === `prismedia:${plugin.id}`}
                  />
                {/if}
              </div>
            {/each}
            {#each visibleInstalledPlugins as plugin (plugin.id)}
              {@const update = pluginUpdates[plugin.pluginId]}
              {@const caps = enabledCaps(plugin.capabilities)}
              {@const hasAuth = !!plugin.authFields && plugin.authFields.length > 0}
              {@const authExpanded = authExpandedFor === plugin.id}
              <div
                class={"surface-card no-lift transition-opacity duration-fast " +
                  (plugin.enabled ? "" : "opacity-60")}
              >
                <div class="p-4">
                  <div class="flex flex-wrap items-start justify-between gap-3">
                    <div class="min-w-0 flex-1">
                      <div class="flex items-center gap-2.5 flex-wrap">
                        <p class="text-sm font-semibold">{plugin.name}</p>
                        <span class="tag-chip tag-chip-accent text-[0.55rem]">Prismedia</span>
                        {#if plugin.isNsfw}
                          <span class="tag-chip text-[0.55rem] bg-status-error/10 text-status-error-text border border-status-error/20">NSFW</span>
                        {/if}
                        <Badge variant={plugin.enabled ? "accent" : "default"}>
                          {#snippet children()}{plugin.enabled ? "Enabled" : "Disabled"}{/snippet}
                        </Badge>
                        {#if update?.updateAvailable}
                          <Badge variant="success">
                            {#snippet children()}<Sparkles class="h-2.5 w-2.5" />Update available{/snippet}
                          </Badge>
                        {/if}
                        {#if hasAuth}
                          {#if plugin.authStatus === "ok"}
                            <Badge variant="success">
                              {#snippet children()}<Check class="h-2.5 w-2.5" />Auth OK{/snippet}
                            </Badge>
                          {:else}
                            <Badge variant="warning">
                              {#snippet children()}<AlertCircle class="h-2.5 w-2.5" />Auth Required{/snippet}
                            </Badge>
                          {/if}
                        {/if}
                      </div>
                      <p class="text-mono-sm text-text-disabled mt-0.5">
                        {plugin.pluginId} · v{plugin.version} · {plugin.runtime}
                      </p>
                      {#if caps.length > 0}
                        <div class="flex flex-wrap items-center gap-1.5 mt-2.5">
                          {#each caps as key (key)}
                            <span class="tag-chip-default text-[0.6rem] px-1.5 py-0.5">
                              {CAPABILITY_META[key]?.label ?? key}
                            </span>
                          {/each}
                        </div>
                      {/if}
                    </div>
                    <div class="flex items-center gap-2 shrink-0">
                      {#if update?.updateAvailable}
                        <button
                          onclick={() => void handlePluginUpdate(plugin)}
                          disabled={updatingPluginId === plugin.id}
                          class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-status-success-text hover:text-text-primary transition-colors duration-fast disabled:opacity-40"
                        >
                          {#if updatingPluginId === plugin.id}
                            <Loader2 class="h-3.5 w-3.5 animate-spin" />
                          {:else}
                            <Download class="h-3.5 w-3.5" />
                          {/if}
                          Update
                        </button>
                      {/if}
                      {#if hasAuth}
                        <button
                          onclick={() => toggleInstalledPluginAuthExpanded(plugin.id)}
                          class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                            (plugin.authStatus === "missing" ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                        >
                          <KeyRound class="h-3.5 w-3.5" />
                          {authExpanded ? "Close" : "Configure"}
                        </button>
                      {/if}
                      <button
                        onclick={() => void handlePluginToggle(plugin)}
                        class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast text-text-muted hover:text-text-primary"
                      >
                        {#if plugin.enabled}
                          <ToggleRight class="h-4 w-4 text-text-accent" />Disable
                        {:else}
                          <ToggleLeft class="h-4 w-4" />Enable
                        {/if}
                      </button>
                      <button
                        onclick={() => void handlePluginRemove(plugin)}
                        class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-status-error-text transition-colors duration-fast"
                      >
                        <Trash2 class="h-3.5 w-3.5" />Remove
                      </button>
                    </div>
                  </div>
                </div>

                {#if authExpanded && plugin.authFields}
                  <PluginCredentialForm
                    bind:values={authValues}
                    fields={plugin.authFields}
                    getPlaceholder={(field) => authPlaceholder(plugin, field)}
                    getValueKey={(field) => field.key}
                    inputIdPrefix={`auth-${plugin.id}`}
                    onCancel={closeAuthForm}
                    onSave={() => void handleInstalledPluginSaveAuth(plugin)}
                    saving={authSavingFor === plugin.id}
                  />
                {/if}
              </div>
            {/each}
            {#each filteredScrapers as pkg (pkg.id)}
              {@const caps = enabledCaps(pkg.capabilities as Record<string, boolean> | null)}
              <div class={"surface-card no-lift p-4 transition-opacity duration-fast " + (pkg.enabled ? "" : "opacity-60")}>
                <div class="flex flex-wrap items-start justify-between gap-3">
                  <div class="min-w-0 flex-1">
                    <div class="flex items-center gap-2.5 flex-wrap">
                      <p class="text-sm font-semibold">{pkg.name}</p>
                      <span class={"tag-chip text-[0.55rem] " + (pkg.isNsfw ? "tag-chip-default" : "tag-chip-accent")}>
                        {pkg.isNsfw ? "Stash" : "Prismedia"}
                      </span>
                      {#if pkg.isNsfw}
                        <span class="tag-chip text-[0.55rem] bg-status-error/10 text-status-error-text border border-status-error/20">NSFW</span>
                      {/if}
                      <Badge variant={pkg.enabled ? "accent" : "default"}>
                        {#snippet children()}{pkg.enabled ? "Enabled" : "Disabled"}{/snippet}
                      </Badge>
                    </div>
                    <p class="text-mono-sm text-text-disabled mt-0.5">{pkg.packageId}</p>
                    {#if caps.length > 0}
                      <div class="flex flex-wrap items-center gap-1.5 mt-2.5">
                        {#each caps as key (key)}
                          <span class={"rounded-xs text-[0.6rem] px-1.5 py-0.5 " + (CAPABILITY_META[key]?.category === "performer" ? "bg-accent-950/80 text-text-accent border border-border-accent/30" : "tag-chip-default")}>
                            {CAPABILITY_META[key]?.label ?? key}
                          </span>
                        {/each}
                      </div>
                    {/if}
                  </div>
                  <div class="flex items-center gap-2 shrink-0">
                    <button
                      onclick={() => void handleToggle(pkg)}
                      class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast text-text-muted hover:text-text-primary"
                    >
                      {#if pkg.enabled}
                        <ToggleRight class="h-4 w-4 text-text-accent" />Disable
                      {:else}
                        <ToggleLeft class="h-4 w-4" />Enable
                      {/if}
                    </button>
                    <button
                      onclick={() => void handleUninstall(pkg)}
                      class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-status-error-text transition-colors duration-fast"
                    >
                      <Trash2 class="h-3.5 w-3.5" />Remove
                    </button>
                  </div>
                </div>
              </div>
            {/each}
          </div>
        {/if}
      </section>
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
