<script lang="ts">
  import { page } from "$app/state";
  import { untrack } from "svelte";
  import { ArrowLeft, CircleAlert, Library, RefreshCw, ShieldAlert } from "@lucide/svelte";
  import { Alert, Button, buttonVariants } from "@prismedia/ui-svelte";
  import { INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type {
    ConnectionResponse,
    ManagedItemInput,
    ManagedItemSnapshot,
    ManagerOptions,
    MappedLibraryFile,
  } from "$lib/api/generated/model";
  import ManagedHoldingDetail from "$lib/components/integrations/ManagedHoldingDetail.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { fetchConnections } from "$lib/api/connections";
  import {
    fetchManagedItem,
    fetchManagerOptions,
    inspectLocalLibraryAccess,
  } from "$lib/api/managed-libraries";
  import { isEntityKindCode } from "$lib/entities/entity-codes";
  import {
    managedHoldingSourceHref,
    parseManagedHoldingIdentities,
  } from "$lib/integrations/managed-holding-route";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  interface HoldingRouteRequest {
    key: string;
    connectionId: string;
    input: ManagedItemInput | null;
  }

  const appChrome = useAppChrome();
  const session = useSession();
  const connectionId = $derived(page.params.connectionId ?? "");
  const sourceHref = $derived(managedHoldingSourceHref(connectionId, page.params.entityKind ?? ""));
  const routeRequest = $derived.by((): HoldingRouteRequest => {
    const kind = page.params.entityKind ?? "";
    const remoteId = page.params.remoteId ?? "";
    const rawIdentities = page.url.searchParams.get("identities");
    const expectedExternalIds = parseManagedHoldingIdentities(rawIdentities);
    const input = remoteId && expectedExternalIds && isEntityKindCode(kind)
      ? { entityKind: kind, remoteId, expectedExternalIds }
      : null;
    return {
      key: `${connectionId}\u0000${kind}\u0000${remoteId}\u0000${rawIdentities ?? ""}`,
      connectionId,
      input,
    };
  });

  let connection = $state<ConnectionResponse | null>(null);
  let detail = $state<ManagedItemSnapshot | null>(null);
  let options = $state<ManagerOptions | null>(null);
  let localFiles = $state<MappedLibraryFile[] | null>(null);
  let loading = $state(true);
  let checking = $state(false);
  let loadError = $state<string | null>(null);
  let availabilityError = $state<string | null>(null);
  let loadSequence = 0;
  let checkSequence = 0;
  let loadedKey: string | null = null;

  const showControls = $derived(connection?.enabledCapabilities.includes(PLUGIN_CAPABILITY.externalManager) ?? false);
  const canControl = $derived(connection?.effectiveCapabilities.some((capability) =>
    capability.kind === PLUGIN_CAPABILITY.externalManager
      && capability.operations.includes(INTEGRATION_OPERATION.reconcileManaged),
  ) ?? false);
  const canRelease = $derived(connection?.effectiveCapabilities.some((capability) =>
    capability.kind === PLUGIN_CAPABILITY.externalManager
      && capability.operations.includes(INTEGRATION_OPERATION.inspectManagedRelease),
  ) ?? false);

  $effect(() => {
    const request = routeRequest;
    if (!session.isAdmin) {
      loadSequence += 1;
      checkSequence += 1;
      connection = null;
      detail = null;
      options = null;
      localFiles = null;
      loadError = null;
      availabilityError = null;
      loading = false;
      checking = false;
      loadedKey = null;
      return;
    }

    untrack(() => { void load(request); });
    return () => {
      loadSequence += 1;
      checkSequence += 1;
    };
  });

  $effect(() => {
    return appChrome.setBreadcrumbs([
      { label: "Request", href: "/request" },
      { label: connection?.name ?? "Connected source", href: sourceHref },
      { label: detail?.item.title ?? "Title" },
    ]);
  });

  async function load(request: HoldingRouteRequest) {
    const current = ++loadSequence;
    const preserveCurrent = loadedKey === request.key && detail !== null && connection !== null;
    checkSequence += 1;
    loading = !preserveCurrent;
    checking = preserveCurrent && (detail?.files.length ?? 0) > 0;
    loadError = null;
    availabilityError = null;
    localFiles = null;
    if (!preserveCurrent) {
      connection = null;
      detail = null;
      options = null;
      loadedKey = null;
    }

    if (!request.input) {
      connection = null;
      detail = null;
      options = null;
      loadedKey = null;
      loadError = "This title link is incomplete. Return to the connected source and open the title again.";
      loading = false;
      checking = false;
      return;
    }

    try {
      const connections = await fetchConnections();
      if (current !== loadSequence || request.key !== routeRequest.key) return;
      const selectedConnection = connections.find((candidate) => candidate.id === request.connectionId) ?? null;
      if (!selectedConnection) throw new Error("This connected source is no longer available.");
      connection = selectedConnection;

      const supportsOptions = selectedConnection.effectiveCapabilities.some((capability) =>
        capability.kind === PLUGIN_CAPABILITY.externalManager
          && capability.operations.includes(INTEGRATION_OPERATION.managerOptions),
      );
      const snapshotPromise = fetchManagedItem(selectedConnection.id, request.input);
      const optionsPromise = supportsOptions
        ? fetchManagerOptions(selectedConnection.id, request.input.entityKind).catch(() => null)
        : Promise.resolve(null);
      const snapshot = await snapshotPromise;
      if (current !== loadSequence || request.key !== routeRequest.key) return;
      connection = selectedConnection;
      detail = snapshot;
      loadedKey = request.key;
      loading = false;
      if (snapshot.files.length > 0) void inspectAvailability(request, selectedConnection);
      else checking = false;

      const managerOptions = await optionsPromise;
      if (current === loadSequence && request.key === routeRequest.key) options = managerOptions;
    } catch (cause) {
      if (current === loadSequence && request.key === routeRequest.key) {
        loadError = cause instanceof Error ? cause.message : "Could not read this title from the connected source.";
        checking = false;
      }
    } finally {
      if (current === loadSequence && request.key === routeRequest.key) loading = false;
    }
  }

  async function inspectAvailability(request: HoldingRouteRequest, selectedConnection: ConnectionResponse) {
    if (!request.input) return;
    const current = ++checkSequence;
    checking = true;
    availabilityError = null;
    localFiles = null;
    try {
      const result = await inspectLocalLibraryAccess(selectedConnection.id, request.input);
      if (current === checkSequence && request.key === routeRequest.key) {
        detail = result.remote;
        loadedKey = request.key;
        localFiles = result.files;
      }
    } catch (cause) {
      if (current === checkSequence && request.key === routeRequest.key) {
        availabilityError = cause instanceof Error ? cause.message : "The availability check could not be completed.";
      }
    } finally {
      if (current === checkSequence && request.key === routeRequest.key) checking = false;
    }
  }
</script>

<svelte:head><title>{detail?.item.title ?? "Connected title"} · Request · Prismedia</title></svelte:head>

{#if !session.isAdmin}
  <StatePlaceholder
    icon={ShieldAlert}
    title="Administrator access required"
    description="Connected source libraries and their file mappings are available to administrators."
  >
    <a class={buttonVariants({ variant: "secondary", size: "sm" })} href="/request"><ArrowLeft />Back to Request</a>
  </StatePlaceholder>
{:else}
  <div class="space-y-5">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <a class={buttonVariants({ variant: "ghost", size: "sm" })} href={sourceHref}>
        <ArrowLeft />Back to {connection?.name ?? "source"}
      </a>
      {#if !loading}
        <Button variant="ghost" size="sm" onclick={() => void load(routeRequest)}><RefreshCw />Refresh</Button>
      {/if}
    </div>

    {#if loadError}
      <Alert.Root variant="destructive"><CircleAlert /><Alert.Description>{loadError}</Alert.Description></Alert.Root>
    {/if}
    {#if loading && !detail}
      <StatePlaceholder icon={Library} title="Reading title from source" description="Loading its details and reported files." busy />
    {:else if detail && connection}
      {#key routeRequest.key}
        <ManagedHoldingDetail
          {connection}
          {detail}
          {options}
          {localFiles}
          {checking}
          {availabilityError}
          {showControls}
          {canControl}
          {canRelease}
          onRecheckAvailability={() => void inspectAvailability(routeRequest, connection!)}
        />
      {/key}
    {:else if !loadError}
      <StatePlaceholder icon={Library} title="Title unavailable" description="Return to the source and choose another title." />
    {/if}
  </div>
{/if}
