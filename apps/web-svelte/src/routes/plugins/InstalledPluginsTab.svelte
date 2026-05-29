<script lang="ts">
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
  import { entityTerms } from "$lib/terminology";
  import type {
    InstalledPlugin,
    PluginUpdateStatus,
    ScraperPackage,
  } from "$lib/api/plugins";
  import PluginCredentialForm from "./PluginCredentialForm.svelte";
  import { authPlaceholder } from "./plugin-auth-format";
  import type { PluginProviderSummary } from "./plugin-page-types";

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

  interface Props {
    authExpandedFor: string | null;
    authSavingFor: string | null;
    authValues: Record<string, string>;
    checkingUpdates: boolean;
    installedPlugins: InstalledPlugin[];
    isSfw: boolean;
    onAuthCancel: () => void;
    onCheckUpdates: () => void;
    onInstalledPluginAuthToggle: (pluginId: string) => void;
    onInstalledPluginRemove: (plugin: InstalledPlugin) => void;
    onInstalledPluginSaveAuth: (plugin: InstalledPlugin) => void;
    onInstalledPluginToggle: (plugin: InstalledPlugin) => void;
    onInstalledPluginUpdate: (plugin: InstalledPlugin) => void;
    onProviderAuthToggle: (pluginId: string) => void;
    onProviderInstall: (plugin: PluginProviderSummary) => void;
    onProviderRemove: (plugin: PluginProviderSummary) => void;
    onProviderSaveAuth: (plugin: PluginProviderSummary) => void;
    onProviderUpdate: (plugin: PluginProviderSummary) => void;
    onScraperRemove: (pkg: ScraperPackage) => void;
    onScraperToggle: (pkg: ScraperPackage) => void;
    pluginUpdates: Record<string, PluginUpdateStatus>;
    providerInstallingId: string | null;
    providerRemovingId: string | null;
    providerUpdatingId: string | null;
    providers: PluginProviderSummary[];
    scrapers: ScraperPackage[];
    updatingPluginId: string | null;
  }

  let {
    authExpandedFor,
    authSavingFor,
    authValues = $bindable(),
    checkingUpdates,
    installedPlugins,
    isSfw,
    onAuthCancel,
    onCheckUpdates,
    onInstalledPluginAuthToggle,
    onInstalledPluginRemove,
    onInstalledPluginSaveAuth,
    onInstalledPluginToggle,
    onInstalledPluginUpdate,
    onProviderAuthToggle,
    onProviderInstall,
    onProviderRemove,
    onProviderSaveAuth,
    onProviderUpdate,
    onScraperRemove,
    onScraperToggle,
    pluginUpdates,
    providerInstallingId,
    providerRemovingId,
    providerUpdatingId,
    providers,
    scrapers,
    updatingPluginId,
  }: Props = $props();

  let search = $state("");
  let capFilter = $state<CapFilter>("all");

  const filteredScrapers = $derived.by(() => {
    const q = search.trim().toLowerCase();
    return scrapers.filter((pkg) => {
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
    const q = search.trim().toLowerCase();
    return providers.filter((plugin) => {
      if (q && !plugin.name.toLowerCase().includes(q) && !plugin.id.toLowerCase().includes(q)) {
        return false;
      }

      return matchesProviderCapabilityFilter(plugin);
    });
  });

  const shownCount = $derived(filteredProviders.length + installedPlugins.length + filteredScrapers.length);

  function enabledCaps(caps: Record<string, boolean> | null | undefined): string[] {
    if (!caps) return [];
    return Object.entries(caps)
      .filter(([, v]) => v)
      .map(([k]) => k);
  }

  function matchesProviderCapabilityFilter(plugin: PluginProviderSummary) {
    if (capFilter === "all") return true;
    if (capFilter === "scene") {
      return plugin.supports.some((support) =>
        support.entityKind === "video" || support.entityKind === "video-series",
      );
    }

    return false;
  }

  function providerSupportLabels(plugin: PluginProviderSummary): string[] {
    return plugin.supports.map((support) =>
      `${support.entityKind}: ${support.actions.join(", ")}`,
    );
  }
</script>

<section class="space-y-2">
  <div class="surface-well flex items-center gap-2 px-3 py-2 flex-wrap">
    <div class="relative">
      <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
      <input
        class="control-input pl-8 w-56 py-1.5 text-sm"
        placeholder="Search installed..."
        bind:value={search}
      />
      {#if search}
        <button
          onclick={() => (search = "")}
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
    {#if installedPlugins.length > 0 || providers.some((plugin) => plugin.installed)}
      <Button
        variant="ghost"
        size="sm"
        onclick={onCheckUpdates}
        disabled={checkingUpdates}
        class="h-auto gap-1.5 px-2.5 py-1.5 text-xs"
      >
        {#if checkingUpdates}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <RefreshCw class="h-3.5 w-3.5" />
        {/if}
        Check for updates
      </Button>
    {/if}
    <span class="text-mono-sm text-text-disabled">{shownCount} shown</span>
  </div>

  {#if filteredProviders.length === 0 && installedPlugins.length === 0 && filteredScrapers.length === 0}
    <div class="surface-card no-lift p-8 text-center">
      <Package class="h-8 w-8 text-text-disabled mx-auto mb-3" />
      <p class="text-text-muted text-sm">
        {#if scrapers.length === 0 && installedPlugins.length === 0 && providers.length === 0}
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
                    {plugin.installed && plugin.enabled ? "Installed" : "Available"}
                  </Badge>
                  {#if plugin.updateAvailable}
                    <Badge variant="success">
                      <Sparkles class="h-2.5 w-2.5" />v{plugin.availableVersion} available
                    </Badge>
                  {/if}
                  {#if plugin.missingAuthKeys.length === 0 && hasAuth}
                    <Badge variant="success">
                      <Check class="h-2.5 w-2.5" />Auth OK
                    </Badge>
                  {:else if plugin.missingAuthKeys.length > 0}
                    <Badge variant="warning">
                      <AlertCircle class="h-2.5 w-2.5" />Auth Required
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
                {#if plugin.installed && plugin.updateAvailable}
                  <button
                    onclick={() => onProviderUpdate(plugin)}
                    disabled={providerUpdatingId === plugin.id}
                    class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-status-success-text hover:text-text-primary transition-colors duration-fast disabled:opacity-40"
                  >
                    {#if providerUpdatingId === plugin.id}
                      <Loader2 class="h-3.5 w-3.5 animate-spin" />
                    {:else}
                      <Download class="h-3.5 w-3.5" />
                    {/if}
                    Update
                  </button>
                {/if}
                {#if !plugin.installed || !plugin.enabled}
                  <button
                    onclick={() => onProviderInstall(plugin)}
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
                    onclick={() => onProviderAuthToggle(plugin.id)}
                    class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                      (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                  >
                    <KeyRound class="h-3.5 w-3.5" />
                    {authExpanded ? "Close" : "Configure"}
                  </button>
                {/if}
                {#if plugin.installed}
                  <button
                    onclick={() => onProviderRemove(plugin)}
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
              fields={plugin.auth}
              getPlaceholder={(field) =>
                plugin.missingAuthKeys.includes(field.key)
                  ? "Required"
                  : "Saved - enter a new value to replace"}
              getValueKey={(field) => `prismedia:${plugin.id}:${field.key}`}
              inputIdPrefix={`plugin-auth-${plugin.id}`}
              onCancel={onAuthCancel}
              onSave={() => onProviderSaveAuth(plugin)}
              saving={authSavingFor === `prismedia:${plugin.id}`}
              bind:values={authValues}
            />
          {/if}
        </div>
      {/each}
      {#each installedPlugins as plugin (plugin.id)}
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
                    {plugin.enabled ? "Enabled" : "Disabled"}
                  </Badge>
                  {#if update?.updateAvailable}
                    <Badge variant="success">
                      <Sparkles class="h-2.5 w-2.5" />Update available
                    </Badge>
                  {/if}
                  {#if hasAuth}
                    {#if plugin.authStatus === "ok"}
                      <Badge variant="success">
                        <Check class="h-2.5 w-2.5" />Auth OK
                      </Badge>
                    {:else}
                      <Badge variant="warning">
                        <AlertCircle class="h-2.5 w-2.5" />Auth Required
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
                    onclick={() => onInstalledPluginUpdate(plugin)}
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
                    onclick={() => onInstalledPluginAuthToggle(plugin.id)}
                    class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                      (plugin.authStatus === "missing" ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                  >
                    <KeyRound class="h-3.5 w-3.5" />
                    {authExpanded ? "Close" : "Configure"}
                  </button>
                {/if}
                <button
                  onclick={() => onInstalledPluginToggle(plugin)}
                  class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast text-text-muted hover:text-text-primary"
                >
                  {#if plugin.enabled}
                    <ToggleRight class="h-4 w-4 text-text-accent" />Disable
                  {:else}
                    <ToggleLeft class="h-4 w-4" />Enable
                  {/if}
                </button>
                <button
                  onclick={() => onInstalledPluginRemove(plugin)}
                  class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-status-error-text transition-colors duration-fast"
                >
                  <Trash2 class="h-3.5 w-3.5" />Remove
                </button>
              </div>
            </div>
          </div>

          {#if authExpanded && plugin.authFields}
            <PluginCredentialForm
              fields={plugin.authFields}
              getPlaceholder={(field) => authPlaceholder(plugin, field)}
              getValueKey={(field) => field.key}
              inputIdPrefix={`auth-${plugin.id}`}
              onCancel={onAuthCancel}
              onSave={() => onInstalledPluginSaveAuth(plugin)}
              saving={authSavingFor === plugin.id}
              bind:values={authValues}
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
                  {pkg.enabled ? "Enabled" : "Disabled"}
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
                onclick={() => onScraperToggle(pkg)}
                class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast text-text-muted hover:text-text-primary"
              >
                {#if pkg.enabled}
                  <ToggleRight class="h-4 w-4 text-text-accent" />Disable
                {:else}
                  <ToggleLeft class="h-4 w-4" />Enable
                {/if}
              </button>
              <button
                onclick={() => onScraperRemove(pkg)}
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
