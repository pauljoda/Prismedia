<script lang="ts">
  import {
    AlertCircle,
    Check,
    Download,
    KeyRound,
    Loader2,
    RefreshCw,
    Search,
    Sparkles,
    X,
  } from "@lucide/svelte";
  import { Badge, Button, TextInput } from "@prismedia/ui-svelte";
  import type { PluginProvider } from "$lib/api/generated/model";
  import PluginCredentialForm from "./PluginCredentialForm.svelte";

  interface Props {
    authSavingFor: string | null;
    installingId: string | null;
    loaded: boolean;
    loading: boolean;
    onInstall: (plugin: PluginProvider) => void;
    onRefresh: () => void;
    onSaveAuth: (plugin: PluginProvider, values: Record<string, string | null>) => void;
    plugins: PluginProvider[];
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

  function providerSupportLabels(plugin: PluginProvider): string[] {
    return plugin.supports.map((support) =>
      `${support.entityKind}: ${support.actions.join(", ")}`,
    );
  }

  function toggleAuthExpanded(pluginId: string) {
    if (authExpandedFor === pluginId) {
      closeAuthForm();
    } else {
      authExpandedFor = pluginId;
      authValues = {};
    }
  }

  function closeAuthForm() {
    authExpandedFor = null;
    authValues = {};
  }

  function saveAuth(plugin: PluginProvider) {
    const values: Record<string, string | null> = {};
    for (const field of plugin.auth) {
      const value = authValues[field.key]?.trim();
      if (value) values[field.key] = value;
    }
    onSaveAuth(plugin, values);
    closeAuthForm();
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
        <TextInput
          size="sm"
          class="w-64 pl-8"
          placeholder="Filter by name or ID..."
          value={search}
          oninput={(event) => (search = event.currentTarget.value)}
        />
        {#if search}
          <Button
            type="button"
            variant="ghost"
            size="icon"
            onclick={() => (search = "")}
            aria-label="Clear search"
            class="absolute right-1 top-1/2 h-6 w-6 -translate-y-1/2 rounded-xs text-text-disabled hover:bg-transparent hover:text-text-muted"
          >
            <X class="h-3 w-3" />
          </Button>
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
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onclick={() => toggleAuthExpanded(plugin.id)}
                class={"h-auto gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast hover:bg-transparent " +
                  (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
              >
                <KeyRound class="h-3.5 w-3.5" />
                {authExpanded ? "Close" : "Configure"}
              </Button>
            {/if}
            {#if !plugin.installed || !plugin.enabled}
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onclick={() => onInstall(plugin)}
                disabled={installingId === plugin.id}
                class="h-auto shrink-0 gap-1.5 px-2.5 py-1.5 text-xs text-text-muted transition-colors duration-fast hover:bg-transparent hover:text-text-accent"
              >
                {#if installingId === plugin.id}
                  <Loader2 class="h-3.5 w-3.5 animate-spin" />
                {:else}
                  <Download class="h-3.5 w-3.5" />
                {/if}
                Install
              </Button>
            {/if}
          </div>
        </div>

        {#if authExpanded}
          <div class="surface-card no-lift">
            <PluginCredentialForm
              fields={plugin.auth}
              getPlaceholder={(field) =>
                plugin.missingAuthKeys.includes(field.key)
                  ? "Required"
                  : "Saved - enter a new value to replace"}
              getValueKey={(field) => field.key}
              inputIdPrefix={`community-plugin-auth-${plugin.id}`}
              onCancel={closeAuthForm}
              onSave={() => saveAuth(plugin)}
              saving={authSavingFor === `prismedia:${plugin.id}`}
              bind:values={authValues}
            />
          </div>
        {/if}
      {/each}
    </div>
  {/if}
</section>
