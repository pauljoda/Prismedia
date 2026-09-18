<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { Library, Search } from "@lucide/svelte";
  import { Alert, Button, DialogBase, Panel, Tabs, TextInput } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, ManagedDiscoverySearchResponse } from "$lib/api/generated/model";
  import { searchManagerTitles } from "$lib/api/managed-discovery";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import DiscoveryResults from "$lib/components/requests/DiscoveryResults.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { requestKindForEntityKind } from "$lib/requests/request-helpers";
  import ConnectedLibraryBrowser from "./ConnectedLibraryBrowser.svelte";
  import ExternalLibraryMappings from "./ExternalLibraryMappings.svelte";

  let { connection, initialEntityKind = null }: {
    connection: ConnectionResponse;
    initialEntityKind?: EntityKind | null;
  } = $props();
  // These are local view labels, not server state codes.
  const findTab = "Find new titles";
  const libraryTab = "In your library";
  let tab = $state(findTab);
  const support = $derived(connection.effectiveCapabilities.find(capability =>
    capability.kind === PLUGIN_CAPABILITY.externalManager
    && capability.operations.includes(INTEGRATION_OPERATION.discoverManaged)));
  const kind = $derived(support?.entityKinds.find(value => value === initialEntityKind) ?? support?.entityKinds[0]);
  const canBrowseLibrary = $derived(connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.connectedLibrary)
    && connection.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.connectedLibrary
      && capability.operations.includes(INTEGRATION_OPERATION.searchLibrary)));
  let query = $state(untrack(() => page.url.searchParams.get("managerQuery") ?? ""));
  let searchedQuery = $state("");
  let results = $state<ManagedDiscoverySearchResponse | null>(null);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let settingsOpen = $state(false);
  let sequence = 0;

  onMount(() => {
    if (query.trim()) void search();
    return () => { sequence++; };
  });

  async function search() {
    const term = query.trim();
    if (!kind || !term || connection.status !== CONNECTION_STATUS.ready) return;
    const current = ++sequence;
    loading = true;
    error = null;
    results = null;
    searchedQuery = term;
    try {
      const found = await searchManagerTitles(connection.id, { entityKind: kind, query: term, limit: 50 });
      if (current === sequence) results = found;
    } catch (cause) {
      if (current === sequence) error = cause instanceof Error ? cause.message : "Could not search this source";
    } finally {
      if (current === sequence) loading = false;
    }
  }

  function openReview(index: number) {
    const item = results?.items[index];
    if (!item) return;
    const requestKind = requestKindForEntityKind(item.entityKind);
    if (!requestKind) return;
    const back = new URLSearchParams(page.url.searchParams);
    back.set("connection", connection.id);
    back.set("managerQuery", searchedQuery);
    const review = new URLSearchParams({
      connection: connection.id,
      namespace: item.externalIdentity.namespace,
      back: back.toString(),
    });
    void goto(resolve(`/request/${requestKind}/${encodeURIComponent(item.externalIdentity.value)}?${review}` as "/"));
  }
</script>

<Tabs.Root value={tab} onValueChange={value => tab = value}>
  <Tabs.List variant="line" aria-label={`${connection.name} browse mode`}>
    <Tabs.Trigger value={findTab}><Search />{findTab}</Tabs.Trigger>
    {#if canBrowseLibrary}<Tabs.Trigger value={libraryTab}><Library />{libraryTab}</Tabs.Trigger>{/if}
  </Tabs.List>
  <Tabs.Content value={findTab} class="space-y-5 pt-5">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-text-muted">Search {connection.name}, review a title, then let it find and organize your files.</p>
      <Button variant="ghost" size="sm" onclick={() => settingsOpen = true}>Library settings</Button>
    </div>
    {#if connection.status !== CONNECTION_STATUS.ready}
      <Alert.Root><Alert.Description>This source is unavailable. Check its connection status in Settings.</Alert.Description></Alert.Root>
    {/if}
    <Panel class="p-4">
      <form class="flex min-w-0 gap-2" onsubmit={event => { event.preventDefault(); void search(); }}>
        <TextInput aria-label={`Find new titles in ${connection.name}`} placeholder="Search for a title…" bind:value={query} maxlength={512} class="min-w-0 flex-1" disabled={loading} />
        <Button type="submit" variant="secondary" disabled={loading || !query.trim() || !kind || connection.status !== CONNECTION_STATUS.ready}><Search />Search</Button>
      </form>
    </Panel>
    {#if error}
      <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description><Alert.Action><Button variant="secondary" size="sm" onclick={() => void search()}>Retry</Button></Alert.Action></Alert.Root>
    {:else if loading}
      <StatePlaceholder icon={Search} title={`Searching ${connection.name}`} busy />
    {:else if results?.items.length}
      <div class="flex items-baseline justify-between gap-3"><h2 class="text-lg font-semibold">Search results</h2><span class="font-mono text-xs text-text-muted">{results.items.length} titles</span></div>
      <DiscoveryResults cards={results.items.map((item, index) => entityReferenceToThumbnailCard({
        id: String(index), kind: item.entityKind, title: item.title, thumbnailUrl: item.metadata?.posterUrl,
      }, { subtitle: [item.year].filter(Boolean).join(" · ") }))} onActivate={card => openReview(Number(card.entity.id))} />
    {:else if results}
      <StatePlaceholder icon={Search} title="No matching titles" description="Try another title or include the release year." />
    {:else}
      <StatePlaceholder icon={Search} title="What would you like to add?" description={`Find a new title through ${connection.name}. Review its details before requesting it.`} />
    {/if}
  </Tabs.Content>
  {#if canBrowseLibrary}
    <Tabs.Content value={libraryTab} class="space-y-5 pt-5">
      {#if tab === libraryTab}<ConnectedLibraryBrowser {connection} {initialEntityKind} />{/if}
    </Tabs.Content>
  {/if}
</Tabs.Root>

<DialogBase.Root open={settingsOpen} onOpenChange={value => settingsOpen = value}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    <DialogBase.Header><DialogBase.Title>Library settings · {connection.name}</DialogBase.Title>
      <DialogBase.Description>Connect the folders Prismedia can read. {connection.name} continues to organize its files.</DialogBase.Description></DialogBase.Header>
    {#if settingsOpen}<ExternalLibraryMappings {connection} {kind} />{/if}
    <DialogBase.Footer><Button variant="outline" onclick={() => settingsOpen = false}>Done</Button></DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
