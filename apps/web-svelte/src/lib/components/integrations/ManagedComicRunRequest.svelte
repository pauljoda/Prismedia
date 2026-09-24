<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { CircleAlert, LoaderCircle } from "@lucide/svelte";
  import { Alert, Button, DialogBase, Select } from "@prismedia/ui-svelte";
  import type { CommitManagedComicRunInput, ExternalIdentity, ReviewedManagedComicRun } from "$lib/api/generated/model";
  import { addManagedComicRun, fetchManagedComicRunReview } from "$lib/api/managed-comic-runs";
  import { ManagedRequestRejectedError } from "$lib/api/managed-requests";
  import { managedHoldingInputHref } from "$lib/integrations/managed-holding-route";
  import { createUuid } from "$lib/utils/uuid";

  let { connectionId, connectionName, identity, onclose }: {
    connectionId: string;
    connectionName: string;
    identity: ExternalIdentity;
    onclose: () => void;
  } = $props();

  let review = $state<ReviewedManagedComicRun | null>(null);
  let mountId = $state("");
  let pending = $state<CommitManagedComicRunInput | null>(null);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let alive = true;

  onMount(() => {
    void loadReview();
    return () => { alive = false; };
  });

  async function loadReview() {
    busy = true;
    error = null;
    try {
      const fresh = await fetchManagedComicRunReview(connectionId, { identity });
      if (alive) {
        review = fresh;
        if (!fresh.mounts.some(mount => mount.id === mountId)) mountId = fresh.mounts[0]?.id ?? "";
      }
    } catch (cause) {
      if (alive) error = cause instanceof Error ? cause.message : "Could not review this comic run.";
    } finally {
      if (alive) busy = false;
    }
  }

  function openHolding(item: NonNullable<ReviewedManagedComicRun["existing"]>) {
    void goto(resolve(managedHoldingInputHref(connectionId, item) as "/"));
  }

  async function submit() {
    if (!review || !mountId || busy) return;
    pending ??= {
      operationId: createUuid(),
      expectedConnectionRevision: review.connectionRevision,
      identity,
      expectedTitle: review.candidate.title,
      mountId,
    };
    busy = true;
    error = null;
    try {
      const result = await addManagedComicRun(connectionId, pending);
      if (alive) {
        pending = null;
        openHolding(result.item);
      }
    } catch (cause) {
      if (alive) {
        error = cause instanceof Error ? cause.message : "Could not confirm the run. Retry to check the same exact title.";
        if (cause instanceof ManagedRequestRejectedError) { pending = null; review = null; }
      }
    } finally {
      if (alive) busy = false;
    }
  }
</script>

<DialogBase.Root open={true} onOpenChange={open => { if (!open && !busy) onclose(); }}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
    <DialogBase.Header>
      <DialogBase.Title>Add comic run</DialogBase.Title>
      <DialogBase.Description>Review the exact run in {connectionName} and choose its mapped library.</DialogBase.Description>
    </DialogBase.Header>

    {#if error}<Alert.Root variant="destructive"><CircleAlert /><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if review}
      <div class="space-y-4 text-sm">
        <div class="space-y-1">
          <p class="text-text-primary">{review.candidate.title}{review.candidate.year ? ` · ${review.candidate.year}` : ""}</p>
          <p class="font-mono text-xs text-text-muted">{identity.value}</p>
        </div>
        {#if review.existing}
          <p class="text-text-muted">This run is already in {connectionName}. Open it to request an exact issue.</p>
        {:else}
          <label class="space-y-2 text-sm">Save to mapped library
            <Select ariaLabel="Comic library" value={mountId}
              options={review.mounts.map(mount => ({ value: mount.id, label: mount.label }))}
              onchange={value => { mountId = value; pending = null; }} disabled={busy || !!pending} />
          </label>
          <p class="text-text-muted">The run will be added with monitoring and automatic search off. You can then choose an exact issue to request.</p>
        {/if}
      </div>
    {:else if busy}
      <p class="flex items-center gap-2 text-sm text-text-muted"><LoaderCircle class="animate-spin" />Checking the exact run and mapped libraries…</p>
    {/if}

    <DialogBase.Footer>
      <Button variant="outline" onclick={onclose} disabled={busy}>Close</Button>
      {#if review?.existing}
        <Button onclick={() => openHolding(review!.existing!)} disabled={busy}>Open run</Button>
      {:else if review}
        <Button onclick={() => void submit()} disabled={busy || !mountId}>
          {pending ? "Retry same add" : "Add run"}
        </Button>
      {:else if error}
        <Button variant="secondary" onclick={() => void loadReview()} disabled={busy}>Review again</Button>
      {/if}
    </DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
