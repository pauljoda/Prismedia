<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Badge, Button, Panel } from "@prismedia/ui-svelte";
  import { MANAGED_TRACKING_STATUS } from "$lib/api/generated/codes";
  import type { ManagedLibraryItem, ManagedTrackingPreview, ManagedTrackingResponse } from "$lib/api/generated/model";
  import { fetchManagedTracking, previewTracking, saveManagedTracking, refreshTracking } from "$lib/api/managed-libraries";
  import ManagedHoldingControls from "./ManagedHoldingControls.svelte";
  import ManagedHoldingRelease from "./ManagedHoldingRelease.svelte";

  let { connectionId, item = null, showControls = false, canControl = false, canRelease = false, compact = false, onLoaded }: { connectionId: string; item?: ManagedLibraryItem | null; showControls?: boolean; canControl?: boolean; canRelease?: boolean; compact?: boolean; onLoaded?: (holdings: ManagedTrackingResponse[]) => void } = $props();
  let expandedId = $state<string | null>(null);
  let holdings = $state<ManagedTrackingResponse[]>([]);
  let preview = $state<ManagedTrackingPreview | null>(null);
  let error = $state<string | null>(null);
  let busy = $state(false);
  let operationId = "";
  let active = true;
  const visible = $derived(holdings.filter(holding => !item || holding.item.remoteId === item.remoteId && holding.item.entityKind === item.entityKind));
  const statusLabels = {
    [MANAGED_TRACKING_STATUS.pending]: "Waiting for verification",
    [MANAGED_TRACKING_STATUS.waitingForFiles]: "Waiting for first files",
    [MANAGED_TRACKING_STATUS.tracking]: "Tracking",
    [MANAGED_TRACKING_STATUS.needsReview]: "Needs review",
    [MANAGED_TRACKING_STATUS.stale]: "Connection unavailable",
    [MANAGED_TRACKING_STATUS.releasePending]: "Handoff pending",
    [MANAGED_TRACKING_STATUS.released]: "No longer managed",
  };
  onMount(() => {
    void load();
    const timer = setInterval(() => { if (!busy) void load(); }, 15000);
    return () => { active = false; clearInterval(timer); };
  });
  async function load() {
    try { const results = await fetchManagedTracking(connectionId); if (active) { holdings = results; error = null; onLoaded?.(results); } }
    catch (cause) { if (active) { error = cause instanceof Error ? cause.message : "Could not read tracked holdings"; onLoaded?.(holdings); } }
  }
  async function match() {
    if (!item) return;
    busy = true; error = null; preview = null;
    try {
      const result = await previewTracking(connectionId, { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds: item.externalIds });
      if (active) { preview = result; operationId = crypto.randomUUID(); }
    } catch (cause) { if (active) error = cause instanceof Error ? cause.message : "Could not match existing items"; }
    finally { if (active) busy = false; }
  }
  async function link() {
    if (!item || !preview?.libraryRootId || preview.reviewReason || !operationId) return;
    busy = true; error = null;
    try {
      const saved = await saveManagedTracking(connectionId, { operationId, libraryRootId: preview.libraryRootId,
        item: { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds: item.externalIds }, selections: preview.selections });
      if (active) { holdings = [...holdings.filter(holding => holding.id !== saved.id), saved]; preview = null; }
    } catch (cause) { if (active) error = cause instanceof Error ? cause.message : "Could not link this holding"; }
    finally { if (active) busy = false; }
  }
  async function refresh(id: string) {
    busy = true; error = null;
    try { await refreshTracking(connectionId, id); await load(); }
    catch (cause) { if (active) error = cause instanceof Error ? cause.message : "Could not queue refresh"; }
    finally { if (active) busy = false; }
  }
</script>

{#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
{#if item || visible.length}
  <Panel class="min-w-0 space-y-3 p-4">
    <h3 class="text-sm font-semibold">{item ? "Library link" : "Following in your library"}</h3>
    {#each visible as holding (holding.id)}
      <div class="space-y-2 border-b border-border-subtle pb-3 last:border-b-0 last:pb-0">
        <p class="break-words text-sm">{holding.title}</p>
        <div class="flex flex-wrap items-center gap-2"><Badge>{statusLabels[holding.status]}</Badge>
          <span class="text-xs text-text-muted">{#if holding.status === MANAGED_TRACKING_STATUS.released}Files and history retained{:else if holding.status === MANAGED_TRACKING_STATUS.releasePending}Ownership reserved until verification completes{:else if holding.status === MANAGED_TRACKING_STATUS.waitingForFiles}{holding.targets.length} requested {holding.targets.length === 1 ? "item" : "items"}{:else}{holding.bindings.filter(file => file.isAvailable).length} of {holding.bindings.length} linked files available{/if}</span></div>
        {#if compact}<Button variant="ghost" size="sm" aria-expanded={expandedId === holding.id} onclick={() => expandedId = expandedId === holding.id ? null : holding.id}>{expandedId === holding.id ? "Hide details" : "View details"}</Button>{/if}
        {#if !compact || expandedId === holding.id}
        {#if holding.problem}<p class="break-words text-sm text-text-muted">{holding.problem}</p>{/if}
        {#if holding.lastCheckedAt}<p class="text-xs text-text-muted">Checked {new Date(holding.lastCheckedAt).toLocaleString()}</p>{/if}
        {#if holding.status !== MANAGED_TRACKING_STATUS.released}<Button variant="outline" size="sm" disabled={busy} onclick={() => void refresh(holding.id)}>{holding.status === MANAGED_TRACKING_STATUS.releasePending ? "Refresh handoff" : "Refresh tracking"}</Button>{/if}
        {#if showControls}<ManagedHoldingControls {connectionId} holdingId={holding.id} canPreview={canControl && (holding.status === MANAGED_TRACKING_STATUS.tracking || holding.status === MANAGED_TRACKING_STATUS.waitingForFiles)} />{/if}
        {#if showControls && canRelease && (holding.status === MANAGED_TRACKING_STATUS.tracking || holding.status === MANAGED_TRACKING_STATUS.waitingForFiles)}
          <ManagedHoldingRelease {connectionId} holdingId={holding.id} onaccepted={saved => { holdings = holdings.map(item => item.id === saved.id ? saved : item); }} />
        {/if}
        {/if}
      </div>
    {/each}
    {#if item && !visible.some(holding => holding.status !== MANAGED_TRACKING_STATUS.released)}
      <p class="text-sm text-text-muted">Link this holding to its existing scanned items to retain your history across external renames and upgrades.</p>
      <Button variant="outline" size="sm" disabled={busy} onclick={match}>Match existing items</Button>
      {#if preview}
        {#if preview.reviewReason}<p role="status" class="text-sm text-text-muted">{preview.reviewReason}</p>
        {:else}
          <p class="text-sm">{preview.selections.length} existing {preview.selections.length === 1 ? "item matches" : "items match"} the reported files and numbering.</p>
          {#each [...new Set(preview.sources.map(source => source.localPath))].slice(0, 10) as path}<p class="break-all font-mono text-xs text-text-muted">{path}</p>{/each}
          <p class="text-xs text-text-muted">The external app keeps organizing these files. This mapped library will use tracked holdings for future scans; new or changed target coverage requires review.</p>
          <Button variant="secondary" disabled={busy} onclick={link}>Link existing items</Button>
        {/if}
      {/if}
    {/if}
  </Panel>
{/if}
