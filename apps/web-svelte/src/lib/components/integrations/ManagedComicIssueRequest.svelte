<script lang="ts">
  import { onMount } from "svelte";
  import { CircleAlert, LoaderCircle } from "@lucide/svelte";
  import { Alert, Button, Checkbox, DialogBase } from "@prismedia/ui-svelte";
  import { EXTERNAL_ID_PROVIDER } from "$lib/api/generated/codes";
  import type {
    CommitReviewedManagedRequestInput,
    ManagedComicIssue,
    ManagedItemInput,
    ReviewedManagedRequest,
    ReviewedManagedRequestCommitResponse,
  } from "$lib/api/generated/model";
  import {
    fetchReviewedManagedRequest,
    saveReviewedManagedRequest,
  } from "$lib/api/reviewed-managed-requests";
  import { ManagedRequestRejectedError } from "$lib/api/managed-requests";
  import { createUuid } from "$lib/utils/uuid";

  let {
    connectionId,
    connectionName,
    item,
    issue,
    onclose,
  }: {
    connectionId: string;
    connectionName: string;
    item: ManagedItemInput;
    issue: ManagedComicIssue;
    onclose: () => void;
  } = $props();

  let review = $state<ReviewedManagedRequest | null>(null);
  let pending = $state<CommitReviewedManagedRequestInput | null>(null);
  let accepted = $state<ReviewedManagedRequestCommitResponse | null>(null);
  let monitored = $state(true);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let alive = true;
  /** The issue is one exact target inside the run the manager already holds. */
  const connected = $derived({ item, remoteTargetId: issue.remoteId, targetLabel: issue.issueLabel });
  const scope = $derived(review?.scopes[0] ?? null);
  const target = $derived(review?.targets?.[0] ?? null);
  const existingRequest = $derived(scope?.existingRequest ?? null);
  const acceptedRequest = $derived(accepted?.scopes[0]?.managedRequest ?? null);
  const acceptedIssueId = $derived(accepted?.scopes[0]?.targetEntityIds?.[0] ?? null);

  onMount(() => {
    void loadReview();
    return () => { alive = false; };
  });

  async function loadReview() {
    busy = true;
    error = null;
    try {
      const result = await fetchReviewedManagedRequest(connectionId, { connected, scopes: [{}] });
      if (alive) review = result;
    } catch (cause) {
      if (alive) error = cause instanceof Error ? cause.message : "Could not review this issue.";
    } finally {
      if (alive) busy = false;
    }
  }

  async function submit() {
    if (!review || !scope || busy || accepted) return;
    pending ??= {
      operationId: createUuid(),
      expectedConnectionRevision: review.connectionRevision,
      connected,
      scopes: [{ libraryRootId: scope.mount.libraryRootId }],
      profileId: null,
      monitored,
      search: true,
    };
    busy = true;
    error = null;
    try {
      const result = await saveReviewedManagedRequest(connectionId, pending);
      if (alive) { accepted = result; pending = null; }
    } catch (cause) {
      if (!alive) return;
      error = cause instanceof Error ? cause.message : "Could not request this issue.";
      if (cause instanceof ManagedRequestRejectedError) pending = null;
    } finally {
      if (alive) busy = false;
    }
  }

  function setOpen(open: boolean) {
    if (!open && !busy) onclose();
  }
</script>

<DialogBase.Root open={true} onOpenChange={setOpen}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
    <DialogBase.Header>
      <DialogBase.Title>Request issue #{issue.issueLabel}</DialogBase.Title>
      <DialogBase.Description>
        {issue.title} · {connectionName}
      </DialogBase.Description>
    </DialogBase.Header>

    {#if error}
      <Alert.Root variant="destructive"><CircleAlert /><Alert.Description>{error}</Alert.Description></Alert.Root>
    {/if}

    {#if accepted}
      <div class="space-y-3 text-sm">
        <p class="text-text-primary">{acceptedRequest
          ? "This issue is requested. Prismedia will show it as wanted until a verified comic archive arrives in the mapped library."
          : "This issue already has its file in the library."}</p>
        <div class="flex flex-wrap gap-2">
          {#if acceptedIssueId}<a href={`/comics/${accepted.entityId}/installments/${acceptedIssueId}`} class="underline">Open issue</a>{/if}
          <a href="/request?activity" class="underline">View request activity</a>
        </div>
      </div>
    {:else if review && target && existingRequest}
      <div class="space-y-3 text-sm">
        <p class="text-text-primary">Issue #{target.label} already has a retained request for this connected run.</p>
        <p class="text-text-muted">{existingRequest.title}</p>
        <a href="/request?activity" class="inline-block underline">View request activity</a>
      </div>
    {:else if review && scope && target}
      <div class="space-y-4 text-sm">
        <div class="space-y-1">
          <p class="text-text-primary">#{target.label} · {target.title}</p>
          <p class="text-text-muted">Comic Vine {target.externalIds?.[EXTERNAL_ID_PROVIDER.comicVine]}</p>
        </div>
        <div class="space-y-1">
          <p class="text-text-muted">Destination</p>
          <p class="text-text-primary">{scope.mount.label}</p>
          <p class="break-all font-mono text-xs text-text-muted">{scope.mount.localPath}</p>
        </div>
        <label class="flex items-start gap-3">
          <Checkbox aria-label="Monitor this issue" checked={monitored}
            onchange={value => monitored = value} disabled={busy || !!pending} />
          <span>Monitor this issue in {connectionName}</span>
        </label>
        <p class="text-text-muted">Prismedia will search this issue once now. {scope.existing?.item.monitored ? "The run is monitored." : "Run monitoring is off in the connected app."}</p>
      </div>
    {:else if busy}
      <p class="flex items-center gap-2 text-sm text-text-muted"><LoaderCircle class="animate-spin" />Checking the exact issue and mapped library…</p>
    {/if}

    <DialogBase.Footer>
      <Button variant="outline" onclick={onclose} disabled={busy}>Done</Button>
      {#if !accepted && review && !existingRequest}
        <Button onclick={() => void submit()} disabled={busy}>
          {pending ? "Retry same request" : "Request issue"}
        </Button>
      {:else if !accepted && error && !pending}
        <Button variant="secondary" onclick={() => void loadReview()} disabled={busy}>Review again</Button>
      {/if}
    </DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
