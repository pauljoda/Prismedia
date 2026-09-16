<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { AlertTriangle, ArrowLeft, ArrowRight, BookOpen, Download, FolderOpen, Search, ShieldUser } from "@lucide/svelte";
  import { Alert, Badge, Button, Panel, Select, TextInput, buttonVariants } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, DiscoveryItemResponse, DiscoveryPageResponse, EntityKind, IntegrationTransferResponse, LibraryRoot } from "$lib/api/generated/model";
  import { fetchConnections, fetchConnectionCatalog } from "$lib/api/connections";
  import { acquirePublication, fetchIntegrationTransfers } from "$lib/api/integration-transfers";
  import { fetchLibraryRoots } from "$lib/api/settings";
  import TransferList from "$lib/components/integrations/TransferList.svelte";
  import { isTransferTerminal } from "$lib/integrations/transfer-labels";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { getEntityKindLabel } from "$lib/entities/entity-grid";
  import { acquisitionAccessLabels, publicationFormatLabel, canImportPublication } from "$lib/integrations/catalog-labels";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let kind = $state<EntityKind | undefined>();
  let query = $state("");
  let activeQuery = $state<string | null>(null);
  let container = $state<string | null>(null);
  let catalog = $state<DiscoveryPageResponse | null>(null);
  let history = $state<Array<{ catalog: DiscoveryPageResponse; container: string | null; query: string | null }>>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let roots = $state<LibraryRoot[]>([]);
  let rootId = $state("");
  let transfers = $state<IntegrationTransferResponse[]>([]);
  let transferError = $state<string | null>(null);
  let refreshError = $state<string | null>(null);
  let submitting = $state(false);
  const operations = new Map<string, string>();
  let requestSequence = 0;
  const connection = $derived(connections.find(item => item.id === connectionId));
  const support = $derived(connection?.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery));
  const canSearch = $derived(support?.operations.includes(INTEGRATION_OPERATION.search) ?? false);
  const canBrowse = $derived(support?.operations.includes(INTEGRATION_OPERATION.browse) ?? false);


  onMount(() => {
    if (session.isAdmin) void initialize(); else loading = false;
    const timer = setInterval(() => {
      if (session.isAdmin && transfers.some(item => !isTransferTerminal(item.phase))) void refreshTransfers();
    }, 5000);
    return () => clearInterval(timer);
  });
  async function refreshTransfers() {
    try { transfers = await fetchIntegrationTransfers(); refreshError = null; }
    catch (cause) { refreshError = cause instanceof Error ? cause.message : "Could not refresh imports"; }
  }
  async function acquire(item: DiscoveryItemResponse, offerId: string) {
    if (!rootId || submitting) return;
    const key = JSON.stringify([connectionId, item.selectionToken, offerId, rootId]);
    const operationId = operations.get(key) ?? crypto.randomUUID();
    operations.set(key, operationId);
    submitting = true; transferError = null;
    try {
      const accepted = await acquirePublication(connectionId, { operationId, selectionToken: item.selectionToken, offerId, libraryRootId: rootId });
      transfers = [accepted, ...transfers.filter(transfer => transfer.id !== accepted.id)];
    } catch (cause) { transferError = cause instanceof Error ? cause.message : "Could not accept this publication. Retry to check the same request."; }
    finally { submitting = false; }
  }
  async function initialize() {
    loading = true; error = null;
    try {
      const [available, libraries] = await Promise.all([fetchConnections(), fetchLibraryRoots()]);
      roots = libraries.filter(root => root.enabled && root.scanBooks);
      rootId = roots[0]?.id ?? "";
      await refreshTransfers();
      connections = available.filter(item => item.status === CONNECTION_STATUS.ready
        && item.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.catalogDiscovery
          && capability.operations.some(operation => operation === INTEGRATION_OPERATION.browse || operation === INTEGRATION_OPERATION.search)));
      const requested = page.url.searchParams.get("connection");
      await chooseConnection(connections.find(item => item.id === requested)?.id ?? connections[0]?.id ?? "");
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not load catalogs"; }
    finally { loading = false; }
  }
  async function chooseConnection(id: string) {
    requestSequence++; connectionId = id;
    kind = connections.find(item => item.id === id)?.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery)?.entityKinds[0];
    query = ""; activeQuery = null; container = null; history = []; catalog = null;
    if (kind && connections.find(item => item.id === id)?.effectiveCapabilities.some(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery && item.operations.includes(INTEGRATION_OPERATION.browse))) await browse();
  }
  async function chooseKind(value: string) {
    kind = support?.entityKinds.find(item => item === value); history = []; container = null; catalog = null;
    if (canBrowse) await browse();
  }
  async function browse(nextContainer: string | null = null, nextQuery: string | null = null, cursor: string | null = null, remember = false) {
    if (!kind || !connectionId) return;
    const sequence = ++requestSequence;
    loading = true; error = null;
    try {
      const result = await fetchConnectionCatalog(connectionId, { entityKind: kind, query: nextQuery, container: nextContainer, cursor, limit: 25 });
      if (sequence !== requestSequence) return;
      if (remember && catalog) history = [...history, { catalog, container, query: activeQuery }];
      catalog = result; container = nextContainer; activeQuery = nextQuery;
    } catch (cause) { if (sequence === requestSequence) error = cause instanceof Error ? cause.message : "Could not load catalog"; }
    finally { if (sequence === requestSequence) loading = false; }
  }
  function back() {
    const previous = history.at(-1);
    if (!previous) return;
    history = history.slice(0, -1); catalog = previous.catalog; container = previous.container; activeQuery = previous.query; query = previous.query ?? ""; error = null;
  }
</script>

<svelte:head><title>Catalogs · Prismedia</title></svelte:head>

