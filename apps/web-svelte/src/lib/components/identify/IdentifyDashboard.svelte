<script lang="ts">
  import {
    Check,
    ChevronRight,
    CornerDownRight,
    Flame,
    Loader2,
    ScanSearch,
    Sparkles,
    X,
  } from "@lucide/svelte";
  import { Button, Checkbox,  cn } from "@prismedia/ui-svelte";
  import { useIdentifyStore } from "./identify-store.svelte";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { entityAccentForKind } from "$lib/entities/entity-accent";

  const store = useIdentifyStore();

  let selectedQueueIds = $state<Set<string>>(new Set());

  const hasReviewable = $derived(
    store.queue.some((q) => q.state === "proposal" || q.state === "search" || q.state === "error"),
  );

  const selectedItems = $derived(store.queue.filter((q) => selectedQueueIds.has(q.entityId)));
  const acceptableSelectedCount = $derived(
    selectedItems.filter((q) => q.state === "proposal" && q.proposal).length,
  );
  const allSelected = $derived(store.queue.length > 0 && selectedQueueIds.size === store.queue.length);

  function toggleQueueSelection(entityId: string) {
    const next = new Set(selectedQueueIds);
    if (next.has(entityId)) next.delete(entityId);
    else next.add(entityId);
    selectedQueueIds = next;
  }

  function toggleSelectAll() {
    selectedQueueIds = allSelected ? new Set() : new Set(store.queue.map((q) => q.entityId));
  }

  function rejectSelected() {
    for (const id of selectedQueueIds) {
      void store.rejectQueueItem(id);
    }
    selectedQueueIds = new Set();
  }

  async function acceptSelected() {
    await store.acceptQueueProposals(selectedItems);
    selectedQueueIds = new Set();
  }

  function proposedTitle(item: (typeof store.queue)[number]): string | null {
    const proposed = item.proposal?.patch?.title?.trim();
    if (!proposed) return null;
    return proposed.localeCompare(item.title.trim(), undefined, { sensitivity: "accent" }) === 0
      ? null
      : proposed;
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Kind nav cards (above queue) -->
  {#if store.supportedKinds.length > 0}
    <div class="flex items-baseline gap-2.5">
      <span class="text-kicker text-text-accent">Browse by kind</span>
    </div>
    <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
      {#each store.supportedKinds as kindInfo (kindInfo.kind)}
        {@const hasPending = kindInfo.pending > 0}
        {@const KindIcon = entityKindIcon(kindInfo.kind)}
        {@const kindAccent = entityAccentForKind(kindInfo.kind).primary}
        <Button variant="outline"
          type="button"
          class="grid h-auto min-h-control-lg w-full grid-cols-[auto_minmax(0,1fr)_auto] gap-3 whitespace-normal p-3 text-left"
          onclick={() => store.navigateToKind(kindInfo.kind)}
        >
          <KindIcon color={kindAccent} aria-hidden="true" />
          <span class="flex min-w-0 flex-col gap-1">
            <span>{kindInfo.label}</span>
            <span class="break-words font-mono text-caption text-muted-foreground">{kindInfo.kind}</span>
            {#if hasPending}
              <span class="text-caption">{kindInfo.pending} queued</span>
            {/if}
          </span>
          <ChevronRight aria-hidden="true" />
        </Button>
      {/each}
    </div>
  {/if}

  <!-- Queue -->
  {#if store.queue.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex flex-col gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5 sm:flex-row sm:items-center">
        <div class="flex items-center gap-2.5">
          <ScanSearch class="h-3.5 w-3.5 text-text-accent" />
          <span class="text-kicker text-text-accent">Review queue</span>
          <span class="font-mono text-[0.7rem] text-text-muted">{store.queue.length} items</span>
          {#if store.queuedCount > 0 || store.searchingCount > 0}
            <span class="inline-flex items-center gap-1.5 font-mono text-[0.7rem] text-text-accent">
              <Loader2 class="h-3 w-3 animate-spin" />
              {store.searchingCount} searching · {store.queuedCount} queued · {store.reviewableCount} to review
            </span>
          {/if}
          {#if store.bulkAccepting}
            <span class="inline-flex items-center gap-1.5 font-mono text-[0.7rem] text-text-accent">
              <Loader2 class="h-3 w-3 animate-spin" />
              Accepting {store.bulkAcceptDone}/{store.bulkAcceptTotal}
            </span>
          {/if}
        </div>
        <div class="hidden flex-1 sm:block"></div>
        <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
          {#if acceptableSelectedCount > 0}
            <Button variant="outline" size="sm"
              type="button"
              class="inline-flex h-8 w-full items-center justify-center gap-1.5 px-2.5 text-[0.72rem] font-medium disabled:cursor-not-allowed disabled:opacity-40 sm:h-7 sm:w-auto"

              disabled={store.bulkAccepting}
              onclick={acceptSelected}
            >
              {#if store.bulkAccepting}
                <Loader2 class="h-3 w-3 animate-spin" />
              {:else}
                <Check class="h-3 w-3" />
              {/if}
              Accept {acceptableSelectedCount}
            </Button>
          {/if}
          {#if selectedQueueIds.size > 0}
            <Button variant="destructive" size="sm"
              type="button"
              class="inline-flex h-8 w-full items-center justify-center gap-1.5 px-2.5 text-[0.72rem] font-medium disabled:cursor-not-allowed disabled:opacity-40 sm:h-7 sm:w-auto"
              disabled={store.bulkAccepting}
              onclick={rejectSelected}
            >
              <X class="h-3 w-3" />
              Reject {selectedQueueIds.size}
            </Button>
          {/if}
          <Button variant="outline" size="sm"
            type="button"
            class="inline-flex h-8 w-full items-center justify-center gap-1.5 px-2.5 text-[0.72rem] font-medium disabled:cursor-not-allowed disabled:opacity-40 sm:h-7 sm:w-auto"
            disabled={!hasReviewable}
            onclick={() => store.reviewQueueItem(store.queue[0])}
          >
            <Sparkles class="h-3 w-3" />
            Review all
          </Button>
        </div>
      </header>

      <!-- Mobile select-all -->
      <label class="flex items-center gap-2.5 border-b border-border-default bg-surface-2 px-3.5 py-2 md:hidden">
        <Checkbox size="sm"
          checked={allSelected}
          onchange={toggleSelectAll}
          aria-label="Select all queued items"
        />
        <span class="text-kicker">{allSelected ? "Deselect all" : "Select all"}</span>
        {#if selectedQueueIds.size > 0}
          <span class="font-mono text-[0.66rem] text-text-muted">{selectedQueueIds.size} selected</span>
        {/if}
      </label>

      <!-- Queue header -->
      <div class="hidden items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-2 md:grid md:grid-cols-[32px_70px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]">
        <label class="flex items-center">
          <Checkbox size="sm"
            checked={allSelected}
            onchange={toggleSelectAll}
            aria-label="Select all queued items"
          />
        </label>
        <span class="text-kicker">State</span>
        <span class="text-kicker">Name</span>
        <span class="text-kicker">Provider</span>
        <span class="text-kicker">Kind</span>
        <span class="text-kicker">Match</span>
        <span class="text-kicker text-right">Action</span>
      </div>

      {#each store.queue as item, i (item.entityId)}
        {@const stateLabel = { proposal: "REVIEW", search: "CHOOSE", queued: "QUEUED", searching: "SEARCHING", applying: "APPLYING", done: "DONE", deleted: "DELETED", error: "ERROR" }[item.state]}
        {@const isSelected = selectedQueueIds.has(item.entityId)}
        <div
          class={cn(
            "grid grid-cols-[auto_auto_minmax(0,1fr)_auto] items-center gap-3 border-b border-border-subtle px-3.5 py-2.5 transition-colors last:border-b-0 md:grid-cols-[32px_70px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]",
            i === 0 && "bg-accent-950/20",
            isSelected && "bg-surface-2",
          )}
        >
          <label class="flex items-center">
            <Checkbox size="sm"
              checked={isSelected}
              aria-label={`Select ${item.title}`}
              onchange={() => toggleQueueSelection(item.entityId)}
            />
          </label>

          <div class="flex items-center gap-2">
            <span
              class={cn(
                "font-mono text-[0.66rem] font-semibold",
                item.state === "proposal" && "text-text-accent",
                item.state === "search" && "text-warning-text",
                (item.state === "queued" || item.state === "searching") && "text-text-muted",
                item.state === "done" && "text-success-text",
                item.state === "error" && "text-error-text",
                item.state === "deleted" && "text-text-disabled",
              )}
            >
              {#if item.state === "searching" || item.state === "queued"}
                <span class="inline-flex items-center gap-1">
                  <Loader2 class="h-3 w-3 animate-spin" />
                  {stateLabel}
                </span>
              {:else}
                {stateLabel}
              {/if}
            </span>
          </div>

          <div class="min-w-0">
            <div class="flex min-w-0 items-center gap-2">
              <div class="truncate font-mono text-[0.78rem] text-text-secondary" title={item.title}>{item.title}</div>
              {#if item.isNsfw}
                <span
                  class="inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-xs border border-error/40 bg-error/10 text-error-text"
                  title="NSFW"
                  aria-label="NSFW"
                >
                  <Flame class="h-3 w-3" />
                </span>
              {/if}
            </div>
            {#if proposedTitle(item)}
              {@const proposed = proposedTitle(item)}
              <div class="flex min-w-0 items-center gap-1 text-text-accent" title={proposed}>
                <CornerDownRight class="h-3 w-3 shrink-0 text-text-muted" />
                <span class="truncate font-heading text-[0.86rem] font-semibold">{proposed}</span>
              </div>
            {/if}
            {#if item.state === "error" && item.errorMessage}
              <div class="truncate font-mono text-[0.66rem] text-error-text">{item.errorMessage}</div>
            {/if}
            <div class="truncate font-mono text-[0.66rem] text-text-muted md:hidden">{item.entityKind}</div>
          </div>

          <div class="hidden items-center gap-2 md:flex">
            {#if item.provider}
              <span class="font-mono text-[0.72rem] text-text-secondary">{item.provider}</span>
            {:else}
              <span class="font-mono text-[0.72rem] text-text-disabled">—</span>
            {/if}
          </div>

          <span class="hidden font-mono text-[0.66rem] text-text-muted md:block">{item.entityKind}</span>

          <span class="hidden font-mono text-[0.72rem] text-text-accent md:block">
            {#if item.proposal?.confidence}
              {Math.round((item.proposal.confidence ?? 0) * 100)}%
            {:else}
              —
            {/if}
          </span>

          <div class="flex justify-end">
            <Button variant="outline" size="sm"
              type="button"
              class={cn(
                "inline-flex h-7 items-center gap-1 rounded-xs border px-2 text-[0.72rem] font-medium transition-colors",
                item.state === "proposal"
                  ? "border-border-accent-strong bg-accent-950/40 text-text-accent hover:bg-accent-950/60"
                  : "border-border-default bg-surface-2 text-text-primary hover:bg-surface-3",
              )}
              onclick={() => store.reviewQueueItem(item)}
            >
              {item.state === "proposal" ? "Review" : item.state === "done" ? "View" : item.state === "error" ? "Retry" : "Identify"}
              <ChevronRight class="h-3 w-3" />
            </Button>
          </div>
        </div>
      {/each}
    </section>
  {/if}

  <!-- Empty state -->
  {#if !store.loading && store.supportedKinds.length === 0 && store.queue.length === 0}
    <div class="surface-panel flex flex-col items-center gap-3 p-8 text-center">
      <ScanSearch class="h-8 w-8 text-text-disabled" />
      <h3 class="text-text-primary">No identify providers</h3>
      <p class="max-w-sm text-[0.82rem] text-text-muted">
        Install and enable a plugin with identify support to get started. Check the Plugins page to manage providers.
      </p>
    </div>
  {/if}
</div>
