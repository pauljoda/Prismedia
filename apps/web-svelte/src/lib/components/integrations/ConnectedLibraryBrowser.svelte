<script lang="ts">
  import { onMount } from "svelte";
  import { ArrowLeft, ArrowRight, FolderOpen, Library, RefreshCw, Search } from "@lucide/svelte";
  import { Alert, Badge, Button, DialogBase, Panel, Select, TextInput } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, EntityKind, ManagedItemSnapshot, ManagedLibraryItem, ManagedLibraryPage, ManagerOptions, MappedLibraryFile } from "$lib/api/generated/model";
  import { fetchManagedItem, fetchManagedLibrary, fetchManagerOptions, inspectLocalLibraryAccess } from "$lib/api/managed-libraries";
  import ExternalLibraryMappings from "./ExternalLibraryMappings.svelte";
  import ManagedHoldingTracking from "./ManagedHoldingTracking.svelte";
  import { getEntityKindLabel } from "$lib/entities/entity-grid";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";

  let { connection }: { connection: ConnectionResponse } = $props();
  const support = $derived(connection.effectiveCapabilities.find(item => item.kind === PLUGIN_CAPABILITY.connectedLibrary));
  const showControls = $derived(connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.externalManager));
  const canControl = $derived(connection.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.externalManager && capability.operations.includes(INTEGRATION_OPERATION.reconcileManaged)));
  let kind = $state<EntityKind | undefined>();
  let query = $state("");
  let activeQuery = $state<string | null>(null);
  let results = $state<ManagedLibraryPage | null>(null);
  let history = $state<ManagedLibraryPage[]>([]);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let options = $state<ManagerOptions | null>(null);
  let detail = $state<ManagedItemSnapshot | null>(null);
  let detailOpen = $state(false);
  let detailLoading = $state(false);
  let detailError = $state<string | null>(null);
  let visibleFiles = $state(50);
  let localFiles = $state<MappedLibraryFile[] | null>(null);
  let sequence = 0;
  let detailSequence = 0;

  onMount(() => { kind = support?.entityKinds[0]; void search(); return () => { sequence++; detailSequence++; }; });
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
  async function inspect(item: ManagedLibraryItem) {
    const current = ++detailSequence;
    detailOpen = true; detailLoading = true; detailError = null; detail = null; options = null; visibleFiles = 50; localFiles = null;
    try {
      const result = await fetchManagedItem(connection.id, { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds: item.externalIds });
      if (current !== detailSequence) return;
      detail = result;
      if (connection.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.externalManager && capability.operations.includes(INTEGRATION_OPERATION.managerOptions))) {
        const choices = await fetchManagerOptions(connection.id, item.entityKind);
        if (current === detailSequence) options = choices;
      }
    } catch (cause) { if (current === detailSequence) detailError = cause instanceof Error ? cause.message : "Could not read the holding"; }
    finally { if (current === detailSequence) detailLoading = false; }
  }
  function back() { const previous = history.at(-1); if (previous) { results = previous; history = history.slice(0, -1); } }
  async function checkLocalAccess() {
    if (!detail) return;
    const current = ++detailSequence;
    detailLoading = true; detailError = null;
    try {
      const result = await inspectLocalLibraryAccess(connection.id, { entityKind: detail.item.entityKind, remoteId: detail.item.remoteId, expectedExternalIds: detail.item.externalIds });
      if (current === detailSequence) { detail = result.remote; localFiles = result.files; }
    } catch (cause) { if (current === detailSequence) detailError = cause instanceof Error ? cause.message : "Could not check local access"; }
    finally { if (current === detailSequence) detailLoading = false; }
  }
</script>

