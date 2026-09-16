<script lang="ts">
  import { onMount } from "svelte";
  import { AlertTriangle, ArrowUpRight, Check, Plug, Plus, RefreshCw, ShieldUser } from "@lucide/svelte";
  import { Alert, Badge, Button, Panel, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, CreateConnectionRequest, PluginProvider } from "$lib/api/generated/model";
  import { addConnection, fetchConnections, probeConnection, removeConnection, saveConnection } from "$lib/api/connections";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { capabilityLabels, connectionStatusLabels } from "$lib/integrations/connection-labels";
  import { SETTING_SECTION, settingsSectionById } from "$lib/settings/settings-section-catalog";
  import { useSession } from "$lib/stores/session.svelte";
  import ConnectionEditor from "./ConnectionEditor.svelte";

  const session = useSession();
  const accent = settingsSectionById(SETTING_SECTION.connections)?.accent;
  let connections = $state<ConnectionResponse[]>([]);
  let plugins = $state<PluginProvider[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let editorError = $state<string | null>(null);
  let message = $state<string | null>(null);
  let editing = $state<ConnectionResponse | null>(null);
  let editorOpen = $state(false);
  let saving = $state(false);
  let testingId = $state<string | null>(null);
  let deleteTarget = $state<ConnectionResponse | null>(null);
  const availablePlugins = $derived(plugins.filter(item => item.installed && item.enabled && item.integration));

  onMount(() => { if (session.isAdmin) void load(); else loading = false; });
  async function load() {
    loading = true; error = null;
    try { [connections, plugins] = await Promise.all([fetchConnections(), fetchPluginProviders()]); }
    catch (cause) { error = cause instanceof Error ? cause.message : "Could not load connections"; }
    finally { loading = false; }
  }
  function upsert(value: ConnectionResponse) {
    connections = [...connections.filter(item => item.id !== value.id), value].sort((a, b) => a.name.localeCompare(b.name));
  }
  function openEditor(value: ConnectionResponse | null) {
    editing = value; editorError = null; message = null; editorOpen = true;
  }
  async function save(request: CreateConnectionRequest) {
    saving = true; editorError = null;
    try {
      const result = editing ? await saveConnection(editing.id, { ...request, expectedRevision: editing.revision }) : await addConnection(request);
      upsert(result); editorOpen = false; editing = null; message = "Connection saved. Test it to verify the available capabilities.";
    } catch (cause) { editorError = cause instanceof Error ? cause.message : "Could not save connection"; }
    finally { saving = false; }
  }
  async function test(connection: ConnectionResponse) {
    testingId = connection.id; error = null; message = null;
    try {
      const result = await probeConnection(connection.id); upsert(result);
      if (result.status === CONNECTION_STATUS.ready) message = `${result.name} is connected.`;
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not test connection"; }
    finally { testingId = null; }
  }
</script>

<svelte:head><title>Connections · Prismedia</title></svelte:head>

{#if !session.isAdmin}
  <StatePlaceholder icon={ShieldUser} title="Administrator access required" description="Connections are managed by a server administrator." />
{:else}
  <div class="flex min-w-0 flex-col gap-5">
    <header class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div class="flex flex-col gap-2">
        <BackLink fallback="/settings" label="Settings" class="self-start" />
        <h1 class="flex items-center gap-2.5"><Plug class="size-5" style={`color: ${accent}`} />Connections</h1>
        <p class="max-w-2xl text-sm text-text-muted">Connect your catalogs and media applications. Use several instances of the same plugin, each with its own settings.</p>
      </div>
      <div class="flex flex-wrap gap-2">
        <a class={buttonVariants({ variant: "secondary" })} href="/plugins"><ArrowUpRight />Manage plugins</a>
        <Button onclick={() => openEditor(null)} disabled={loading || !availablePlugins.length || editorOpen}><Plus />Add connection</Button>
      </div>
    </header>
    {#if error}
      <Alert.Root variant="destructive"><AlertTriangle /><Alert.Description>{error}</Alert.Description>
        <Alert.Action><Button variant="secondary" size="sm" onclick={load}>Retry</Button></Alert.Action>
      </Alert.Root>
    {/if}
    {#if message}
      <Alert.Root role="status"><Check /><Alert.Description>{message}</Alert.Description></Alert.Root>
    {/if}
    {#if editorOpen}
      {#key editing?.id}
        <ConnectionEditor connection={editing} plugins={availablePlugins} {saving} error={editorError}
          onSave={save} onCancel={() => { editorOpen = false; editing = null; }} />
      {/key}
    {/if}
    {#if loading}
      <StatePlaceholder icon={Plug} title="Loading connections" busy />
    {:else if !connections.length && !editorOpen && !error}
      <StatePlaceholder icon={Plug} title="Connect your first application"
        description={availablePlugins.length ? "Add a connection to an installed integration plugin. You can connect more than one instance." : "Install a plugin with discovery, download, or library capabilities to get started."} />
    {:else}
      <div class="flex flex-col gap-3">
        {#each connections as connection (connection.id)}
          {@const plugin = plugins.find(item => item.id === connection.pluginId)}
          <Panel class="p-5">
            <article class="flex flex-col gap-4" aria-label={connection.name}>
              <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="flex min-w-0 flex-col gap-1">
                  <h2 class="text-base font-semibold">{connection.name}</h2>
                  <p class="text-sm text-text-muted">{plugin?.name ?? connection.pluginId}</p>
                  <p class="break-all font-mono text-xs text-text-muted">{connection.baseUrl}</p>
                </div>
                <Badge>{connectionStatusLabels[connection.status]}</Badge>
              </div>
              <div class="flex flex-wrap gap-2" aria-label="Enabled capabilities">
                {#each connection.enabledCapabilities as capability (capability)}<Badge>{capabilityLabels[capability]}</Badge>{/each}
              </div>
              {#if connection.lastError}
                <Alert.Root variant="destructive"><Alert.Description>{connection.lastError}</Alert.Description></Alert.Root>
              {:else if connection.status === CONNECTION_STATUS.ready}
                <p class="text-xs text-text-muted">{connection.effectiveCapabilities.map(item => capabilityLabels[item.kind]).join(" · ")} verified{connection.lastCheckedAt ? ` · ${new Date(connection.lastCheckedAt).toLocaleString()}` : ""}</p>
              {/if}
              {#if connection.status === CONNECTION_STATUS.ready && !connection.hasPersistentRemoteIdentity}
                <p class="text-xs text-text-muted">This application does not report an installation ID. Its items are tracked within this connection.</p>
              {/if}
              {#if !plugin?.installed || !plugin.enabled}
                <p class="text-sm text-text-muted">Install and enable this plugin to use the connection.</p>
              {/if}
              <div class="flex flex-wrap gap-2 border-t border-border-subtle pt-3">
                <Button variant="secondary" size="sm" onclick={() => void test(connection)} disabled={!connection.enabled || !!testingId || editorOpen || !plugin?.enabled}>
                  <RefreshCw class={testingId === connection.id ? "animate-spin" : undefined} />{testingId === connection.id ? "Testing…" : "Test connection"}
                </Button>
                {#if connection.status === CONNECTION_STATUS.ready && connection.effectiveCapabilities.some(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery)}
                  <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={`/request/catalogs?connection=${connection.id}`}>Browse catalog</a>
                {/if}
                <Button variant="ghost" size="sm" onclick={() => openEditor(connection)} disabled={editorOpen || !!testingId || !plugin?.enabled}>Edit</Button>
                <Button variant="ghost" size="sm" onclick={() => deleteTarget = connection} disabled={editorOpen || !!testingId}>Remove</Button>
              </div>
            </article>
          </Panel>
        {/each}
      </div>
    {/if}
  </div>
{/if}

<ConfirmDialog open={!!deleteTarget} title="Remove connection?" message={`Remove ${deleteTarget?.name ?? "this connection"} and its saved credentials?`}
  confirmLabel="Remove connection" danger onClose={() => deleteTarget = null} onConfirm={async () => {
    if (!deleteTarget) return;
    await removeConnection(deleteTarget); connections = connections.filter(item => item.id !== deleteTarget?.id);
  }} />
