<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { ArrowLeft, ArrowRight, Library, RefreshCw, Search } from "@lucide/svelte";
  import { Alert, Button, DialogBase, Panel, Select, TextInput } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, ManagedLibraryItem, ManagedLibraryPage } from "$lib/api/generated/model";
  import { fetchManagedLibrary } from "$lib/api/managed-libraries";
  import ExternalLibraryMappings from "./ExternalLibraryMappings.svelte";
  import DiscoveryResults from "$lib/components/requests/DiscoveryResults.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { getEntityKindLabel } from "$lib/entities/entity-grid";
  import { connectionFeatureKinds, connectionSupports } from "$lib/integrations/connection-features";
  import { managedHoldingHref } from "$lib/integrations/managed-holding-route";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";

  let { connection, initialEntityKind = null }: { connection: ConnectionResponse; initialEntityKind?: EntityKind | null } = $props();
  const libraryKinds = $derived(connectionFeatureKinds(connection, "libraryBrowse"));
  let kind = $state<EntityKind | undefined>();
  let query = $state("");
  let activeQuery = $state<string | null>(null);
  let results = $state<ManagedLibraryPage | null>(null);
  let history = $state<ManagedLibraryPage[]>([]);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let settingsOpen = $state(false);
  let sequence = 0;
  let mounted = false;

  onMount(() => { mounted = true; kind = preferredKind(); void search(); return () => { mounted = false; sequence++; }; });
  $effect(() => {
    const requestedKind = initialEntityKind;
    if (!mounted) return;
    untrack(() => {
      const nextKind = preferredKind(requestedKind);
      if (!nextKind || nextKind === kind) return;
      kind = nextKind; results = null; history = []; void search();
    });
  });
  function preferredKind(requested = initialEntityKind) {
    return libraryKinds.find(item => item === requested) ?? libraryKinds[0];
  }
  async function search(cursor: string | null = null) {
    if (!kind) return;
    const current = ++sequence;
    loading = true; error = null;
    try {
      const result = await fetchManagedLibrary(connection.id, { entityKind: kind, query: cursor ? activeQuery : query.trim() || null, cursor, limit: 25 });
      if (current !== sequence) return;
      if (cursor && results) history = [...history, results];
      else { history = []; activeQuery = query.trim() || null; }
      results = result;
    } catch (cause) { if (current === sequence) error = cause instanceof Error ? cause.message : "Could not read the library"; }
    finally { if (current === sequence) loading = false; }
  }
  function openHolding(item: ManagedLibraryItem) {
    void goto(resolve(managedHoldingHref(connection.id, item) as "/"));
  }
  function back() { const previous = history.at(-1); if (previous) { results = previous; history = history.slice(0, -1); } }
</script>

{#if connection.status !== CONNECTION_STATUS.ready}
  <Alert.Root><Alert.Description>This source is unavailable. Your saved requests are in Activity. Test the connection in Settings to browse again.</Alert.Description></Alert.Root>
{/if}
<div class="flex flex-wrap items-center justify-between gap-3">
  <p class="text-sm text-text-muted">Browse titles already in {connection.name}. Open a title to view its files and library access.</p>
  {#if connectionSupports(connection, "managerOptions")}
    <Button variant="ghost" size="sm" onclick={() => settingsOpen = true}>Library settings</Button>
  {/if}
</div>
<Panel class="flex min-w-0 flex-col gap-3 p-4">
  {#if libraryKinds.length > 1}
    <Select ariaLabel="Library media type" value={kind} options={libraryKinds.map(value => ({ value, label: getEntityKindLabel(value) }))}
      onchange={value => { kind = libraryKinds.find(item => item === value); results = null; history = []; void search(); }} disabled={loading} />
  {/if}
  <form class="flex min-w-0 gap-2" onsubmit={event => { event.preventDefault(); void search(); }}>
    <TextInput aria-label="Search connected library" placeholder={`Search ${connection.name}…`} bind:value={query} maxlength={512} class="min-w-0 flex-1" disabled={loading} />
    <Button type="submit" variant="secondary" disabled={loading || !kind}><Search />Search</Button>
  </form>

</Panel>
{#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description><Alert.Action><Button variant="secondary" size="sm" onclick={() => void search()}>Retry</Button></Alert.Action></Alert.Root>{/if}
{#if loading}<StatePlaceholder icon={Library} title="Reading connected library" busy />
{:else if results}
  <div class="flex items-center justify-between gap-3">
    <h2 class="text-base font-semibold">In {connection.name}</h2>
    <Button variant="ghost" size="sm" onclick={() => void search()}><RefreshCw />Refresh</Button>
  </div>
  {#if !results.items.length}<StatePlaceholder icon={Library} title="No matching titles" description="Try another title or metadata ID." />{/if}
  <DiscoveryResults cards={results.items.map(item => entityReferenceToThumbnailCard({ id: item.remoteId, kind: item.entityKind, title: item.title, thumbnailUrl: item.presentation?.posterUrl }, {
    subtitle: [item.year, item.remoteFileCount == null ? "Availability unknown" : Number(item.remoteFileCount) > 0 ? "Files in source" : "Waiting for files"].filter(Boolean).join(" · "),
  }))} onActivate={card => { const item = results?.items.find(item => item.remoteId === card.entity.id); if (item) openHolding(item); }} />
  <div class="flex flex-wrap justify-between gap-2">
    {#if history.length}<Button variant="secondary" onclick={back}><ArrowLeft />Previous</Button>{/if}
    {#if results.nextCursor}<Button variant="secondary" onclick={() => void search(results?.nextCursor)}>Next<ArrowRight /></Button>{/if}
  </div>
{/if}

<DialogBase.Root open={settingsOpen} onOpenChange={value => settingsOpen = value}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    <DialogBase.Header><DialogBase.Title>Library settings · {connection.name}</DialogBase.Title>
      <DialogBase.Description>Connect the folders Prismedia can read. {connection.name} continues to organize its files.</DialogBase.Description></DialogBase.Header>
    {#if settingsOpen}<ExternalLibraryMappings {connection} {kind} />{/if}
    <DialogBase.Footer><Button variant="outline" onclick={() => settingsOpen = false}>Done</Button></DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
