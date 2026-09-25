<script lang="ts">
  import { Package, Sparkles } from "@lucide/svelte";
  import { Button, SearchInput } from "@prismedia/ui-svelte";
  import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PluginCard from "$lib/components/plugins/PluginCard.svelte";
  import PluginCoverage from "$lib/components/plugins/PluginCoverage.svelte";
  import { familyCoverage, pluginServesFamily } from "$lib/plugins/plugin-families";
  import PluginCredentialForm from "./PluginCredentialForm.svelte";

  interface Props {
    authExpandedFor: string | null;
    authSavingFor: string | null;
    authValues: Record<string, string>;
    /** Connections, grouped onto the plugin cards that back them. */
    connections?: ConnectionResponse[];
    onAuthCancel: () => void;
    onBrowseCommunity?: () => void;
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
    connections = [],
    onAuthCancel,
    onBrowseCommunity,
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
  let family = $state<string | null>(null);

  const ordered = $derived(
    [...providers].sort(
      (left, right) => Number(right.enabled) - Number(left.enabled) || left.name.localeCompare(right.name),
    ),
  );
  const coverage = $derived(familyCoverage(providers));
  const filteredProviders = $derived.by(() => {
    const query = search.trim().toLowerCase();
    return ordered.filter(
      (plugin) =>
        (!query || plugin.name.toLowerCase().includes(query) || plugin.id.toLowerCase().includes(query)) &&
        (!family || pluginServesFamily(plugin, family)),
    );
  });
  const connectionsByPlugin = $derived.by(() => {
    const grouped = new Map<string, ConnectionResponse[]>();
    for (const connection of connections) {
      const list = grouped.get(connection.pluginId);
      if (list) list.push(connection);
      else grouped.set(connection.pluginId, [connection]);
    }
    return grouped;
  });
</script>

{#if providers.length === 0}
  <StatePlaceholder icon={Package} title="No plugins installed">
    {#if onBrowseCommunity}
      <Button variant="secondary" size="sm" onclick={onBrowseCommunity}>
        <Sparkles aria-hidden="true" />
        Browse community
      </Button>
    {/if}
  </StatePlaceholder>
{:else}
  <section class="flex flex-col gap-4">
    <PluginCoverage {coverage} selected={family} onSelect={(next) => (family = next)} />

    <div class="flex flex-wrap items-center gap-3">
      <SearchInput
        name="installed-plugin-search"
        ariaLabel="Search installed plugins"
        class="w-full sm:w-64"
        placeholder="Search installed..."
        bind:value={search}
      />
      {#if search.trim() || family}
        <span class="font-mono text-[0.72rem] text-text-muted">
          <span class="text-text-secondary">{filteredProviders.length}</span>/{providers.length}
        </span>
      {/if}
    </div>

    {#if filteredProviders.length === 0}
      <StatePlaceholder icon={Package} title="No matching plugins" />
    {:else}
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {#each filteredProviders as plugin (plugin.id)}
          {@const credentialsOpen = authExpandedFor === `prismedia:${plugin.id}`}
          <PluginCard
            {plugin}
            connections={connectionsByPlugin.get(plugin.id) ?? []}
            {credentialsOpen}
            updating={providerUpdatingId === plugin.id}
            removing={providerRemovingId === plugin.id}
            enabling={providerInstallingId === plugin.id}
            onUpdate={onProviderUpdate}
            onRemove={onProviderRemove}
            onEnable={onProviderInstall}
            onToggleCredentials={(target) => onProviderAuthToggle(target.id)}
          >
            {#snippet credentials()}
              <PluginCredentialForm
                fields={plugin.auth}
                getPlaceholder={(field) =>
                  plugin.missingAuthKeys.includes(field.key) ? "Required" : "Saved - enter a new value to replace"}
                getValueKey={(field) => `prismedia:${plugin.id}:${field.key}`}
                inputIdPrefix={`plugin-auth-${plugin.id}`}
                onCancel={onAuthCancel}
                onSave={() => onProviderSaveAuth(plugin)}
                saving={authSavingFor === `prismedia:${plugin.id}`}
                bind:values={authValues}
              />
            {/snippet}
          </PluginCard>
        {/each}
      </div>
    {/if}
  </section>
{/if}
