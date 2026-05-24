<script lang="ts">
  // TODO(refactor): split into per-tab routes (`installed/`,
  // `prismedia-index/`, `stash-index/`, `stashbox/`) under a tab-shell
  // `+layout.svelte`. Deferred because each tab carries install /
  // uninstall / auth-key / endpoint-edit flows that need browser QA
  // before the seams are safe to cut.
  import { onMount } from "svelte";
  import {
    AlertCircle,
    Boxes,
    Check,
    Download,
    Film,
    Globe,
    KeyRound,
    Loader2,
    Package,
    Pencil,
    Plug,
    Plus,
    Puzzle,
    RefreshCw,
    Save,
    Search,
    Sparkles,
    ToggleLeft,
    ToggleRight,
    Trash2,
    Users,
    X,
  } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
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

  type PluginsTab = "installed" | "prismedia-index" | "stash-index" | "stashbox";
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
  let indexSearch = $state("");
  let installingId = $state<string | null>(null);

  let prismediaEntries = $state<PrismediaPluginIndexEntry[]>([]);
  let prismediaLoading = $state(false);
  let prismediaLoaded = $state(false);
  let prismediaSearch = $state("");
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

  function authFieldName(field: { key: string; label: string }) {
    const key = field.key.toLowerCase();
    if (key.includes("client_id")) return "client ID";
    if (key.includes("client_secret")) return "client secret";
    if (key.includes("username")) return "username";
    if (key.includes("password")) return "password";
    if (key.includes("api_key") || key.includes("apikey")) return "API key";
    if (key.includes("token")) return "token";
    return field.label.toLowerCase();
  }

  function authPlaceholder(
    plugin: InstalledPlugin,
    field: { key: string; label: string },
  ) {
    const name = authFieldName(field);
    if (plugin.authStatus === "ok") {
      return `Configured - enter new ${name} to replace`;
    }
    if (name === "username" || name === "password") {
      return `Enter your ${field.label}`;
    }
    return `Paste your ${field.label}`;
  }

  function authLinkLabel(field: { key: string }) {
    const key = field.key.toLowerCase();
    if (key.includes("username") || key.includes("password")) return "Open login";
    if (key.includes("client_id") || key.includes("client_secret")) return "Open settings";
    return "Get key";
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

  async function handleProviderSaveAuth(plugin: PluginProvider) {
    authSavingFor = `prismedia:${plugin.id}`;
    error = null;
    try {
      const values: Record<string, string | null> = {};
      for (const field of plugin.auth) {
        const value = authValues[`prismedia:${plugin.id}:${field.key}`]?.trim();
        if (value) values[field.key] = value;
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

  const filteredPlugins = $derived.by(() => {
    const q = prismediaSearch.trim().toLowerCase();
    return visibleProviderPlugins.filter((plugin) => {
      if (!q) return true;
      return plugin.name.toLowerCase().includes(q) || plugin.id.toLowerCase().includes(q);
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

  const filteredIndex = $derived.by(() => {
    const q = indexSearch.trim().toLowerCase();
    return q
      ? indexEntries.filter(
          (e) => e.name.toLowerCase().includes(q) || e.id.toLowerCase().includes(q),
        )
      : indexEntries;
  });

  type TabDef = { key: PluginsTab; label: string; count: number | null; nsfw: boolean };
  const visibleTabs = $derived<TabDef[]>(
    (
      [
        { key: "installed", label: "Installed", count: visibleScrapers.length + visibleInstalledPlugins.length + visibleInstalledProviders.length, nsfw: false },
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
      ] as TabDef[]
    ).filter((t) => !isSfw || !t.nsfw),
  );

  function tabIcon(key: PluginsTab) {
    if (key === "installed") return Boxes;
    if (key === "prismedia-index") return Sparkles;
    if (key === "stash-index") return Globe;
    return Plug;
  }

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

<div class="space-y-4">
  <!-- Header -->
  <div>
    <h1 class="flex items-center gap-2.5">
      <Puzzle class="h-5 w-5 text-text-accent" />
      Plugins
    </h1>
    <p class="mt-1 text-text-muted text-[0.78rem]">
      Install and manage identification plugins and metadata providers
    </p>
  </div>

  <!-- Stats -->
  <div class="grid gap-2 {isSfw ? 'grid-cols-2' : 'grid-cols-4'}">
    <div class="surface-stat px-3 py-2">
      <span class="text-kicker !text-text-disabled">Installed</span>
      <div class="text-lg font-semibold text-text-primary leading-tight">
        {visibleScrapers.length + visibleInstalledPlugins.length + visibleInstalledProviders.length}
      </div>
    </div>
    {#if !isSfw}
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">{entityTerms.video} Scrapers</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{videoCount}</div>
      </div>
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">{entityTerms.performers} Scrapers</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{performerCount}</div>
      </div>
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">StashBox</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">{stashBoxEndpoints.length}</div>
      </div>
    {:else}
      <div class="surface-stat px-3 py-2">
        <span class="text-kicker !text-text-disabled">Prismedia Plugins</span>
        <div class="text-lg font-semibold text-text-primary leading-tight">
          {visibleProviderPlugins.length}
        </div>
      </div>
    {/if}
  </div>

  <!-- Messages -->
  {#if error}
    <div class="surface-well border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text flex items-center gap-2">
      <span class="flex-1">{error}</span>
      <button
        onclick={() => (error = null)}
        aria-label="Dismiss error"
        class="text-text-disabled hover:text-text-muted"
      >
        <X class="h-3 w-3" />
      </button>
    </div>
  {/if}
  {#if message && !error}
    <div class="surface-well border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text">
      {message}
    </div>
  {/if}

  {#if loading}
    <div class="flex items-center justify-center py-20">
      <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
    </div>
  {:else}
    <!-- Tabs -->
    <div class="flex items-center gap-1 overflow-x-auto scrollbar-hidden">
      {#each visibleTabs as t (t.key)}
        {@const Icon = tabIcon(t.key)}
        <button
          onclick={() => (tab = t.key)}
          class={"flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-sm transition-all duration-fast whitespace-nowrap " +
            (tab === t.key
              ? "bg-accent-950 text-text-accent border border-border-accent shadow-[var(--shadow-glow-accent)]"
              : "text-text-muted border border-transparent hover:text-text-secondary hover:bg-surface-3/40")}
        >
          <Icon class="h-3.5 w-3.5" />
          {t.label}
          {#if t.nsfw}
            <span
              class="tag-chip text-[0.5rem] bg-status-error/10 text-status-error-text border border-status-error/20 px-1 py-0"
            >
              NSFW
            </span>
          {/if}
          {#if t.count != null && t.count > 0}
            <span class="text-mono-sm text-text-disabled ml-1">{t.count}</span>
          {/if}
        </button>
      {/each}
    </div>

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
                  <div class="border-t border-border-subtle px-4 py-3 space-y-3 bg-surface-1/50">
                    <h4 class="text-[0.72rem] font-medium text-text-secondary">Authentication</h4>
                    {#each plugin.auth as field (field.key)}
                      <div>
                        <div class="flex items-center justify-between mb-1">
                          <label class="text-[0.65rem] text-text-disabled" for="plugin-auth-{plugin.id}-{field.key}">
                            {field.label}
                            {#if field.required}
                              <span class="text-status-error-text ml-0.5">*</span>
                            {/if}
                          </label>
                          {#if field.url}
                            <button
                              type="button"
                              onclick={() => window.open(field.url ?? "", "_blank", "noopener,noreferrer")}
                              class="text-[0.6rem] text-text-accent hover:underline"
                            >
                              {authLinkLabel(field)}
                            </button>
                          {/if}
                        </div>
                        <input
                          id="plugin-auth-{plugin.id}-{field.key}"
                          type="password"
                          value={authValues[`prismedia:${plugin.id}:${field.key}`] ?? ""}
                          oninput={(e) => {
                            authValues = {
                              ...authValues,
                              [`prismedia:${plugin.id}:${field.key}`]: (e.currentTarget as HTMLInputElement).value,
                            };
                          }}
                          placeholder={plugin.missingAuthKeys.includes(field.key) ? "Required" : "Saved - enter a new value to replace"}
                          class="control-input py-1.5 font-mono"
                        />
                      </div>
                    {/each}
                    <div class="flex items-center justify-end gap-2 pt-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onclick={() => {
                          authExpandedFor = null;
                          authValues = {};
                        }}
                        class="h-auto px-3 py-1.5 text-[0.72rem]"
                      >
                        {#snippet children()}Cancel{/snippet}
                      </Button>
                      <Button
                        type="button"
                        variant="primary"
                        size="sm"
                        disabled={authSavingFor === `prismedia:${plugin.id}` ||
                          !plugin.auth.some((field) => authValues[`prismedia:${plugin.id}:${field.key}`]?.trim())}
                        onclick={() => void handleProviderSaveAuth(plugin)}
                        class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
                      >
                        {#snippet children()}
                          {#if authSavingFor === `prismedia:${plugin.id}`}
                            <Loader2 class="h-3 w-3 animate-spin" />
                          {:else}
                            <Save class="h-3 w-3" />
                          {/if}
                          Save Credentials
                        {/snippet}
                      </Button>
                    </div>
                  </div>
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
                  <div class="border-t border-border-subtle px-4 py-3 space-y-3 bg-surface-1/50">
                    <h4 class="text-[0.72rem] font-medium text-text-secondary">Authentication</h4>
                    {#each plugin.authFields as field (field.key)}
                      <div>
                        <div class="flex items-center justify-between mb-1">
                          <label class="text-[0.65rem] text-text-disabled" for="auth-{plugin.id}-{field.key}">
                            {field.label}
                            {#if field.required}
                              <span class="text-status-error-text ml-0.5">*</span>
                            {/if}
                          </label>
                          {#if field.url}
                            <button
                              type="button"
                              onclick={() => window.open(field.url ?? "", "_blank", "noopener,noreferrer")}
                              class="text-[0.6rem] text-text-accent hover:underline"
                            >
                              {authLinkLabel(field)}
                            </button>
                          {/if}
                        </div>
                        <input
                          id="auth-{plugin.id}-{field.key}"
                          type="password"
                          value={authValues[field.key] ?? ""}
                          oninput={(e) => {
                            authValues = {
                              ...authValues,
                              [field.key]: (e.currentTarget as HTMLInputElement).value,
                            };
                          }}
                          placeholder={authPlaceholder(plugin, field)}
                          class="control-input py-1.5 font-mono"
                        />
                      </div>
                    {/each}
                    <div class="flex items-center justify-end gap-2 pt-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onclick={() => {
                          authExpandedFor = null;
                          authValues = {};
                        }}
                        class="h-auto px-3 py-1.5 text-[0.72rem]"
                      >
                        {#snippet children()}Cancel{/snippet}
                      </Button>
                      <Button
                        type="button"
                        variant="primary"
                        size="sm"
                        disabled={authSavingFor === plugin.id ||
                          !plugin.authFields.some((f) => authValues[f.key]?.trim())}
                        onclick={() => void handleInstalledPluginSaveAuth(plugin)}
                        class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
                      >
                        {#snippet children()}
                          {#if authSavingFor === plugin.id}
                            <Loader2 class="h-3 w-3 animate-spin" />
                          {:else}
                            <Save class="h-3 w-3" />
                          {/if}
                          Save Credentials
                        {/snippet}
                      </Button>
                    </div>
                  </div>
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
      <section class="space-y-3">
        <div class="flex items-center justify-between gap-3 flex-wrap">
          <p class="text-text-muted text-[0.72rem]">
            {visibleProviderPlugins.length} plugins available
          </p>
          <div class="flex items-center gap-2">
            <div class="relative">
              <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
              <input
                class="control-input pl-8 w-64 py-1.5 text-sm"
                placeholder="Filter by name or ID..."
                bind:value={prismediaSearch}
              />
              {#if prismediaSearch}
                <button
                  onclick={() => (prismediaSearch = "")}
                  aria-label="Clear search"
                  class="absolute right-2 top-1/2 -translate-y-1/2 text-text-disabled hover:text-text-muted"
                >
                  <X class="h-3 w-3" />
                </button>
              {/if}
            </div>
            <Button variant="secondary" size="sm" onclick={() => void loadPrismediaIndex()} disabled={prismediaLoading}>
              {#snippet children()}
                {#if prismediaLoading}
                  <Loader2 class="h-3.5 w-3.5 animate-spin" />
                {:else}
                  <RefreshCw class="h-3.5 w-3.5" />
                {/if}
                Refresh
              {/snippet}
            </Button>
          </div>
        </div>

        {#if prismediaLoading && !prismediaLoaded}
          <div class="surface-card no-lift p-12 flex items-center justify-center">
            <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
          </div>
        {:else if filteredPlugins.length === 0}
          <div class="surface-card no-lift p-8 text-center">
            <Sparkles class="h-8 w-8 text-text-disabled mx-auto mb-3" />
            <p class="text-text-muted text-sm">
              {prismediaSearch
                ? "No plugins match your search."
                : prismediaLoaded
                  ? "No plugins available."
                  : "Loading plugin index..."}
            </p>
          </div>
        {:else}
          <div class="space-y-1">
            {#each filteredPlugins as plugin (plugin.id)}
              {@const authExpanded = authExpandedFor === `prismedia:${plugin.id}`}
              {@const hasAuth = plugin.auth.length > 0}
              <div class="surface-card no-lift px-4 py-3 flex items-center gap-3">
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-2 flex-wrap">
                    <p class="text-sm font-medium">{plugin.name}</p>
                    <span class="text-mono-sm text-text-disabled">v{plugin.version}</span>
                    {#if plugin.installed}
                      <Badge variant={plugin.enabled ? "accent" : "default"}>
                        {#snippet children()}{plugin.enabled ? "Installed" : "Disabled"}{/snippet}
                      </Badge>
                    {/if}
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
                  <p class="text-text-disabled text-[0.65rem] mt-0.5 font-mono">
                    {plugin.id} · dotnet-process
                  </p>
                  <div class="flex flex-wrap items-center gap-1.5 mt-1.5">
                    {#each providerSupportLabels(plugin) as label (label)}
                      <span class="tag-chip-default text-[0.55rem] px-1.5 py-0.5">
                        {label}
                      </span>
                    {/each}
                  </div>
                </div>
                <div class="flex items-center gap-2 shrink-0">
                  {#if hasAuth && plugin.installed}
                    <button
                      onclick={() => toggleProviderAuthExpanded(plugin.id)}
                      class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                        (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                    >
                      <KeyRound class="h-3.5 w-3.5" />
                      {authExpanded ? "Close" : "Configure"}
                    </button>
                  {/if}
                  {#if !plugin.installed || !plugin.enabled}
                    <button
                      onclick={() => void handleProviderInstall(plugin)}
                      disabled={providerInstallingId === plugin.id}
                      class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-text-accent transition-colors duration-fast shrink-0 disabled:opacity-40"
                    >
                      {#if providerInstallingId === plugin.id}
                        <Loader2 class="h-3.5 w-3.5 animate-spin" />
                      {:else}
                        <Download class="h-3.5 w-3.5" />
                      {/if}
                      Install
                    </button>
                  {/if}
                </div>
              </div>

              {#if authExpanded}
                <div class="surface-card no-lift border-t border-border-subtle px-4 py-3 space-y-3 bg-surface-1/50">
                  <h4 class="text-[0.72rem] font-medium text-text-secondary">Authentication</h4>
                  {#each plugin.auth as field (field.key)}
                    <div>
                      <div class="flex items-center justify-between mb-1">
                        <label class="text-[0.65rem] text-text-disabled" for="community-plugin-auth-{plugin.id}-{field.key}">
                          {field.label}
                          {#if field.required}
                            <span class="text-status-error-text ml-0.5">*</span>
                          {/if}
                        </label>
                        {#if field.url}
                          <button
                            type="button"
                            onclick={() => window.open(field.url ?? "", "_blank", "noopener,noreferrer")}
                            class="text-[0.6rem] text-text-accent hover:underline"
                          >
                            {authLinkLabel(field)}
                          </button>
                        {/if}
                      </div>
                      <input
                        id="community-plugin-auth-{plugin.id}-{field.key}"
                        type="password"
                        value={authValues[`prismedia:${plugin.id}:${field.key}`] ?? ""}
                        oninput={(e) => {
                          authValues = {
                            ...authValues,
                            [`prismedia:${plugin.id}:${field.key}`]: (e.currentTarget as HTMLInputElement).value,
                          };
                        }}
                        placeholder={plugin.missingAuthKeys.includes(field.key) ? "Required" : "Saved - enter a new value to replace"}
                        class="control-input py-1.5 font-mono"
                      />
                    </div>
                  {/each}
                  <div class="flex items-center justify-end gap-2 pt-1">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onclick={() => {
                        authExpandedFor = null;
                        authValues = {};
                      }}
                      class="h-auto px-3 py-1.5 text-[0.72rem]"
                    >
                      {#snippet children()}Cancel{/snippet}
                    </Button>
                    <Button
                      type="button"
                      variant="primary"
                      size="sm"
                      disabled={authSavingFor === `prismedia:${plugin.id}` ||
                        !plugin.auth.some((field) => authValues[`prismedia:${plugin.id}:${field.key}`]?.trim())}
                      onclick={() => void handleProviderSaveAuth(plugin)}
                      class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
                    >
                      {#snippet children()}
                        {#if authSavingFor === `prismedia:${plugin.id}`}
                          <Loader2 class="h-3 w-3 animate-spin" />
                        {:else}
                          <Save class="h-3 w-3" />
                        {/if}
                        Save Credentials
                      {/snippet}
                    </Button>
                  </div>
                </div>
              {/if}
            {/each}
          </div>
        {/if}
      </section>
    {/if}
    <!-- STASH COMMUNITY INDEX TAB -->
    {#if tab === "stash-index" && !isSfw}
      <section class="space-y-3">
        <div class="flex items-center justify-between gap-3 flex-wrap">
          <p class="text-text-muted text-[0.72rem]">
            {indexEntries.length} scrapers available · All Stash community scrapers are classified as NSFW
          </p>
          <div class="flex items-center gap-2">
            <div class="relative">
              <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
              <input
                class="control-input pl-8 w-64 py-1.5 text-sm"
                placeholder="Filter by name or ID..."
                bind:value={indexSearch}
              />
              {#if indexSearch}
                <button
                  onclick={() => (indexSearch = "")}
                  aria-label="Clear search"
                  class="absolute right-2 top-1/2 -translate-y-1/2 text-text-disabled hover:text-text-muted"
                >
                  <X class="h-3 w-3" />
                </button>
              {/if}
            </div>
            <Button variant="secondary" size="sm" onclick={() => void loadStashIndex(true)} disabled={indexLoading}>
              {#snippet children()}
                {#if indexLoading}
                  <Loader2 class="h-3.5 w-3.5 animate-spin" />
                {:else}
                  <RefreshCw class="h-3.5 w-3.5" />
                {/if}
                Refresh
              {/snippet}
            </Button>
          </div>
        </div>

        {#if indexLoading && !indexLoaded}
          <div class="surface-card no-lift p-12 flex items-center justify-center">
            <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
          </div>
        {:else}
          <div class="space-y-1 max-h-[600px] overflow-y-auto scrollbar-hidden">
            {#each filteredIndex as entry (entry.id)}
              <div class="surface-card no-lift px-4 py-3 flex items-center gap-3">
                <div class="min-w-0 flex-1">
                  <p class="text-sm font-medium">{entry.name}</p>
                  <p class="text-text-disabled text-[0.65rem] mt-0.5 font-mono">
                    {entry.id}
                    <span class="text-text-disabled/60 ml-2">{entry.date}</span>
                    {#if entry.requires?.length}
                      <span class="text-text-disabled/60 ml-2">requires: {entry.requires.join(", ")}</span>
                    {/if}
                  </p>
                </div>
                {#if entry.installed}
                  <Badge variant="accent">
                    {#snippet children()}<Check class="h-2.5 w-2.5 mr-1" />Installed{/snippet}
                  </Badge>
                {:else}
                  <button
                    onclick={() => void handleScraperInstall(entry.id)}
                    disabled={installingId === entry.id}
                    class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-text-accent transition-colors duration-fast shrink-0 disabled:opacity-40"
                  >
                    {#if installingId === entry.id}
                      <Loader2 class="h-3.5 w-3.5 animate-spin" />
                    {:else}
                      <Download class="h-3.5 w-3.5" />
                    {/if}
                    Install
                  </button>
                {/if}
              </div>
            {/each}
            {#if filteredIndex.length === 0}
              <div class="surface-card no-lift p-8 text-center">
                <p class="text-text-muted text-sm">
                  {indexSearch ? "No scrapers match your search." : "Index is empty."}
                </p>
              </div>
            {/if}
          </div>
        {/if}
      </section>
    {/if}

    <!-- STASHBOX ENDPOINTS TAB -->
    {#if tab === "stashbox" && !isSfw}
      <section class="space-y-2">
        <div class="flex items-center justify-between px-1">
          <p class="text-text-muted text-[0.72rem]">
            Connect to StashDB, ThePornDB, FansDB, and other Stash-Box protocol servers
          </p>
          <Button
            variant="ghost"
            size="sm"
            onclick={openAddStashBox}
            class="h-auto gap-1 px-2 py-1 text-[0.68rem] text-text-accent hover:bg-accent-950/60"
          >
            {#snippet children()}<Plus class="h-3 w-3" />Add Endpoint{/snippet}
          </Button>
        </div>

        {#if stashBoxEndpoints.length === 0 && !showStashBoxForm}
          <div class="empty-rack-slot p-6 text-center">
            <Plug class="h-8 w-8 text-text-disabled mx-auto mb-3" />
            <p class="text-[0.75rem] text-text-disabled">
              No endpoints configured. Add one to enable fingerprint-based identification.
            </p>
          </div>
        {/if}

        {#each stashBoxEndpoints as ep (ep.id)}
          {@const tr = sbTestResult}
          <div class="surface-card no-lift p-3.5">
            <div class="flex items-center justify-between gap-3">
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-2 flex-wrap">
                  <span class="text-[0.82rem] font-medium truncate">{ep.name}</span>
                  <span class="tag-chip text-[0.55rem] bg-status-error/10 text-status-error-text border border-status-error/20">NSFW</span>
                  {#if !ep.enabled}
                    <Badge>
                      {#snippet children()}Disabled{/snippet}
                    </Badge>
                  {/if}
                  {#if tr && tr.id === ep.id}
                    <Badge variant={tr.valid ? "success" : "error"}>
                      {#snippet children()}
                        {#if tr.valid}
                          <Check class="h-2.5 w-2.5" />Connected
                        {:else}
                          <AlertCircle class="h-2.5 w-2.5" />{tr.error ?? "Failed"}
                        {/if}
                      {/snippet}
                    </Badge>
                  {/if}
                </div>
                <p class="text-[0.65rem] text-text-disabled truncate mt-0.5">
                  {ep.endpoint} · Key: {ep.apiKeyPreview}
                </p>
              </div>
              <div class="flex items-center gap-1 shrink-0">
                <button
                  onclick={() => void testEndpoint(ep)}
                  disabled={sbTesting === ep.id}
                  aria-label="Test connection"
                  class="p-1.5 rounded-xs text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
                >
                  {#if sbTesting === ep.id}
                    <Loader2 class="h-3.5 w-3.5 animate-spin text-accent-400" />
                  {:else}
                    <RefreshCw class="h-3.5 w-3.5" />
                  {/if}
                </button>
                <button
                  onclick={() => openEditStashBox(ep)}
                  aria-label="Edit"
                  class="p-1.5 rounded-xs text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
                >
                  <Pencil class="h-3.5 w-3.5" />
                </button>
                <button
                  onclick={() => void toggleEndpointEnabled(ep)}
                  aria-label={ep.enabled ? "Disable" : "Enable"}
                  class="p-1.5 rounded-xs text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
                >
                  {#if ep.enabled}
                    <ToggleRight class="h-3.5 w-3.5 text-text-accent" />
                  {:else}
                    <ToggleLeft class="h-3.5 w-3.5" />
                  {/if}
                </button>
                <button
                  onclick={() => void deleteEndpoint(ep)}
                  aria-label="Remove"
                  class="p-1.5 rounded-xs text-text-muted transition-colors hover:bg-status-error/10 hover:text-status-error-text"
                >
                  <Trash2 class="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          </div>
        {/each}

        {#if showStashBoxForm}
          <div class="surface-well space-y-3 border border-border-accent/30 p-4">
            <div class="flex items-center justify-between">
              <h4 class="text-[0.78rem] font-medium">
                {editingStashBox ? "Edit Endpoint" : "Add Stash-Box Endpoint"}
              </h4>
              <button
                onclick={() => (showStashBoxForm = false)}
                aria-label="Close"
                class="p-1 text-text-disabled transition-colors hover:text-text-muted"
              >
                <X class="h-3.5 w-3.5" />
              </button>
            </div>
            <div class="grid gap-2.5">
              <div>
                <label for="sb-name" class="text-[0.65rem] text-text-disabled block mb-1">Name</label>
                <input
                  id="sb-name"
                  type="text"
                  bind:value={sbName}
                  placeholder="StashDB"
                  class="control-input py-1.5"
                />
              </div>
              <div>
                <label for="sb-endpoint" class="text-[0.65rem] text-text-disabled block mb-1">GraphQL Endpoint</label>
                <input
                  id="sb-endpoint"
                  type="text"
                  bind:value={sbEndpoint}
                  placeholder="https://stashdb.org/graphql"
                  class="control-input py-1.5"
                />
                <div class="flex gap-1.5 mt-1.5 flex-wrap">
                  {#each [{ label: "StashDB", url: "https://stashdb.org/graphql" }, { label: "FansDB", url: "https://fansdb.cc/graphql" }, { label: "PMVStash", url: "https://pmvstash.org/graphql" }, { label: "ThePornDB", url: "https://theporndb.net/graphql" }] as preset (preset.url)}
                    <button
                      onclick={() => {
                        sbEndpoint = preset.url;
                        if (!sbName) sbName = preset.label;
                      }}
                      class="border border-border-subtle rounded-xs px-1.5 py-0.5 text-[0.6rem] text-text-disabled transition-colors hover:border-border-default hover:text-text-muted"
                    >
                      {preset.label}
                    </button>
                  {/each}
                </div>
              </div>
              <div>
                <label for="sb-apikey" class="text-[0.65rem] text-text-disabled block mb-1">
                  API Key
                  {#if editingStashBox}
                    <span class="text-text-disabled">(leave blank to keep current)</span>
                  {/if}
                </label>
                <input
                  id="sb-apikey"
                  type="password"
                  bind:value={sbApiKey}
                  placeholder={editingStashBox ? "••••••••" : "Paste your API key"}
                  class="control-input py-1.5 font-mono"
                />
              </div>
            </div>
            <div class="flex items-center justify-end gap-2 pt-1">
              <Button variant="ghost" size="sm" onclick={() => (showStashBoxForm = false)} class="h-auto px-3 py-1.5 text-[0.72rem]">
                {#snippet children()}Cancel{/snippet}
              </Button>
              <Button
                variant="primary"
                size="sm"
                disabled={sbSaving || !sbName || !sbEndpoint}
                onclick={() => void saveStashBox()}
                class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
              >
                {#snippet children()}
                  {#if sbSaving}
                    <Loader2 class="h-3 w-3 animate-spin" />
                  {:else}
                    <Save class="h-3 w-3" />
                  {/if}
                  {editingStashBox ? "Update" : "Save"}
                {/snippet}
              </Button>
            </div>
          </div>
        {/if}
      </section>
    {/if}
  {/if}
</div>
