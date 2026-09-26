<script lang="ts">
  import { Check, Download, KeyRound, Loader2, RefreshCw, Sparkles } from "@lucide/svelte";
  import { Button, SearchInput } from "@prismedia/ui-svelte";
  import type { PluginProvider } from "$lib/api/generated/model";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import FamilyBandStrip from "$lib/components/entities/FamilyBandStrip.svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { pluginFamilies } from "$lib/plugins/plugin-families";
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

  /** Not-yet-installed plugins lead; installed ones follow so the catalog still reads complete. */
  const ordered = $derived(
    [...plugins].sort(
      (left, right) =>
        Number(left.installed && left.enabled) - Number(right.installed && right.enabled) ||
        left.name.localeCompare(right.name),
    ),
  );
  const filteredPlugins = $derived.by(() => {
    const query = search.trim().toLowerCase();
    if (!query) return ordered;
    return ordered.filter(
      (plugin) => plugin.name.toLowerCase().includes(query) || plugin.id.toLowerCase().includes(query),
    );
  });
  const familiesByPlugin = $derived(new Map(plugins.map((plugin) => [plugin.id, pluginFamilies(plugin)])));

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

<section class="flex flex-col gap-4">
  <div class="flex flex-wrap items-center gap-2">
    <SearchInput
      name="community-plugin-search"
      ariaLabel="Search community plugins"
      class="w-full sm:w-64"
      placeholder="Filter by name or ID..."
      bind:value={search}
    />
    <Button variant="ghost" size="sm" onclick={onRefresh} disabled={loading}>
      {#if loading}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<RefreshCw aria-hidden="true" />{/if}
      Refresh
    </Button>
  </div>

  {#if loading && !loaded}
    <StatePlaceholder icon={Sparkles} title="Loading plugin index" busy />
  {:else if filteredPlugins.length === 0}
    <StatePlaceholder icon={Sparkles} title={search ? "No matching plugins" : "No plugins available"} />
  {:else}
    <ul
      class="flex flex-col divide-y divide-[var(--color-border-subtle)] overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)]"
    >
      {#each filteredPlugins as plugin (plugin.id)}
        {@const families = familiesByPlugin.get(plugin.id) ?? []}
        {@const authExpanded = authExpandedFor === plugin.id}
        {@const hasAuth = plugin.supports.length > 0 && plugin.auth.length > 0}
        {@const active = plugin.installed && plugin.enabled}
        {@const operations = [...new Set(families.flatMap((support) => support.operations))]}
        <li class="min-w-0">
          <div class="flex min-w-0 items-center gap-3 px-3 py-2.5 sm:px-4">
            <PluginIcon name={plugin.name} iconUrl={plugin.iconUrl} class="size-9" />
            <div class="min-w-0 flex-1">
              <div class="flex min-w-0 flex-wrap items-baseline gap-x-2">
                <p class="truncate text-label font-medium text-text-primary">{plugin.name}</p>
                <span class="font-mono text-[0.64rem] text-text-disabled">v{plugin.version}</span>
                {#if hasAuth}
                  <KeyRound class="size-3 self-center text-text-disabled" aria-label="Requires credentials" />
                {/if}
              </div>
              <p class="truncate text-caption text-text-muted">
                {families.map((support) => support.family.label).join(", ") || "—"}
                {#if operations.length > 0}<span class="text-text-disabled"> — {operations.join(" · ")}</span>{/if}
              </p>
            </div>
            <FamilyBandStrip
              bands={families.map((support) => ({ key: support.family.key, label: support.family.label, accent: support.family.accent }))}
              height={4}
              label="Families"
              class="hidden w-24 shrink-0 md:flex"
            />
            <div class="flex shrink-0 items-center gap-1">
              {#if hasAuth && plugin.installed}
                <Button
                  variant={plugin.missingAuthKeys.length > 0 && !authExpanded ? "secondary" : "ghost"}
                  size="sm"
                  aria-expanded={authExpanded}
                  onclick={() => toggleAuthExpanded(plugin.id)}
                >
                  <KeyRound aria-hidden="true" />
                  {authExpanded ? "Close" : "Keys"}
                </Button>
              {/if}
              {#if active}
                <span class="flex h-control-sm items-center gap-1.5 px-2 text-caption text-text-muted">
                  <Check class="size-3.5" aria-hidden="true" />
                  Installed
                </span>
              {:else}
                <Button variant="secondary" size="sm" onclick={() => onInstall(plugin)} disabled={installingId === plugin.id}>
                  {#if installingId === plugin.id}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Download aria-hidden="true" />{/if}
                  {plugin.installed ? "Enable" : "Install"}
                </Button>
              {/if}
            </div>
          </div>

          {#if authExpanded}
            <PluginCredentialForm
              fields={plugin.auth}
              getPlaceholder={(field) =>
                plugin.missingAuthKeys.includes(field.key) ? "Required" : "Saved - enter a new value to replace"}
              getValueKey={(field) => field.key}
              inputIdPrefix={`community-plugin-auth-${plugin.id}`}
              onCancel={closeAuthForm}
              onSave={() => saveAuth(plugin)}
              saving={authSavingFor === `prismedia:${plugin.id}`}
              bind:values={authValues}
            />
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</section>