<ManagedHoldingTracking connectionId={connection.id} {showControls} {canControl} />
{#if connection.status !== CONNECTION_STATUS.ready}
  <Alert.Root><Alert.Description>Test this connection in Settings to resume remote observations. Saved tracking remains available here.</Alert.Description></Alert.Root>
{/if}

{#if connection.effectiveCapabilities.some(capability => capability.kind === PLUGIN_CAPABILITY.externalManager && capability.operations.includes(INTEGRATION_OPERATION.managerOptions))}
  <ExternalLibraryMappings {connection} {kind} />
{/if}
<Panel class="flex min-w-0 flex-col gap-3 p-4">
  {#if (support?.entityKinds.length ?? 0) > 1}
    <Select ariaLabel="Library media type" value={kind} options={(support?.entityKinds ?? []).map(value => ({ value, label: getEntityKindLabel(value) }))}
      onchange={value => { kind = support?.entityKinds.find(item => item === value); results = null; history = []; void search(); }} disabled={loading} />
  {/if}
  <form class="flex min-w-0 gap-2" onsubmit={event => { event.preventDefault(); void search(); }}>
    <TextInput aria-label="Search connected library" placeholder="Search existing holdings…" bind:value={query} maxlength={512} class="min-w-0 flex-1" disabled={loading} />
    <Button type="submit" variant="secondary" disabled={loading}><Search />Search</Button>
  </form>
  <p class="text-xs text-text-muted">Searches holdings in {connection.name}. File availability here is reported by the connected application.</p>
</Panel>
{#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description><Alert.Action><Button variant="secondary" size="sm" onclick={() => void search()}>Retry</Button></Alert.Action></Alert.Root>{/if}
{#if loading}<StatePlaceholder icon={Library} title="Reading connected library" busy />
{:else if results}
  <div class="flex items-center justify-between gap-3">
    <h2 class="text-base font-semibold">Existing holdings</h2>
    <Button variant="ghost" size="sm" onclick={() => void search()}><RefreshCw />Refresh</Button>
  </div>
  {#if !results.items.length}<StatePlaceholder icon={Library} title="No matching holdings" description="Try another title or metadata ID." />{/if}
  {#each results.items as item (item.remoteId)}
    <Panel class="p-4">
      <article class="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0 space-y-2">
          <h3 class="break-words text-sm font-semibold">{item.title}{item.year ? ` (${item.year})` : ""}</h3>
          <div class="flex flex-wrap gap-2"><Badge>{item.monitored ? "Monitored" : "Not monitored"}</Badge><Badge>{item.remoteFileCount == null ? "File count unknown" : `${item.remoteFileCount} ${item.remoteFileCount === 1 ? "file" : "files"} reported`}</Badge></div>
        </div>
        <Button variant="secondary" size="sm" class="self-start sm:shrink-0" onclick={() => void inspect(item)}><FolderOpen />Inspect holding</Button>
      </article>
    </Panel>
  {/each}
  <div class="flex flex-wrap justify-between gap-2">
    {#if history.length}<Button variant="secondary" onclick={back}><ArrowLeft />Previous</Button>{/if}
    {#if results.nextCursor}<Button variant="secondary" onclick={() => void search(results?.nextCursor)}>Next<ArrowRight /></Button>{/if}
  </div>
{/if}

<DialogBase.Root open={detailOpen} onOpenChange={value => { detailOpen = value; if (!value) detailSequence++; }}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    <DialogBase.Header>
      <DialogBase.Title>{detail?.item.title ?? "Connected holding"}</DialogBase.Title>
      <DialogBase.Description>Files reported by {connection.name}. {localFiles ? "Local checks confirm readability and size; importing into the library is a separate step." : "Local access has not been checked."}</DialogBase.Description>
    </DialogBase.Header>
    {#if detailError}<p role="alert" class="text-sm text-error-text">{detailError}</p>{/if}
    {#if detailLoading}<p role="status" class="text-sm text-text-muted">Reading file associations and profiles…</p>{/if}
    {#if detail}
      <div class="space-y-2 text-sm text-text-muted">
        <p>Observed {new Date(detail.observedAt).toLocaleString()}</p>
        <p>Profile: {options?.profiles.find(profile => profile.id === detail?.item.profileId)?.label ?? detail.item.profileId ?? "Unknown"}</p>
        <p class="break-all">Remote folder: {detail.path}</p>
        <Button variant="outline" size="sm" disabled={detailLoading} onclick={checkLocalAccess}>Check local access</Button>
      </div>
      <div class="divide-y divide-border-subtle">
        {#each detail.files.slice(0, visibleFiles) as file (file.remoteId)}
          {@const local = localFiles?.find(candidate => candidate.remoteId === file.remoteId)}
          <div class="space-y-2 py-3">
            <p class="break-all font-mono text-xs">{file.path}</p>
            <p class="text-xs text-text-muted">{(Number(file.sizeBytes) / 1024 / 1024).toFixed(1)} MiB · {file.targets.length} content {file.targets.length === 1 ? "target" : "targets"}</p>
            <p class="break-words text-sm text-text-muted">{file.targets.slice(0, 6).map(target => target.seasonNumber != null ? `S${target.seasonNumber} E${target.episodeNumber}: ${target.title}` : target.title).join(" · ")}{file.targets.length > 6 ? ` · ${file.targets.length - 6} more` : ""}</p>
            {#if local}<p class="text-xs text-text-muted">{local.isReadable && local.sizeMatches ? "Readable locally · size matches" : local.problem}</p>{/if}
          </div>
        {/each}
        {#if !detail.files.length}<p class="py-3 text-sm text-text-muted">No final files are currently associated with this holding.</p>{/if}
      </div>
      {#if detail.files.length > visibleFiles}<Button variant="secondary" onclick={() => visibleFiles += 50}>Show more files</Button>{/if}
      {#key detail.item.remoteId}<ManagedHoldingTracking connectionId={connection.id} item={detail.item} {showControls} {canControl} />{/key}
    {/if}
    <DialogBase.Footer><Button variant="outline" onclick={() => { detailOpen = false; detailSequence++; }}>Done</Button></DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
