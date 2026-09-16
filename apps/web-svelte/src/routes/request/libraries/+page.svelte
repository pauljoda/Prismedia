<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Library, ShieldUser } from "@lucide/svelte";
  import { Alert, Select, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { useSession } from "$lib/stores/session.svelte";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import ConnectedLibraryBrowser from "$lib/components/integrations/ConnectedLibraryBrowser.svelte";
  const session = useSession();
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let loading = $state(true);
  let error = $state<string | null>(null);
  const connection = $derived(connections.find(item => item.id === connectionId));
  onMount(() => { if (session.isAdmin) void initialize(); else loading = false; });
  async function initialize() {
    try {
      connections = (await fetchConnections()).filter(item => item.status === CONNECTION_STATUS.ready
        && item.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.connectedLibrary
          && capability.operations.includes(INTEGRATION_OPERATION.searchLibrary) && capability.operations.includes(INTEGRATION_OPERATION.getLibraryItem)));
      connectionId = connections.find(item => item.id === page.url.searchParams.get("connection"))?.id ?? connections[0]?.id ?? "";
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not load connected libraries"; }
    finally { loading = false; }
  }
</script>
<svelte:head><title>Connected libraries · Prismedia</title></svelte:head>
{#if !session.isAdmin}
  <StatePlaceholder icon={ShieldUser} title="Administrator access required" description="Connected libraries are managed by a server administrator." />
{:else}
  <div class="flex min-w-0 flex-col gap-4">
    <header class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div class="space-y-2"><BackLink fallback="/request" label="Requests" /><h1 class="flex items-center gap-2.5"><Library class="size-5 text-text-accent" />Connected libraries</h1><p class="text-sm text-text-muted">Explore the collections already managed by your connected applications.</p></div>
      <a class={buttonVariants({ variant: "secondary", size: "sm" })} href="/settings/connections">Manage connections</a>
    </header>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if loading}<StatePlaceholder icon={Library} title="Loading connections" busy />
    {:else if !connections.length}<StatePlaceholder icon={Library} title="Connect an existing library" description="Install a compatible plugin, then add and test its connection in Settings." />
    {:else}
      <Select ariaLabel="Library connection" value={connectionId} options={connections.map(item => ({ value: item.id, label: item.name }))} onchange={value => connectionId = value} />
      {#if connection}{#key connection.id}<ConnectedLibraryBrowser {connection} />{/key}{/if}
    {/if}
  </div>
{/if}
