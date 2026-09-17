<script lang="ts">
  import { onMount } from "svelte";
  import { Activity, ArrowUpRight } from "@lucide/svelte";
  import { Alert, Select, buttonVariants } from "@prismedia/ui-svelte";
  import { INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, IntegrationTransferResponse } from "$lib/api/generated/model";
  import { fetchIntegrationTransfers } from "$lib/api/integration-transfers";
  import ManagedHoldingTracking from "$lib/components/integrations/ManagedHoldingTracking.svelte";
  import ManagedRequests from "$lib/components/integrations/ManagedRequests.svelte";
  import TransferList from "$lib/components/integrations/TransferList.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";

  let { connections }: { connections: ConnectionResponse[] } = $props();
  let connectionId = $state("");
  let transfers = $state<IntegrationTransferResponse[]>([]);
  let error = $state<string | null>(null);
  let loading = $state(true);
  let holdingsByConnection = $state<Record<string, string[]>>({});
  let alive = true;
  const visibleConnections = $derived(connections.filter(item => (!connectionId || item.id === connectionId) && item.enabledCapabilities.includes(PLUGIN_CAPABILITY.connectedLibrary)));
  const visibleTransfers = $derived(transfers.filter(item => !connectionId || item.connectionId === connectionId));
  onMount(() => {
    void refresh();
    const timer = setInterval(() => void refresh(), 10000);
    return () => { alive = false; clearInterval(timer); };
  });
  async function refresh() {
    try { const result = await fetchIntegrationTransfers(); if (alive) { transfers = result; error = null; } }
    catch (cause) { if (alive) error = cause instanceof Error ? cause.message : "Could not refresh activity"; }
    finally { if (alive) loading = false; }
  }
</script>

<div class="space-y-5">
  <div class="flex flex-wrap items-end justify-between gap-3">
    <div class="space-y-1"><h2 class="text-lg font-semibold">Requests & imports</h2><p class="text-sm text-text-muted">Follow downloads, review problems, and open completed items.</p></div>
    <a class={buttonVariants({ variant: "ghost", size: "sm" })} href="/downloads">Download queue<ArrowUpRight /></a>
  </div>
  <div class="max-w-sm"><Select ariaLabel="Activity source" value={connectionId}
    options={[{ value: "", label: "All sources" }, ...connections.map(item => ({ value: item.id, label: item.name }))]} onchange={value => connectionId = value} /></div>
  {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
  {#if loading}<StatePlaceholder icon={Activity} title="Loading activity" busy />{/if}
  <TransferList transfers={visibleTransfers} onrefresh={refresh} />
  {#each visibleConnections as connection (connection.id)}
    <section class="space-y-3" aria-label={connection.name}>
      <h3 class="pt-3 text-base font-semibold">{connection.name}</h3>
      <ManagedHoldingTracking compact connectionId={connection.id} showControls={connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.externalManager)}
        canControl={connection.effectiveCapabilities.some(item => item.operations.includes(INTEGRATION_OPERATION.reconcileManaged))}
        canRelease={connection.effectiveCapabilities.some(item => item.operations.includes(INTEGRATION_OPERATION.inspectManagedRelease))}
        onLoaded={holdings => holdingsByConnection = { ...holdingsByConnection, [connection.id]: holdings.map(item => item.id) }} />
      {#if holdingsByConnection[connection.id]}
        <ManagedRequests {connection} excludeIds={holdingsByConnection[connection.id]} />
      {/if}
    </section>
  {/each}
  {#if !loading && !error && !visibleTransfers.length && !visibleConnections.length}<StatePlaceholder icon={Activity} title="Nothing to follow yet" description="Choose a title in Browse to start a request or import." />{/if}
</div>
