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
  import { cn } from "@prismedia/ui-svelte";
  import { useIdentifyStore } from "./identify-store.svelte";
  import { entityKindIcon } from "./identify-icons";

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

  function cancelSelected() {
    for (const id of selectedQueueIds) {
      void store.deleteQueueItem(id);
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
        <button
          type="button"
          class={cn(
            "surface-card flex flex-col gap-2.5 p-3.5 text-left transition-all",
            hasPending && "border-border-accent-strong",
          )}
          onclick={() => store.navigateToKind(kindInfo.kind)}
        >
          <div class="flex items-center justify-between">
            <div
              class={cn(
                "grid h-9 w-9 place-items-center rounded-xs border",
                hasPending
                  ? "border-border-accent bg-accent-950/40 text-text-accent-bright"
                  : "border-border-subtle bg-surface-3 text-text-secondary",
              )}
            >
              <KindIcon class="h-[18px] w-[18px]" />
            </div>
          </div>

          <div>
            <div
              class={cn(
                "font-heading text-[0.95rem] font-semibold",
                hasPending ? "text-text-accent-bright" : "text-text-primary",
              )}
            >
              {kindInfo.label}
            </div>
            <div class="font-mono text-[0.66rem] text-text-muted">{kindInfo.kind}</div>
          </div>

          <div class="flex items-center gap-4 border-t border-border-subtle pt-2.5">
            {#if hasPending}
              <span class="font-mono text-[0.66rem] text-text-accent">{kindInfo.pending} queued</span>
            {/if}
            <div class="flex-1"></div>
            <ChevronRight class={cn("h-3.5 w-3.5", hasPending ? "text-text-accent" : "text-text-muted")} />
          </div>
        </button>
      {/each}
    </div>
  {/if}

  <!-- Queue -->
  {#if store.queue.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <ScanSearch class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Review queue</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{store.queue.length} items</span>
        {#if store.bulkAccepting}
          <span class="inline-flex items-center gap-1.5 font-mono text-[0.7rem] text-text-accent">
            <Loader2 class="h-3 w-3 animate-spin" />
            Accepting {store.bulkAcceptDone}/{store.bulkAcceptTotal}
          </span>
        {/if}
        <div class="flex-1"></div>
        {#if acceptableSelectedCount > 0}
          <button
            type="button"
            class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-accent-strong px-2.5 text-[0.72rem] font-medium text-text-primary transition-all disabled:cursor-not-allowed disabled:opacity-40"
            style="background: linear-gradient(135deg, rgba(242,194,106,0.24), rgba(242,194,106,0.1)); box-shadow: 0 0 18px rgba(242,194,106,0.16);"
            disabled={store.bulkAccepting}
            onclick={acceptSelected}
          >
            {#if store.bulkAccepting}
              <Loader2 class="h-3 w-3 animate-spin" />
            {:else}
              <Check class="h-3 w-3" />
            {/if}
            Accept {acceptableSelectedCount}
          </button>
        {/if}
        {#if selectedQueueIds.size > 0}
          <button
            type="button"
            class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.72rem] font-medium text-text-muted transition-colors hover:border-error/50 hover:text-error-text disabled:cursor-not-allowed disabled:opacity-40"
            disabled={store.bulkAccepting}
            onclick={cancelSelected}
          >
            <X class="h-3 w-3" />
            Cancel {selectedQueueIds.size}
          </button>
        {/if}
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-2.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/60 disabled:cursor-not-allowed disabled:opacity-40"
          disabled={!hasReviewable}
          onclick={() => store.reviewQueueItem(store.queue[0])}
        >
          <Sparkles class="h-3 w-3" />
          Review all
        </button>
      </header>

      <!-- Queue header -->
      <div class="hidden items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-2 md:grid md:grid-cols-[32px_70px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]">
        <label class="flex items-center">
          <input
            type="checkbox"
            class="h-3.5 w-3.5 accent-accent-500"
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
        {@const stateLabel = { proposal: "REVIEW", search: "SEARCH", done: "DONE", deleted: "DELETED", error: "ERROR" }[item.state]}
        {@const isSelected = selectedQueueIds.has(item.entityId)}
        <div
          class={cn(
            "grid grid-cols-[auto_auto_minmax(0,1fr)_auto] items-center gap-3 border-b border-border-subtle px-3.5 py-2.5 transition-colors last:border-b-0 md:grid-cols-[32px_70px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]",
            i === 0 && "bg-accent-950/20",
            isSelected && "bg-surface-2",
          )}
        >
          <label class="flex items-center">
            <input
              type="checkbox"
              class="h-3.5 w-3.5 accent-accent-500"
              checked={isSelected}
              onchange={() => toggleQueueSelection(item.entityId)}
            />
          </label>

          <div class="flex items-center gap-2">
            <span
              class={cn(
                "font-mono text-[0.66rem] font-semibold",
                item.state === "proposal" && "text-text-accent",
                item.state === "search" && "text-warning-text",
                item.state === "done" && "text-success-text",
                item.state === "error" && "text-error-text",
                item.state === "deleted" && "text-text-disabled",
              )}
            >
              {stateLabel}
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
            <button
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
            </button>
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
