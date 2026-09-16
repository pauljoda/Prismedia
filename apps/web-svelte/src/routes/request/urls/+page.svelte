<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Download, Link, Search, ShieldUser } from "@lucide/svelte";
  import { Alert, Button, Panel, Select, TextInput, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, ExecutorInspectionResponse, IntegrationTransferResponse, LibraryRoot } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { inspectPublicationUrl, acquireExecutorPublication, fetchIntegrationTransfers } from "$lib/api/integration-transfers";
  import { fetchLibraryRoots } from "$lib/api/settings";
  import TransferList from "$lib/components/integrations/TransferList.svelte";
  import { isTransferTerminal } from "$lib/integrations/transfer-labels";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { getEntityKindLabel } from "$lib/entities/entity-grid";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let kind = $state<EntityKind | undefined>();
  let url = $state("");
  let inspection = $state<ExecutorInspectionResponse | null>(null);
  let roots = $state<LibraryRoot[]>([]);
  let rootId = $state("");
  let transfers = $state<IntegrationTransferResponse[]>([]);
  let loading = $state(true);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let refreshError = $state<string | null>(null);
  const operations = new Map<string, string>();
  const connection = $derived(connections.find(item => item.id === connectionId));
  const kinds = $derived(connection?.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery)?.entityKinds ?? []);

  onMount(() => {
    if (session.isAdmin) void initialize(); else loading = false;
    const timer = setInterval(() => {
      if (session.isAdmin && transfers.some(item => !isTransferTerminal(item.phase))) void refreshTransfers();
    }, 5000);
    return () => clearInterval(timer);
  });
  async function refreshTransfers() {
    try { transfers = await fetchIntegrationTransfers(); refreshError = null; }
    catch (cause) { refreshError = cause instanceof Error ? cause.message : "Could not refresh transfers"; }
  }
  async function initialize() {
    try {
      const [available, libraries] = await Promise.all([fetchConnections(), fetchLibraryRoots()]);
      connections = available.filter(item => item.status === CONNECTION_STATUS.ready && item.hasPersistentRemoteIdentity
        && item.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.catalogDiscovery && capability.operations.includes(INTEGRATION_OPERATION.inspect))
        && item.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.transferExecutor));
      roots = libraries.filter(root => root.enabled && !root.isReadOnly && root.scanBooks);
      rootId = roots[0]?.id ?? "";
      const requested = page.url.searchParams.get("connection");
      chooseConnection(connections.find(item => item.id === requested)?.id ?? connections[0]?.id ?? "");
      await refreshTransfers();
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not load URL connections"; }
    finally { loading = false; }
  }
  function chooseConnection(value: string) {
    connectionId = value;
    kind = connections.find(item => item.id === value)?.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery)?.entityKinds[0];
    inspection = null; error = null;
  }
  async function inspect() {
    if (!connectionId || !kind || busy) return;
    busy = true; error = null; inspection = null;
    try { inspection = await inspectPublicationUrl(connectionId, { url: url.trim(), entityKind: kind }); }
    catch (cause) { error = cause instanceof Error ? cause.message : "Could not inspect this URL"; }
    finally { busy = false; }
  }
  async function acquire(itemId: string) {
    if (!inspection || !rootId || busy) return;
    const key = JSON.stringify([connectionId, inspection.selectionToken, itemId, rootId]);
    const operationId = operations.get(key) ?? crypto.randomUUID();
    operations.set(key, operationId);
    busy = true; error = null;
    try {
      const accepted = await acquireExecutorPublication(connectionId, { operationId, selectionToken: inspection.selectionToken, itemId, libraryRootId: rootId });
      transfers = [accepted, ...transfers.filter(item => item.id !== accepted.id)];
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not accept this publication. Retry to check the same request."; }
    finally { busy = false; }
  }
</script>

<svelte:head><title>Import from URL · Prismedia</title></svelte:head>

{#if !session.isAdmin}
  <StatePlaceholder icon={ShieldUser} title="Administrator access required" description="URL acquisition connections are managed by a server administrator." />
{:else}
  <div class="flex min-w-0 flex-col gap-5">
    <header class="flex flex-wrap items-end justify-between gap-3">
      <div class="space-y-2">
        <BackLink fallback="/request" label="Requests" />
        <h1 class="flex items-center gap-2.5"><Link class="size-5 text-text-accent" />Import from URL</h1>
        <p class="text-sm text-text-muted">Inspect a source, choose a publication, and import it into your library.</p>
      </div>
      <a class={buttonVariants({ variant: "secondary", size: "sm" })} href="/settings/connections">Manage connections</a>
    </header>
    {#if loading}
      <StatePlaceholder icon={Link} title="Loading connections" busy />
    {:else if !connections.length}
      <StatePlaceholder icon={Link} title="Connect a URL executor" description="Add and test a connection that supports URL inspection and publication downloads." />
    {:else}
      <Panel class="flex flex-col gap-4 p-4">
        <div class="grid min-w-0 gap-3 sm:grid-cols-2">
          <Select ariaLabel="URL executor" value={connectionId} options={connections.map(item => ({ value: item.id, label: item.name }))} onchange={chooseConnection} disabled={busy} />
          <Select ariaLabel="Media type" value={kind} options={kinds.map(value => ({ value, label: getEntityKindLabel(value) }))}
            onchange={value => { kind = kinds.find(item => item === value); inspection = null; }} disabled={busy} />
        </div>
        <form class="flex min-w-0 flex-col gap-3 sm:flex-row" onsubmit={event => { event.preventDefault(); void inspect(); }}>
          <TextInput type="url" aria-label="Source URL" placeholder="https://…" bind:value={url} oninput={() => inspection = null} maxlength={8192} required disabled={busy} class="min-w-0 flex-1" />
          <Button type="submit" variant="secondary" disabled={busy || !url.trim()}><Search />{busy ? "Working…" : "Inspect URL"}</Button>
        </form>
        <div class="space-y-2">
          <p class="text-xs font-medium text-text-muted">Import destination</p>
          <Select ariaLabel="Import destination" value={rootId} options={roots.map(root => ({ value: root.id, label: root.label }))} onchange={value => rootId = value}
            disabled={busy || !roots.length} placeholder="Choose a publication library" />
          {#if !roots.length}<p class="text-sm text-text-muted">Add an enabled library with book scanning in Settings to import publications.</p>{/if}
        </div>
      </Panel>
    {/if}
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if refreshError}<Alert.Root variant="destructive"><Alert.Description>{refreshError}</Alert.Description></Alert.Root>{/if}
    {#if inspection}
      <section class="space-y-3" aria-label="Inspected publications">
        <h2 class="text-base font-semibold">Choose a publication</h2>
        {#each inspection.warnings as warning}<Alert.Root><Alert.Description>{warning}</Alert.Description></Alert.Root>{/each}
        {#each inspection.items as item (item.id)}
          <Panel class="flex min-w-0 flex-wrap items-center justify-between gap-3 p-4">
            <h3 class="min-w-0 break-words text-sm font-semibold">{item.title}</h3>
            <Button variant="secondary" size="sm" disabled={busy || !rootId} onclick={() => void acquire(item.id)}><Download />Import publication</Button>
          </Panel>
        {/each}
      </section>
    {/if}
    <TransferList {transfers} onrefresh={refreshTransfers} />
  </div>
{/if}
