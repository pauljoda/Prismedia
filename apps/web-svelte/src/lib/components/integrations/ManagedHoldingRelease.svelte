<script lang="ts">
  import { onDestroy } from "svelte";
  import { Alert, Button, Checkbox, Panel } from "@prismedia/ui-svelte";
  import type { ManagedReleasePreview, ManagedTrackingResponse, ReleaseManagedHoldingRequest } from "$lib/api/generated/model";
  import { fetchReleasePreview, ManagedReleaseRejectedError, saveOwnershipRelease } from "$lib/api/managed-release";
  let { connectionId, holdingId, onaccepted }: { connectionId: string; holdingId: string; onaccepted: (holding: ManagedTrackingResponse) => void } = $props();
  let preview = $state<ManagedReleasePreview | null>(null);
  let pending = $state<ReleaseManagedHoldingRequest | null>(null);
  let acknowledged = $state(false);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let alive = true;
  onDestroy(() => { alive = false; });
  async function review() {
    busy = true; error = null; preview = null; acknowledged = false;
    try { const result = await fetchReleasePreview(connectionId, holdingId); if (alive) preview = result; }
    catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function release() {
    if (!pending && (!preview?.canRelease || !acknowledged)) return;
    if (!pending && preview) pending = { operationId: crypto.randomUUID(), expectedRevision: preview.revision,
      scopeFingerprint: preview.scopeFingerprint, expectedPath: preview.observation.state.path };
    busy = true; error = null;
    try {
      const saved = await saveOwnershipRelease(connectionId, holdingId, pending!);
      if (alive) { pending = null; preview = null; onaccepted(saved); }
    } catch (cause) {
      if (alive) { error = message(cause); if (cause instanceof ManagedReleaseRejectedError) { pending = null; preview = null; } }
    } finally { if (alive) busy = false; }
  }
  function message(cause: unknown) { return cause instanceof Error ? cause.message : "Could not complete the ownership handoff"; }
</script>

<div class="flex min-w-0 flex-col gap-3">
  {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
  {#if !pending}<Button variant="outline" size="sm" disabled={busy} onclick={review}>Review ownership handoff</Button>{/if}
  {#if preview}
    <Panel class="flex min-w-0 flex-col gap-3 p-3">
      <h4 class="text-sm font-semibold">Release acquisition owner</h4>
      <p class="break-words text-sm font-medium">{preview.observation.state.item.title}</p>
      <p class="text-sm text-text-muted">Prismedia will stop this holding's requests and file tracking. Files, library items and your history are retained. You can choose another acquisition owner after the handoff completes.</p>
      <div class="flex flex-col gap-1 text-xs text-text-muted">
        <p>{preview.observation.state.targets.some(target => target.monitored) ? "Monitoring is still enabled for selected items." : `Monitoring is off for ${preview.observation.state.targets.length} selected ${preview.observation.state.targets.length === 1 ? "item" : "items"}.`}</p>
        <p>{preview.observation.queueEmpty ? "The connected application's download queue is empty." : "The connected application has download activity."}</p>
        <p>{preview.observation.commandsIdle ? "No active commands were reported." : "Commands are running or their state is unverified."}</p>
      </div>
      {#if preview.problem}<Alert.Root><Alert.Description>{preview.problem}</Alert.Description></Alert.Root>{/if}
      {#if preview.canRelease}
        <p class="text-xs text-text-muted">Prismedia checks again before releasing ownership. Keep monitoring off and avoid starting work in the connected app during handoff.</p>
        <label class="flex items-start gap-3 text-sm"><Checkbox aria-label="I will keep this scope unmonitored in the connected app" checked={acknowledged} onchange={value => acknowledged = value} disabled={busy || !!pending} />I will keep this scope unmonitored in the connected app</label>
        <Button variant="secondary" disabled={busy || !acknowledged} onclick={release}>{pending ? "Retry same handoff" : "Release acquisition owner"}</Button>
      {/if}
    </Panel>
  {/if}
</div>
