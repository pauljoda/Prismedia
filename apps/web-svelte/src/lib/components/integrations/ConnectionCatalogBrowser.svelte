<script lang="ts">
  import { fetchConnectionCatalog } from "$lib/api/connections";
  import { onMount, untrack } from "svelte";

  import { ArrowLeft, ArrowRight, BookOpen, Download, FolderOpen, Search } from "@lucide/svelte";
  import { Alert, Badge, Button, DialogBase, Panel, Select, TextInput } from "@prismedia/ui-svelte";
  import { INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, DiscoveryItemResponse, DiscoveryPageResponse, EntityKind, LibraryRoot } from "$lib/api/generated/model";

  import { acquirePublication } from "$lib/api/integration-transfers";
  import { fetchLibraryRoots } from "$lib/api/settings";

  import SourceAttribution from "$lib/components/integrations/SourceAttribution.svelte";
  import { integrationImportRoots } from "$lib/integrations/import-options";

  import DiscoveryResults from "$lib/components/requests/DiscoveryResults.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { getEntityKindLabel } from "$lib/entities/entity-grid";
  import { acquisitionAccessLabels, publicationFormatLabel, canImportPublication } from "$lib/integrations/catalog-labels";

  let { connection, initialEntityKind = null }: { connection: ConnectionResponse; initialEntityKind?: EntityKind | null } = $props();
  const connectionId = $derived(connection.id);
  let selected = $state<DiscoveryItemResponse | null>(null);
  let acceptedTitle = $state<string | null>(null);
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

  let transferError = $state<string | null>(null);

  let submitting = $state(false);
  const operations = new Map<string, string>();
  let requestSequence = 0;
  let initialized = $state(false);

  const support = $derived(connection?.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.catalogDiscovery));
  const canSearch = $derived(support?.operations.includes(INTEGRATION_OPERATION.search) ?? false);
  const canBrowse = $derived(support?.operations.includes(INTEGRATION_OPERATION.browse) ?? false);
  const destinations = $derived(integrationImportRoots(roots, kind));

  onMount(() => { void initialize(); return () => { requestSequence++; }; });
  $effect(() => {
    const requestedKind = initialEntityKind;
    if (!initialized) return;
    untrack(() => {
      const nextKind = preferredKind(requestedKind);
      if (!nextKind || nextKind === kind) return;
      void chooseKind(nextKind);
    });
  });
  async function acquire(item: DiscoveryItemResponse, offerId: string) {
    if (!rootId || submitting) return;
    const key = JSON.stringify([connectionId, item.selectionToken, offerId, rootId]);
    const operationId = operations.get(key) ?? crypto.randomUUID();
    operations.set(key, operationId);
    submitting = true; transferError = null;
    try {
      const accepted = await acquirePublication(connectionId, { operationId, selectionToken: item.selectionToken, offerId, libraryRootId: rootId });
      acceptedTitle = accepted.title; selected = null;
    } catch (cause) { transferError = cause instanceof Error ? cause.message : "Could not accept this item. Retry to check the same request."; }
    finally { submitting = false; }
  }
  async function initialize() {
    loading = true;
    try {
      roots = await fetchLibraryRoots();
      kind = preferredKind();
      chooseDestination();
      if (canBrowse) await browse();
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not load this source"; }
    finally { loading = false; initialized = true; }
  }
  function preferredKind(requested = initialEntityKind) {
    return support?.entityKinds.find(item => item === requested) ?? support?.entityKinds[0];
  }
  async function chooseKind(value: string) {
    requestSequence++; loading = false; query = ""; activeQuery = null;
    kind = support?.entityKinds.find(item => item === value); history = []; container = null; catalog = null;
    chooseDestination();
    if (canBrowse) await browse();
  }
  function chooseDestination() {
    const compatible = integrationImportRoots(roots, kind);
    if (!compatible.some(root => root.id === rootId)) rootId = compatible[0]?.id ?? "";
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
    requestSequence++; loading = false;
    history = history.slice(0, -1); catalog = previous.catalog; container = previous.container; activeQuery = previous.query; query = previous.query ?? ""; error = null;
  }
</script>
<div class="space-y-5">
  <Panel class="space-y-3 p-4">
    {#if (support?.entityKinds.length ?? 0) > 1}
      <Select ariaLabel="Media type" value={kind} options={(support?.entityKinds ?? []).map(value => ({ value, label: getEntityKindLabel(value) }))}
        onchange={value => void chooseKind(value)} disabled={loading} />
    {/if}
    {#if canSearch}
      <form class="flex min-w-0 gap-2" onsubmit={event => { event.preventDefault(); history = []; void browse(null, query.trim() || null); }}>
        <TextInput aria-label="Search source" placeholder={`Search ${connection.name}…`} bind:value={query} maxlength={512} class="min-w-0 flex-1" disabled={loading} />
        <Button type="submit" disabled={loading || (!query.trim() && !canBrowse)}><Search />Search</Button>
      </form>
    {/if}
    <p class="text-xs text-text-muted">Choose a title to view available formats and add it to your library.</p>
  </Panel>
  {#if acceptedTitle}<Alert.Root role="status"><Alert.Description>Import started for {acceptedTitle}. <a class="underline" href="/request?activity">Follow progress in Activity</a>.</Alert.Description></Alert.Root>{/if}
  {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description><Alert.Action><Button variant="outline" size="sm" onclick={() => void browse(container, activeQuery)}>Retry</Button></Alert.Action></Alert.Root>{/if}
  {#if loading}<StatePlaceholder icon={BookOpen} title="Loading titles" busy />
  {:else if catalog}
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex min-w-0 items-center gap-3">
        {#if history.length}<Button variant="ghost" size="sm" onclick={back}><ArrowLeft />Back</Button>{/if}
        <h2 class="break-words text-base font-semibold">{catalog.title}</h2>
        <span class="font-mono text-xs text-text-muted">{catalog.items.length} items</span>
      </div>
      {#if (container || activeQuery) && canBrowse}<Button variant="ghost" size="sm" onclick={() => { history = []; query = ""; void browse(); }}>Browse all</Button>{/if}
    </div>
    {#if !catalog.items.length}<StatePlaceholder icon={BookOpen} title="No matching titles" description="Try another search or browse another section." />{/if}
    {#if catalog.items.some(item => item.isContainer)}
      <div class="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {#each catalog.items.filter(item => item.isContainer) as item (item.id)}
          <Button variant="outline" class="h-auto justify-start gap-3 py-4 text-left whitespace-normal" onclick={() => void browse(item.selectionToken, null, null, true)}><FolderOpen class="shrink-0" />{item.publication.title}<ArrowRight class="ml-auto shrink-0" /></Button>
        {/each}
      </div>
    {/if}
    <DiscoveryResults cards={catalog.items.filter(item => !item.isContainer).map(item => entityReferenceToThumbnailCard({ id: item.id, kind: item.entityKind, title: item.publication.title }, {
      subtitle: item.publication.authors.join(", ") || item.publication.editionLabel || item.offers.map(offer => publicationFormatLabel(offer.mediaType)).filter(Boolean).join(" · "),
    }))} onActivate={card => { selected = catalog?.items.find(item => item.id === card.entity.id) ?? null; transferError = null; }} />
    {#if catalog.nextCursor}<div class="flex justify-end"><Button variant="secondary" onclick={() => void browse(container, activeQuery, catalog?.nextCursor, true)}>Next page<ArrowRight /></Button></div>{/if}
  {:else if !error}<StatePlaceholder icon={Search} title={`Search ${connection.name}`} description="Enter a title or keyword to find something to add to your library." />{/if}
</div>

<DialogBase.Root open={!!selected} onOpenChange={open => { if (!open && !submitting) selected = null; }}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
    <DialogBase.Header><DialogBase.Title>{selected?.publication.title ?? "Add to library"}</DialogBase.Title>
      <DialogBase.Description>Choose a format and the library to save it in.</DialogBase.Description></DialogBase.Header>
    {#if selected}
      {#if selected.publication.authors.length}<p class="text-sm text-text-muted">{selected.publication.authors.join(", ")}</p>{/if}
      {#if selected.publication.description}<p class="text-sm text-text-muted whitespace-pre-line">{selected.publication.description}</p>{/if}
      {#if selected.publication.publisher || selected.publication.language}<p class="text-xs text-text-muted">{[selected.publication.publisher, selected.publication.language].filter(Boolean).join(" · ")}</p>{/if}
      {#if selected.publication.attribution}<SourceAttribution attribution={selected.publication.attribution} />{/if}
      <div class="flex flex-wrap gap-2">{#each selected.offers as offer (offer.id)}<Badge>{acquisitionAccessLabels[offer.access]} · {publicationFormatLabel(offer.mediaType)}</Badge>{/each}</div>
      {#if selected.offers.some(offer => canImportPublication(selected!.entityKind, offer))}
        <label class="space-y-2 text-sm">Save to library
          <Select ariaLabel="Import destination" value={rootId} options={destinations.map(root => ({ value: root.id, label: root.label }))}
            onchange={value => rootId = value} disabled={submitting || !destinations.length} placeholder="Choose a library" />
        </label>
        {#if !destinations.length}<p class="text-sm text-text-muted">Add an enabled library for this media type in <a href="/settings/libraries" class="underline">Settings</a> first.</p>{/if}
      {:else}<p class="text-sm text-text-muted">This source does not offer a direct download for this item.</p>{/if}
      {#if transferError}<Alert.Root variant="destructive"><Alert.Description>{transferError}</Alert.Description></Alert.Root>{/if}
      <DialogBase.Footer>
        <Button variant="outline" disabled={submitting} onclick={() => selected = null}>Close</Button>
        {#each selected.offers.filter(offer => canImportPublication(selected!.entityKind, offer)) as offer (offer.id)}
          <Button disabled={!rootId || submitting} onclick={() => { if (selected) void acquire(selected, offer.id); }}><Download />Import {publicationFormatLabel(offer.mediaType)}</Button>
        {/each}
      </DialogBase.Footer>
    {/if}
  </DialogBase.Content>
</DialogBase.Root>
