<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Badge, Button, Checkbox, Panel, Select } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, MANAGED_REQUEST_PHASE, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, CreateManagedRequestInput, ExternalLibraryMount, ManagedRequestPreview, ManagedRequestResponse } from "$lib/api/generated/model";
  import { fetchEntities } from "$lib/api/entities";
  import { fetchLibraryMounts } from "$lib/api/managed-libraries";
  import { cancelRequest, fetchManagedRequestPreview, fetchManagedRequests, ManagedRequestRejectedError, refreshRequest, saveManagedRequest } from "$lib/api/managed-requests";
  import EntityPicker, { type EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
  import ManagedHoldingControls from "./ManagedHoldingControls.svelte";

  let { connection, initialEntity = null }: { connection: ConnectionResponse; initialEntity?: EntityPickerItem | null } = $props();
  const canRequest = $derived(connection.enabled && connection.status === CONNECTION_STATUS.ready && connection.effectiveCapabilities.some(capability =>
    capability.kind === PLUGIN_CAPABILITY.externalManager && capability.entityKinds.includes(ENTITY_KIND.movie)
    && capability.operations.includes(INTEGRATION_OPERATION.lookupManaged) && capability.operations.includes(INTEGRATION_OPERATION.ensureManaged)));
  let requests = $state<ManagedRequestResponse[]>([]);
  const visibleRequests = $derived(initialEntity ? requests.filter(request => request.entityId === initialEntity.id) : requests);
  let mounts = $state<ExternalLibraryMount[]>([]);
  let selected = $state<EntityPickerItem[]>([]);
  let libraryRootId = $state("");
  let preview = $state<ManagedRequestPreview | null>(null);
  let pending = $state<CreateManagedRequestInput | null>(null);
  let expanded = $state(false);
  let busy = $state(false);
  let profileId = $state("");
  let monitored = $state(false);
  let search = $state(true);
  let error = $state<string | null>(null);
  let alive = true;
  let sequence = 0;
  const phaseLabels = {
    [MANAGED_REQUEST_PHASE.pendingCreation]: "Request queued",
    [MANAGED_REQUEST_PHASE.creationUncertain]: "Creation outcome uncertain",
    [MANAGED_REQUEST_PHASE.awaitingFiles]: "Waiting for files",
    [MANAGED_REQUEST_PHASE.completed]: "Imported into Prismedia",
    [MANAGED_REQUEST_PHASE.rejected]: "Creation refused",
    [MANAGED_REQUEST_PHASE.cancelled]: "Cancelled",
    [MANAGED_REQUEST_PHASE.ownershipReleased]: "Ownership released",
  };
  const activePhases = new Set<ManagedRequestResponse["phase"]>([MANAGED_REQUEST_PHASE.pendingCreation, MANAGED_REQUEST_PHASE.creationUncertain, MANAGED_REQUEST_PHASE.awaitingFiles]);
  onMount(() => {
    if (initialEntity) { selected = [initialEntity]; void open(); }
    void load();
    const timer = setInterval(() => { if (!busy) void load(); }, 5000);
    return () => { alive = false; sequence++; clearInterval(timer); };
  });
  async function load() {
    const current = ++sequence;
    try {
      const result = await fetchManagedRequests(connection.id);
      if (!alive || current !== sequence) return;
      requests = result;
      if (initialEntity && result.some(item => item.entityId === initialEntity.id && item.phase !== MANAGED_REQUEST_PHASE.cancelled && item.phase !== MANAGED_REQUEST_PHASE.ownershipReleased)) expanded = false;
      if (pending && result.some(item => item.id === pending?.operationId)) { pending = null; preview = null; selected = []; error = null; if (initialEntity) expanded = false; }
    } catch (cause) { if (alive && current === sequence) error = message(cause); }
  }
  async function open() {
    expanded = !expanded;
    if (!expanded || mounts.length) return;
    busy = true; error = null;
    try {
      const result = await fetchLibraryMounts(connection.id);
      if (alive) { mounts = result; libraryRootId = mounts[0]?.libraryRootId ?? ""; }
    } catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function findWanted(query: string): Promise<EntityPickerItem[]> {
    const found = await fetchEntities({ kind: ENTITY_KIND.movie, wanted: true, hasFile: false, query, limit: 20 });
    return found.items.map(item => ({ id: item.id, title: item.title, thumbnailUrl: item.coverThumbUrl, subtitle: item.subtitle ?? undefined }));
  }
  async function review() {
    if (!selected[0] || !libraryRootId) return;
    busy = true; error = null; preview = null;
    try {
      const result = await fetchManagedRequestPreview(connection.id, { entityId: selected[0].id, libraryRootId });
      if (alive) { preview = result; profileId = result.existing?.item.profileId ?? result.options.profiles[0]?.id ?? ""; monitored = result.existing?.item.monitored ?? false; search = true; }
    } catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function submit() {
    if (!pending && (!preview || !profileId)) return;
    if (!pending && preview) pending = { operationId: crypto.randomUUID(), entityId: preview.entityId, libraryRootId: preview.mount.libraryRootId,
      reviewedWork: preview.work, profileId, monitored, search };
    busy = true; error = null; sequence++;
    try {
      const saved = await saveManagedRequest(connection.id, pending!);
      if (alive) { requests = [saved, ...requests.filter(item => item.id !== saved.id)]; pending = null; preview = null; selected = []; if (initialEntity) expanded = false; }
    } catch (cause) {
      if (alive) { error = message(cause); if (cause instanceof ManagedRequestRejectedError) { pending = null; preview = null; } }
    } finally { if (alive) busy = false; }
  }
  async function refresh(request: ManagedRequestResponse) {
    busy = true; error = null;
    try { await refreshRequest(connection.id, request.id); await load(); }
    catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function cancel(request: ManagedRequestResponse) {
    busy = true; error = null; sequence++;
    try { const saved = await cancelRequest(connection.id, request); if (alive) requests = requests.map(item => item.id === saved.id ? saved : item); }
    catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  function message(cause: unknown) { return cause instanceof Error ? cause.message : "The manager request could not be completed"; }
</script>

{#if canRequest || requests.length || error}
  <Panel class="min-w-0 space-y-4 p-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-sm font-semibold">Requests through {connection.name}</h2>
      {#if canRequest && !initialEntity}<Button variant="outline" size="sm" onclick={open} disabled={busy} aria-expanded={expanded}>{expanded ? "Hide request form" : "Request a wanted movie"}</Button>{/if}
    </div>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if expanded && canRequest}
      <div class="space-y-3">
        <p class="text-sm text-text-muted">Choose a wanted movie with an identified TMDB record. The connected app will own its acquisition and file organization.</p>
        {#if !mounts.length && !busy}<p class="text-sm text-text-muted">Map and enable a video library in Connected libraries before requesting a movie.</p>{/if}
        {#if initialEntity}<p class="text-sm font-medium">{initialEntity.title}</p>
        {:else}<EntityPicker label="Wanted movie" mode="single" values={selected} onChange={values => { selected = values; preview = null; }} onSearch={findWanted} disabled={busy || !!pending} placeholder="Find a wanted movie" />{/if}
        <Select ariaLabel="Mapped library" value={libraryRootId} options={mounts.map(mount => ({ value: mount.libraryRootId, label: mount.label }))}
          disabled={busy || !!pending} onchange={value => { libraryRootId = value; preview = null; }} />
        <Button variant="outline" size="sm" disabled={busy || !!pending || !selected.length || !libraryRootId} onclick={review}>Review manager request</Button>
        {#if preview}
          <div class="space-y-3 border-t border-border-subtle pt-3">
            <p class="break-words text-sm font-medium">{preview.title}</p>
            <p class="text-sm text-text-muted">{preview.existing ? "This movie already exists in the connected app. Its current profile and location will be retained." : "The movie will be added to the selected external library."}</p>
            <Select ariaLabel="Request profile" value={profileId} options={preview.options.profiles.map(profile => ({ value: profile.id, label: profile.label }))}
              disabled={busy || !!pending || !!preview.existing} onchange={value => profileId = value} />
            <label class="flex items-center gap-3 text-sm"><Checkbox aria-label="Monitor this movie" checked={monitored} disabled={busy || !!pending} onchange={value => monitored = value} />Monitor this movie in {connection.name}</label>
            <label class="flex items-center gap-3 text-sm"><Checkbox aria-label="Search now" checked={search} disabled={busy || !!pending} onchange={value => search = value} />Search now</label>
            <p class="text-xs text-text-muted">A completed search can find no release. This request stays waiting until Prismedia can read the imported file.</p>
            <Button variant="secondary" disabled={busy || !profileId} onclick={submit}>{pending ? "Retry same request" : "Request through manager"}</Button>
          </div>
        {/if}
      </div>
    {/if}
    {#each visibleRequests as request (request.id)}
      <article class="space-y-2 border-t border-border-subtle pt-3">
        <p class="break-words text-sm font-medium">{request.title}</p>
        <Badge>{request.reviewRequired ? "Needs review" : phaseLabels[request.phase]}</Badge>
        {#if request.problem}<p class="break-words text-sm text-text-muted">{request.problem}</p>{/if}
        <div class="flex flex-wrap gap-2">
          {#if activePhases.has(request.phase)}<Button variant="outline" size="sm" disabled={busy} onclick={() => void refresh(request)}>Refresh request</Button>{/if}
          {#if request.canCancel}<Button variant="outline" size="sm" disabled={busy} onclick={() => void cancel(request)}>Cancel request</Button>{/if}
        </div>
        {#if request.remoteId}<ManagedHoldingControls connectionId={connection.id} holdingId={request.id} canPreview={canRequest && request.phase !== MANAGED_REQUEST_PHASE.ownershipReleased} />{/if}
      </article>
    {/each}
  </Panel>
{/if}
