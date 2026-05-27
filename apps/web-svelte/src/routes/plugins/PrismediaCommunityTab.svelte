<script lang="ts">
  import {
    AlertCircle,
    Check,
    Download,
    KeyRound,
    Loader2,
    RefreshCw,
    Save,
    Search,
    Sparkles,
    X,
  } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import { authLinkLabel } from "./plugin-auth-format";
  import type { PluginProviderSummary } from "./plugin-page-types";

  interface Props {
    authSavingFor: string | null;
    installingId: string | null;
    loaded: boolean;
    loading: boolean;
    onInstall: (plugin: PluginProviderSummary) => void;
    onRefresh: () => void;
    onSaveAuth: (plugin: PluginProviderSummary, values: Record<string, string | null>) => void;
    plugins: PluginProviderSummary[];
  }

  let {
    authSavingFor,
    installingId,
    loaded,
    loading,
    onInstall,
    onRefresh,
    onSaveAuth,
    plugins,
  }: Props = $props();

  let search = $state("");
  let authExpandedFor = $state<string | null>(null);
  let authValues = $state<Record<string, string>>({});

  const filteredPlugins = $derived.by(() => {
    const q = search.trim().toLowerCase();
    if (!q) return plugins;
    return plugins.filter((plugin) =>
      plugin.name.toLowerCase().includes(q) || plugin.id.toLowerCase().includes(q),
    );
  });

  function providerSupportLabels(plugin: PluginProviderSummary): string[] {
    return plugin.supports.map((support) =>
      `${support.entityKind}: ${support.actions.join(", ")}`,
    );
  }

  function toggleAuthExpanded(pluginId: string) {
    if (authExpandedFor === pluginId) {
      authExpandedFor = null;
      authValues = {};
    } else {
      authExpandedFor = pluginId;
      authValues = {};
    }
  }

  function updateAuthValue(key: string, value: string) {
    authValues = {
      ...authValues,
      [key]: value,
    };
  }

  function saveAuth(plugin: PluginProviderSummary) {
    const values: Record<string, string | null> = {};
    for (const field of plugin.auth) {
      const value = authValues[field.key]?.trim();
      if (value) values[field.key] = value;
    }
    onSaveAuth(plugin, values);
    authExpandedFor = null;
    authValues = {};
  }
</script>

<section class="space-y-3">
  <div class="flex items-center justify-between gap-3 flex-wrap">
    <p class="text-text-muted text-[0.72rem]">
      {plugins.length} plugins available
    </p>
    <div class="flex items-center gap-2">
      <div class="relative">
        <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
        <input
          class="control-input pl-8 w-64 py-1.5 text-sm"
          placeholder="Filter by name or ID..."
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
      <Button variant="secondary" size="sm" onclick={onRefresh} disabled={loading}>
        {#if loading}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <RefreshCw class="h-3.5 w-3.5" />
        {/if}
        Refresh
      </Button>
    </div>
  </div>

  {#if loading && !loaded}
    <div class="surface-card no-lift p-12 flex items-center justify-center">
      <Loader2 class="h-6 w-6 animate-spin text-text-muted" />
    </div>
  {:else if filteredPlugins.length === 0}
    <div class="surface-card no-lift p-8 text-center">
      <Sparkles class="h-8 w-8 text-text-disabled mx-auto mb-3" />
      <p class="text-text-muted text-sm">
        {search
          ? "No plugins match your search."
          : loaded
            ? "No plugins available."
            : "Loading plugin index..."}
      </p>
    </div>
  {:else}
    <div class="space-y-1">
      {#each filteredPlugins as plugin (plugin.id)}
        {@const authExpanded = authExpandedFor === plugin.id}
        {@const hasAuth = plugin.auth.length > 0}
        <div class="surface-card no-lift px-4 py-3 flex items-center gap-3">
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2 flex-wrap">
              <p class="text-sm font-medium">{plugin.name}</p>
              <span class="text-mono-sm text-text-disabled">v{plugin.version}</span>
              {#if plugin.installed}
                <Badge variant={plugin.enabled ? "accent" : "default"}>
                  {plugin.enabled ? "Installed" : "Disabled"}
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
                onclick={() => toggleAuthExpanded(plugin.id)}
                class={"flex items-center gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast " +
                  (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
              >
                <KeyRound class="h-3.5 w-3.5" />
                {authExpanded ? "Close" : "Configure"}
              </button>
            {/if}
            {#if !plugin.installed || !plugin.enabled}
              <button
                onclick={() => onInstall(plugin)}
                disabled={installingId === plugin.id}
                class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted hover:text-text-accent transition-colors duration-fast shrink-0 disabled:opacity-40"
              >
                {#if installingId === plugin.id}
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
                  value={authValues[field.key] ?? ""}
                  oninput={(event) => updateAuthValue(field.key, event.currentTarget.value)}
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
                Cancel
              </Button>
              <Button
                type="button"
                variant="primary"
                size="sm"
                disabled={authSavingFor === `prismedia:${plugin.id}` ||
                  !plugin.auth.some((field) => authValues[field.key]?.trim())}
                onclick={() => saveAuth(plugin)}
                class="h-auto gap-1.5 px-3 py-1.5 text-[0.72rem]"
              >
                {#if authSavingFor === `prismedia:${plugin.id}`}
                  <Loader2 class="h-3 w-3 animate-spin" />
                {:else}
                  <Save class="h-3 w-3" />
                {/if}
                Save Credentials
              </Button>
            </div>
          </div>
        {/if}
      {/each}
    </div>
  {/if}
</section>