{#if !session.isAdmin}
  <StatePlaceholder icon={ShieldUser} title="Administrator access required" description="Catalog connections are managed by a server administrator." />
{:else}
  <div class="flex min-w-0 flex-col gap-5">
    <header class="flex flex-wrap items-end justify-between gap-3">
      <div class="space-y-2">
        <BackLink fallback="/request" label="Requests" />
        <h1 class="flex items-center gap-2.5"><BookOpen class="size-5 text-text-accent" />Catalogs</h1>
        <p class="text-sm text-text-muted">Explore publications available from your connected sources.</p>
      </div>
      <a class={buttonVariants({ variant: "secondary", size: "sm" })} href="/settings/connections">Manage connections</a>
    </header>
    {#if connections.length}
      <Panel class="flex flex-col gap-3 p-4">
        <div class="grid min-w-0 gap-3 sm:grid-cols-2">
          <Select ariaLabel="Catalog connection" value={connectionId} options={connections.map(item => ({ value: item.id, label: item.name }))}
            onchange={value => void chooseConnection(value)} disabled={loading} />
          <Select ariaLabel="Media type" value={kind} options={(support?.entityKinds ?? []).map(value => ({ value, label: getEntityKindLabel(value) }))}
            onchange={value => void chooseKind(value)} disabled={loading} />
        </div>
        {#if canSearch}
          <form class="flex min-w-0 gap-2" onsubmit={event => { event.preventDefault(); history = []; void browse(null, query.trim() || null); }}>
            <TextInput aria-label="Search catalog" placeholder="Search this catalog…" bind:value={query} maxlength={512} class="min-w-0 flex-1" disabled={loading} />
            <Button type="submit" variant="secondary" disabled={loading || (!query.trim() && !canBrowse)}><Search />Search</Button>
          </form>
        {/if}
        <div class="space-y-2">
          <p class="text-xs font-medium text-text-muted">Import destination</p>
          <Select ariaLabel="Import destination" value={rootId} options={roots.map(root => ({ value: root.id, label: root.label }))}
            onchange={value => rootId = value} disabled={submitting || !roots.length} placeholder="Choose a publication library" />
          {#if !roots.length}<p class="text-sm text-text-muted">Add an enabled library with book scanning in Settings to import publications.</p>{/if}
        </div>
      </Panel>
    {/if}
    {#if error}
      <Alert.Root variant="destructive"><AlertTriangle /><Alert.Description>{error}</Alert.Description></Alert.Root>
    {/if}
    {#if transferError}<Alert.Root variant="destructive"><Alert.Description>{transferError}</Alert.Description></Alert.Root>{/if}
    {#if refreshError}<Alert.Root variant="destructive"><Alert.Description>{refreshError}</Alert.Description></Alert.Root>{/if}
    <TransferList {transfers} onrefresh={refreshTransfers} />
    {#if loading}
      <StatePlaceholder icon={BookOpen} title="Loading catalog" busy />
    {:else if !connections.length}
      <StatePlaceholder icon={BookOpen} title="Connect a catalog" description="Add and test a connection with discovery support to browse its publications." />
    {:else if catalog}
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex min-w-0 items-center gap-3">
          {#if history.length}<Button variant="ghost" size="sm" onclick={back}><ArrowLeft />Back</Button>{/if}
          <h2 class="break-words text-base font-semibold">{catalog.title}</h2>
        </div>
        {#if (container || activeQuery) && canBrowse}<Button variant="ghost" size="sm" onclick={() => { history = []; query = ""; void browse(); }}>Catalog home</Button>{/if}
      </div>
      {#if !catalog.items.length}
        <StatePlaceholder icon={BookOpen} title="No publications on this page" description="Try another media type, search, or the next page if available." />
      {:else}
        <div class="flex flex-col gap-3">
          {#each catalog.items as item (item.id)}
            <Panel class="p-4">
              <article class="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div class="min-w-0 space-y-2">
                  <h3 class="break-words text-sm font-semibold">{item.publication.title}</h3>
                  {#if item.publication.authors.length}<p class="text-sm text-text-muted">{item.publication.authors.join(", ")}</p>{/if}
                  {#if item.publication.description}<p class="line-clamp-3 break-words text-sm text-text-muted">{item.publication.description}</p>{/if}
                  {#if item.publication.publisher || item.publication.language}<p class="text-xs text-text-muted">{[item.publication.publisher, item.publication.language].filter(Boolean).join(" · ")}</p>{/if}
                  {#if item.offers.length}
                    <div class="flex flex-wrap gap-2">{#each item.offers as offer (offer.id)}<Badge>{acquisitionAccessLabels[offer.access]}{publicationFormatLabel(offer.mediaType) ? ` · ${publicationFormatLabel(offer.mediaType)}` : ""}</Badge>{/each}</div>
                  {/if}
                </div>
                {#if !item.isContainer}
                  <div class="flex shrink-0 flex-wrap gap-2 self-start">
                    {#each item.offers.filter(offer => canImportPublication(item.entityKind, offer)) as offer (offer.id)}
                      <Button variant="secondary" size="sm" disabled={!rootId || submitting} onclick={() => void acquire(item, offer.id)}><Download />Import {publicationFormatLabel(offer.mediaType)}</Button>
                    {/each}
                  </div>
                {/if}
                {#if item.isContainer}<Button variant="secondary" size="sm" onclick={() => void browse(item.selectionToken, null, null, true)} class="self-start sm:shrink-0"><FolderOpen />Open</Button>{/if}
              </article>
            </Panel>
          {/each}
        </div>
      {/if}
      {#if catalog.nextCursor}<Button variant="secondary" class="self-end" onclick={() => void browse(container, activeQuery, catalog?.nextCursor, true)}>Next page<ArrowRight /></Button>{/if}
    {/if}
  </div>
{/if}
