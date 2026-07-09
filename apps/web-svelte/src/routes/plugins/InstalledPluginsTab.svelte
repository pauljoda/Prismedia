<script lang="ts">
  import {
    AlertCircle,
    Check,
    Download,
    Film,
    KeyRound,
    Loader2,
    Package,
    Search,
    Sparkles,
    Trash2,
    Users,
    X,
  } from "@lucide/svelte";
  import { Badge, Button, TextInput } from "@prismedia/ui-svelte";
  import { ENTITY_KIND } from "$lib/api/generated/codes";
  import type { PluginProvider } from "$lib/api/generated/model";
  import { entityTerms } from "$lib/terminology";
  import PluginCredentialForm from "./PluginCredentialForm.svelte";

  type CapFilter = "all" | "scene" | "performer";

  interface Props {
    authExpandedFor: string | null;
    authSavingFor: string | null;
    authValues: Record<string, string>;
    isSfw: boolean;
    onAuthCancel: () => void;
    onProviderAuthToggle: (pluginId: string) => void;
    onProviderInstall: (plugin: PluginProvider) => void;
    onProviderRemove: (plugin: PluginProvider) => void;
    onProviderSaveAuth: (plugin: PluginProvider) => void;
    onProviderUpdate: (plugin: PluginProvider) => void;
    providerInstallingId: string | null;
    providerRemovingId: string | null;
    providerUpdatingId: string | null;
    providers: PluginProvider[];
  }

  let {
    authExpandedFor,
    authSavingFor,
    authValues = $bindable(),
    isSfw,
    onAuthCancel,
    onProviderAuthToggle,
    onProviderInstall,
    onProviderRemove,
    onProviderSaveAuth,
    onProviderUpdate,
    providerInstallingId,
    providerRemovingId,
    providerUpdatingId,
    providers,
  }: Props = $props();

  let search = $state("");
  let capFilter = $state<CapFilter>("all");

  const filteredProviders = $derived.by(() => {
    const query = search.trim().toLowerCase();
    return providers.filter((plugin) => {
      if (query && !plugin.name.toLowerCase().includes(query) && !plugin.id.toLowerCase().includes(query)) {
        return false;
      }

      return matchesProviderCapabilityFilter(plugin);
    });
  });

  function matchesProviderCapabilityFilter(plugin: PluginProvider): boolean {
    if (capFilter === "all") return true;
    if (capFilter === "scene") {
      return plugin.supports.some((support) =>
        support.entityKind === ENTITY_KIND.video || support.entityKind === ENTITY_KIND.videoSeries,
      );
    }

    return plugin.supports.some((support) => support.entityKind === ENTITY_KIND.person);
  }

  function providerSupportLabels(plugin: PluginProvider): string[] {
    return plugin.supports.map((support) =>
      `${support.entityKind}: ${support.actions.join(", ")}`,
    );
  }
</script>

<section class="space-y-2">
  <div class="surface-well flex items-center gap-2 px-3 py-2 flex-wrap">
    <div class="relative">
      <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-text-disabled" />
      <TextInput
        size="sm"
        class="w-56 pl-8"
        placeholder="Search installed..."
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
    {#if !isSfw}
      <div class="w-px h-4 bg-border-subtle mx-1"></div>
      {#each ["all", "scene", "performer"] as const as filter (filter)}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onclick={() => (capFilter = filter)}
          class={"h-auto gap-1.5 rounded-xs border px-2.5 py-1.5 text-xs transition-all duration-fast " +
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
        </Button>
      {/each}
    {/if}
    <div class="flex-1"></div>
    <span class="text-mono-sm text-text-disabled">{filteredProviders.length} shown</span>
  </div>

  {#if filteredProviders.length === 0}
    <div class="surface-card no-lift p-8 text-center">
      <Package class="h-8 w-8 text-text-disabled mx-auto mb-3" />
      <p class="text-text-muted text-sm">
        {#if providers.length === 0}
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
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onclick={() => onProviderUpdate(plugin)}
                    disabled={providerUpdatingId === plugin.id}
                    class="h-auto gap-1.5 px-2.5 py-1.5 text-xs text-status-success-text transition-colors duration-fast hover:bg-transparent hover:text-text-primary"
                  >
                    {#if providerUpdatingId === plugin.id}
                      <Loader2 class="h-3.5 w-3.5 animate-spin" />
                    {:else}
                      <Download class="h-3.5 w-3.5" />
                    {/if}
                    Update
                  </Button>
                {/if}
                {#if !plugin.installed || !plugin.enabled}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onclick={() => onProviderInstall(plugin)}
                    disabled={providerInstallingId === plugin.id}
                    class="h-auto gap-1.5 px-2.5 py-1.5 text-xs text-text-muted transition-colors duration-fast hover:bg-transparent hover:text-text-accent"
                  >
                    {#if providerInstallingId === plugin.id}
                      <Loader2 class="h-3.5 w-3.5 animate-spin" />
                    {:else}
                      <Download class="h-3.5 w-3.5" />
                    {/if}
                    Install
                  </Button>
                {/if}
                {#if hasAuth}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onclick={() => onProviderAuthToggle(plugin.id)}
                    class={"h-auto gap-1.5 px-2.5 py-1.5 text-xs transition-colors duration-fast hover:bg-transparent " +
                      (plugin.missingAuthKeys.length > 0 ? "text-status-warning-text" : "text-text-muted hover:text-text-primary")}
                  >
                    <KeyRound class="h-3.5 w-3.5" />
                    {authExpanded ? "Close" : "Configure"}
                  </Button>
                {/if}
                {#if plugin.installed}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onclick={() => onProviderRemove(plugin)}
                    disabled={providerRemovingId === plugin.id}
                    class="h-auto gap-1.5 px-2.5 py-1.5 text-xs text-text-muted transition-colors duration-fast hover:bg-transparent hover:text-status-error-text"
                  >
                    {#if providerRemovingId === plugin.id}
                      <Loader2 class="h-3.5 w-3.5 animate-spin" />
                    {:else}
                      <Trash2 class="h-3.5 w-3.5" />
                    {/if}
                    Remove
                  </Button>
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
    </div>
  {/if}
</section>
