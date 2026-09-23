<script lang="ts">
  import { onMount } from "svelte";
  import { AlertTriangle, ArrowUpRight, Check, FolderCog, Plug, Plus, RefreshCw, ShieldUser } from "@lucide/svelte";
  import { Alert, Badge, Button, DialogBase, Panel, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY, type PluginCapabilityCode } from "$lib/api/generated/codes";
  import type { ConnectionResponse, CreateConnectionRequest, EntityKind, ExternalLibraryMount, PluginProvider } from "$lib/api/generated/model";
  import { addConnection, fetchConnections, probeConnection, removeConnection, saveConnection } from "$lib/api/connections";
  import { fetchLibraryMounts } from "$lib/api/managed-libraries";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { capabilityLabels, connectionStatusLabels } from "$lib/integrations/connection-labels";
  import { canBrowseRequestSource } from "$lib/requests/request-source-compatibility";
  import { SETTING_SECTION, settingsSectionById } from "$lib/settings/settings-section-catalog";
  import { useSession } from "$lib/stores/session.svelte";
  import ExternalLibraryMappings from "$lib/components/integrations/ExternalLibraryMappings.svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
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
  let libraryMounts = $state<Record<string, ExternalLibraryMount[]>>({});
  let libraryMountErrors = $state<Record<string, string>>({});
  let mappingTarget = $state<ConnectionResponse | null>(null);
  let mappingClosing = $state(false);
  let editorTrigger = $state<HTMLElement | null>(null);
  let editorClosing = $state(false);
  const availablePlugins = $derived(plugins.filter(item => item.installed && item.enabled && item.integration));

  onMount(() => { if (session.isAdmin) void load(); else loading = false; });
  $effect(() => {
    if (loading || !connections.some(connection => connection.enabled
      && connection.status === CONNECTION_STATUS.unverified)) return;
    let active = true;
    let refreshing = false;
    const timer = setInterval(async () => {
      if (refreshing) return;
      refreshing = true;
      try {
        const refreshed = await fetchConnections();
        if (!active) return;
        const currentById = new Map(connections.map(connection => [connection.id, connection]));
        const merged = refreshed.map(connection => {
          const current = currentById.get(connection.id);
          return current && current.revision > connection.revision ? current : connection;
        });
        connections = merged;
        await Promise.all(merged
          .filter(connection => connection.status === CONNECTION_STATUS.ready
            && currentById.get(connection.id)?.status !== CONNECTION_STATUS.ready)
          .map(refreshLibraryMounts));
      }
      catch { /* The normal page error surface remains reserved for user-initiated reads. */ }
      finally { refreshing = false; }
    }, 2_000);
    return () => { active = false; clearInterval(timer); };
  });
  function supportsLibraryMappings(connection: ConnectionResponse) {
    return connection.effectiveCapabilities.some(item => item.kind === PLUGIN_CAPABILITY.connectedLibrary || item.kind === PLUGIN_CAPABILITY.externalManager);
  }
  function mappingKind(connection: ConnectionResponse): EntityKind | undefined {
    return connection.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.externalManager
      && item.operations.includes(INTEGRATION_OPERATION.managerOptions))?.entityKinds[0];
  }
  function capabilityPurpose(connection: ConnectionResponse, capability: PluginCapabilityCode): string {
    const operations = connection.effectiveCapabilities
      .filter(item => item.kind === capability)
      .flatMap(item => item.operations);
    switch (capability) {
      case PLUGIN_CAPABILITY.connectedLibrary:
        return operations.includes(INTEGRATION_OPERATION.searchLibrary) && operations.includes(INTEGRATION_OPERATION.getLibraryItem)
          ? "Browse existing library · reads existing files in place"
          : operations.includes(INTEGRATION_OPERATION.searchLibrary) ? "Browse existing library" : "Connected library settings";
      case PLUGIN_CAPABILITY.externalManager:
        return operations.some(operation => operation === INTEGRATION_OPERATION.requestManaged
          || operation === INTEGRATION_OPERATION.ensureManaged
          || operation === INTEGRATION_OPERATION.configureManaged
          || operation === INTEGRATION_OPERATION.reconcileManaged)
          ? "Manage requests · acquisition stays in the connected app"
          : operations.includes(INTEGRATION_OPERATION.managerOptions) ? "Application profiles and settings" : "Manager integration";
      case PLUGIN_CAPABILITY.catalogDiscovery:
        return operations.some(operation => operation === INTEGRATION_OPERATION.browse || operation === INTEGRATION_OPERATION.search)
          ? "Browse & import · selected titles download into Prismedia"
          : operations.includes(INTEGRATION_OPERATION.inspect) ? "Inspect catalog entries" : "Catalog integration";
      case PLUGIN_CAPABILITY.transferExecutor:
        return operations.includes(INTEGRATION_OPERATION.submit)
          ? "Download URLs · downloads use this connection" : "Download executor";
      case PLUGIN_CAPABILITY.acquisitionSource: return "Acquisition sources · supplies candidates for requests";
      case PLUGIN_CAPABILITY.metadata: return "Metadata lookup · fills title details";
    }
  }
  async function refreshLibraryMounts(connection: ConnectionResponse) {
    if (!supportsLibraryMappings(connection)) {
      const { [connection.id]: _mounts, ...remainingMounts } = libraryMounts;
      const { [connection.id]: _errors, ...remainingErrors } = libraryMountErrors;
      libraryMounts = remainingMounts;
      libraryMountErrors = remainingErrors;
      return;
    }
    try {
      const mounts = await fetchLibraryMounts(connection.id);
      if (!connections.some(item => item.id === connection.id && item.revision === connection.revision)) return;
      libraryMounts = { ...libraryMounts, [connection.id]: mounts };
      const { [connection.id]: _, ...remainingErrors } = libraryMountErrors;
      libraryMountErrors = remainingErrors;
    } catch (cause) {
      if (!connections.some(item => item.id === connection.id && item.revision === connection.revision)) return;
      const message = cause instanceof Error ? cause.message : "Could not read library folders";
      const { [connection.id]: _, ...remainingMounts } = libraryMounts;
      libraryMounts = remainingMounts;
      libraryMountErrors = { ...libraryMountErrors, [connection.id]: message };
    }
  }
  async function load() {
    loading = true; error = null;
    try {
      const [nextConnections, nextPlugins] = await Promise.all([fetchConnections(), fetchPluginProviders()]);
      connections = nextConnections;
      plugins = nextPlugins;
      await Promise.all(nextConnections.map(refreshLibraryMounts));
    }
    catch (cause) { error = cause instanceof Error ? cause.message : "Could not load connections"; }
    finally { loading = false; }
  }
  function upsert(value: ConnectionResponse) {
    connections = [...connections.filter(item => item.id !== value.id), value].sort((a, b) => a.name.localeCompare(b.name));
  }
  function restoreEditorFocus(trigger: HTMLElement | null) {
    if (!trigger) return;
    requestAnimationFrame(() => trigger.focus());
  }
  function closeEditor() {
    if (editorClosing) return;
    const trigger = editorTrigger;
    editorClosing = true;
    editorOpen = false;
    editing = null;
    editorTrigger = null;
    restoreEditorFocus(trigger);
    editorClosing = false;
  }
  function openEditor(value: ConnectionResponse | null, trigger?: HTMLElement) {
    editing = value; editorTrigger = trigger ?? null; editorError = null; message = null; editorOpen = true;
  }
  async function save(request: CreateConnectionRequest) {
    saving = true; editorError = null;
    try {
      const result = editing ? await saveConnection(editing.id, { ...request, expectedRevision: editing.revision }) : await addConnection(request);
      upsert(result);
      editorOpen = false;
      editing = null;
      await refreshLibraryMounts(result);
      if (result.enabled) {
        message = "Connection saved. Prismedia is checking it automatically.";
      } else {
        message = "Connection saved. Enable it when ready; Prismedia will check it automatically.";
      }
    } catch (cause) { editorError = cause instanceof Error ? cause.message : "Could not save connection"; }
    finally {
      saving = false;
      if (!editorOpen) {
        const trigger = editorTrigger;
        editorTrigger = null;
        restoreEditorFocus(trigger);
      }
    }
  }
  async function test(connection: ConnectionResponse) {
    testingId = connection.id; error = null; message = null;
    try {
      const result = await probeConnection(connection.id); upsert(result); await refreshLibraryMounts(result);
      message = result.status === CONNECTION_STATUS.ready
        ? `${result.name} is connected.`
        : `${result.name} test status: ${connectionStatusLabels[result.status]}.`;
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not test connection"; }
    finally { testingId = null; }
  }
  function openMappings(connection: ConnectionResponse) {
    if (!saving && !mappingClosing && mappingKind(connection)) mappingTarget = connection;
  }
  async function closeMappings() {
    const target = mappingTarget;
    if (!target || mappingClosing) return;
    mappingClosing = true;
    mappingTarget = null;
    await refreshLibraryMounts(target);
    mappingClosing = false;
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
        <Button onclick={(event) => openEditor(null, event.currentTarget as HTMLElement)} disabled={loading || saving || !availablePlugins.length || editorOpen}><Plus />Add connection</Button>
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
        <ConnectionEditor open={editorOpen} connection={editing} plugins={availablePlugins} {saving} error={editorError}
          onSave={save} onCancel={closeEditor} />
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
                <div class="flex min-w-0 items-start gap-3">
                  <PluginIcon name={plugin?.name ?? connection.pluginId} iconUrl={plugin?.iconUrl} class="size-11" />
                  <div class="flex min-w-0 flex-col gap-1">
                    <h2 class="text-base font-semibold">{connection.name}</h2>
                    <p class="text-sm text-text-muted">{plugin?.name ?? connection.pluginId}</p>
                    <p class="break-all font-mono text-xs text-text-muted">{connection.baseUrl}</p>
                  </div>
                </div>
                <Badge>{connectionStatusLabels[connection.status]}</Badge>
              </div>
              <div class="flex flex-col gap-2" aria-label="Configured capabilities">
                <p class="text-xs font-medium uppercase tracking-[0.12em] text-text-muted">Configured for</p>
                <div class="flex flex-wrap gap-2">
                  {#each connection.enabledCapabilities as capability (capability)}<Badge>{capabilityLabels[capability]}</Badge>{/each}
                </div>
                <p class="text-xs text-text-muted">{connection.enabledCapabilities.map(capability => capabilityPurpose(connection, capability)).join(" · ")}</p>
              </div>
              {#if connection.status === CONNECTION_STATUS.unverified}
                <p class="text-xs text-text-muted">Prismedia is checking this connection automatically.</p>
              {:else if connection.lastError}
                <Alert.Root variant="destructive"><Alert.Description>{connection.lastError}</Alert.Description></Alert.Root>
              {:else if connection.status === CONNECTION_STATUS.ready}
                <p class="text-xs text-text-muted">API reachable{connection.lastCheckedAt ? ` · tested ${new Date(connection.lastCheckedAt).toLocaleString()}` : ""}</p>
              {/if}
              {#if supportsLibraryMappings(connection)}
                {#if libraryMountErrors[connection.id]}
                  <p class="text-xs text-text-muted">Library folder links unavailable.</p>
                {:else if libraryMounts[connection.id]}
                  {@const count = libraryMounts[connection.id].length}
                  <p class="text-xs text-text-muted">{count === 0 ? "No library folders linked" : `${count} library folder${count === 1 ? "" : "s"} linked`}</p>
                {:else}
                  <p class="text-xs text-text-muted">Library folder links are loading…</p>
                {/if}
              {/if}
              {#if connection.status === CONNECTION_STATUS.ready && !connection.hasPersistentRemoteIdentity}
                <p class="text-xs text-text-muted">This application does not report an installation ID. Its items are tracked within this connection.</p>
              {/if}
              {#if !plugin?.installed || !plugin.enabled}
                <p class="text-sm text-text-muted">Install and enable this plugin to use the connection.</p>
              {/if}
              <div class="flex flex-wrap gap-2 border-t border-border-subtle pt-3">
                <Button variant="secondary" size="sm" onclick={() => void test(connection)} disabled={!connection.enabled || saving || !!testingId || editorOpen || !plugin?.enabled}>
                  <RefreshCw class={testingId === connection.id ? "animate-spin" : undefined} />{testingId === connection.id ? "Testing…" : "Test connection"}
                </Button>
                {#if connection.status === CONNECTION_STATUS.ready && canBrowseRequestSource(connection)}
                  <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={`/request?connection=${connection.id}`}>Browse titles</a>
                {/if}
                {#if supportsLibraryMappings(connection) && mappingKind(connection)}
                  <Button variant="secondary" size="sm" onclick={() => openMappings(connection)} disabled={saving || !!testingId || editorOpen}>
                    <FolderCog />Manage library folders
                  </Button>
                {:else if supportsLibraryMappings(connection)}
                  <a class={buttonVariants({ variant: "secondary", size: "sm" })} href={`/settings/${SETTING_SECTION.libraries}`}>
                    <FolderCog />Map library folders
                  </a>
                {/if}
                <Button variant="ghost" size="sm" onclick={(event) => openEditor(connection, event.currentTarget as HTMLElement)} disabled={saving || editorOpen || !!testingId || !plugin?.enabled}>Edit</Button>
                <Button variant="ghost" size="sm" onclick={() => deleteTarget = connection} disabled={saving || editorOpen || !!testingId}>Remove</Button>
              </div>
            </article>
          </Panel>
        {/each}
      </div>
    {/if}
</div>
{/if}

<DialogBase.Root open={!!mappingTarget} onOpenChange={value => { if (!value) void closeMappings(); }}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    {#if mappingTarget}
      {@const kind = mappingKind(mappingTarget)}
      <DialogBase.Header>
        <DialogBase.Title>Library folders · {mappingTarget.name}</DialogBase.Title>
        <DialogBase.Description>Review the existing folders Prismedia can read. The connected application continues to organize its files.</DialogBase.Description>
      </DialogBase.Header>
      <ExternalLibraryMappings connection={mappingTarget} {kind} />
      <DialogBase.Footer><Button variant="outline" onclick={() => void closeMappings()}>Done</Button></DialogBase.Footer>
    {/if}
  </DialogBase.Content>
</DialogBase.Root>

<ConfirmDialog open={!!deleteTarget} title="Remove connection?" message={`Remove ${deleteTarget?.name ?? "this connection"} and its saved credentials?`}
  confirmLabel="Remove connection" danger onClose={() => deleteTarget = null} onConfirm={async () => {
    if (!deleteTarget) return;
    await removeConnection(deleteTarget); connections = connections.filter(item => item.id !== deleteTarget?.id);
  }} />
