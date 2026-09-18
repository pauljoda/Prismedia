<script lang="ts">
  import { onDestroy } from "svelte";
  import { CircleStop } from "@lucide/svelte";
  import { Alert, Button, Checkbox, DialogBase } from "@prismedia/ui-svelte";
  import type { ManagedReleasePreview, ManagedTrackingResponse, ReleaseManagedHoldingRequest } from "$lib/api/generated/model";
  import { fetchReleasePreview, ManagedReleaseRejectedError, saveOwnershipRelease } from "$lib/api/managed-release";
  let {
    connectionId,
    holdingId,
    onaccepted,
    connectionName = "Connected app",
  }: {
    connectionId: string;
    holdingId: string;
    onaccepted: (holding: ManagedTrackingResponse) => void;
    connectionName?: string;
  } = $props();
  let open = $state(false);
  let preview = $state<ManagedReleasePreview | null>(null);
  let pending = $state<ReleaseManagedHoldingRequest | null>(null);
  let acknowledged = $state(false);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let alive = true;
  onDestroy(() => { alive = false; });
  function setOpen(value: boolean) {
    if (!value && busy) return;
    open = value;
    if (value && !pending) {
      void review();
    } else if (!value && !pending) {
      preview = null;
      acknowledged = false;
      error = null;
    }
  }
  async function review() {
    busy = true; error = null; preview = null; acknowledged = false;
    try { const result = await fetchReleasePreview(connectionId, holdingId); if (alive) preview = result; }
    catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function release() {
    if (!pending && (!preview?.canRelease || !acknowledged)) return;
    if (!pending && preview) {
      const remoteItemAbsent = preview.observation.remoteItemAbsent === true;
      pending = {
        operationId: crypto.randomUUID(),
        expectedRevision: preview.revision,
        scopeFingerprint: preview.scopeFingerprint,
        expectedPath: remoteItemAbsent ? null : preview.observation.state!.path,
        remoteItemAbsent,
      };
    }
    busy = true; error = null;
    try {
      const saved = await saveOwnershipRelease(connectionId, holdingId, pending!);
      if (alive) { pending = null; preview = null; open = false; onaccepted(saved); }
    } catch (cause) {
      if (alive) { error = message(cause); if (cause instanceof ManagedReleaseRejectedError) { pending = null; preview = null; } }
    } finally { if (alive) busy = false; }
  }
  function message(cause: unknown) { return cause instanceof Error ? cause.message : "Could not stop Prismedia management"; }
</script>

<div class="min-w-0">
  <Button variant="ghost" size="sm" aria-label={`Stop managing this title with ${connectionName}`} onclick={() => setOpen(true)}>
    <CircleStop />Stop managing…
  </Button>
</div>

<DialogBase.Root {open} onOpenChange={setOpen}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
    <DialogBase.Header>
      <DialogBase.Title>Stop managing with {connectionName}?</DialogBase.Title>
      <DialogBase.Description>Prismedia will stop its requests and file tracking for this title. Existing files, library items, and history remain where they are.</DialogBase.Description>
    </DialogBase.Header>
    <div class="space-y-4">
      {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
      {#if busy && !preview}<p class="text-sm text-text-muted">Checking monitoring, queue, and command activity in {connectionName}…</p>{/if}
      {#if preview}
      <div class="space-y-4">
      {#if preview.observation.remoteItemAbsent}
        <p class="text-sm text-text-muted">{connectionName} no longer reports this title. Prismedia retained its metadata and history; any local files remain in place.</p>
        <p class="text-sm text-text-muted">Stopping management lets you request this title through another source. It does not delete retained library data or local files.</p>
      {:else if preview.observation.state}
        <p class="break-words text-sm font-medium">{preview.observation.state.item.title}</p>
        <p class="text-sm text-text-muted">This does not transfer, copy, move, or delete files. {connectionName} can continue organizing its files. A different acquisition owner can be selected only after Prismedia finishes stopping this association.</p>
      {/if}
      <div class="flex flex-col gap-1 text-xs text-text-muted">
        {#if preview.observation.state}
          <p>{preview.observation.state.targets.some(target => target.monitored) ? "Monitoring is still enabled for selected items." : `Monitoring is off for ${preview.observation.state.targets.length} selected ${preview.observation.state.targets.length === 1 ? "item" : "items"}.`}</p>
        {/if}
        <p>{preview.observation.queueEmpty ? `${connectionName}'s download queue is empty.` : `${connectionName} has download activity.`}</p>
        <p>{preview.observation.commandsIdle ? "No active commands were reported." : "Commands are running or their state is unverified."}</p>
      </div>
      {#if preview.problem}<Alert.Root><Alert.Description>{preview.problem}</Alert.Description></Alert.Root>{/if}
      {#if preview.observation.state?.targets.some(target => target.monitored)}
        <p class="text-sm text-text-muted">Turn off monitoring in Settings & activity, then check again.</p>
      {/if}
      {#if preview.canRelease}
        {#if preview.observation.remoteItemAbsent}
          <p class="text-xs text-text-muted">Prismedia checks that the title remains absent and remote activity stays idle before finishing.</p>
          <label class="flex items-start gap-3 text-sm"><Checkbox aria-label="I understand Prismedia will stop managing this title" checked={acknowledged} onchange={value => acknowledged = value} disabled={busy || !!pending} />I understand Prismedia will stop managing this title.</label>
        {:else}
          <p class="text-xs text-text-muted">Prismedia checks these conditions again before finishing. Keep monitoring off when choosing a different acquisition owner.</p>
          <label class="flex items-start gap-3 text-sm"><Checkbox aria-label={`I will keep this title unmonitored in ${connectionName}`} checked={acknowledged} onchange={value => acknowledged = value} disabled={busy || !!pending} />I will keep this title unmonitored in {connectionName}.</label>
        {/if}
      {/if}
      </div>
      {/if}
    </div>
    <DialogBase.Footer>
      <Button variant="outline" disabled={busy} onclick={() => setOpen(false)}>{preview?.canRelease ? "Cancel" : "Close"}</Button>
      {#if preview?.canRelease}
        <Button variant="secondary" disabled={busy || !acknowledged} onclick={release}>{pending ? "Retry same stop request" : "Stop Prismedia management"}</Button>
      {:else if !busy && !pending}
        <Button variant="secondary" disabled={busy} onclick={review}>Check again</Button>
      {/if}
    </DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
