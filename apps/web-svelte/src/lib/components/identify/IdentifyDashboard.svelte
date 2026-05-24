<script lang="ts">
  import {
    ChevronRight,
    Filter,
    Loader2,
    Plug,
    ScanSearch,
    Sparkles,
    Zap,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import { useIdentifyStore } from "./identify-store.svelte";
  import { entityKindIcon } from "./identify-icons";

  const store = useIdentifyStore();

  const pendingCount = $derived(store.queue.filter((q) => q.state === "pending-review").length);
  const choiceCount = $derived(store.queue.filter((q) => q.state === "pending-choice").length);
  const errorCount = $derived(store.queue.filter((q) => q.state === "error").length);
  const hasReviewable = $derived(
    store.queue.some((q) => q.state === "pending-review" || q.state === "pending-choice" || q.state === "not-searched"),
  );
</script>

<div class="flex flex-col gap-4">
  <!-- Stats row -->
  <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
    <div class="surface-panel flex flex-col gap-1 p-3.5">
      <span class="text-kicker">Queue</span>
      <span class="font-mono text-2xl font-semibold text-text-accent">{store.queue.length}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">awaiting review</span>
    </div>
    <div class="surface-panel flex flex-col gap-1 p-3.5">
      <span class="text-kicker">Pending</span>
      <span class="font-mono text-2xl font-semibold text-text-primary">{pendingCount}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">ready to review</span>
    </div>
    <div class="surface-panel flex flex-col gap-1 p-3.5">
      <span class="text-kicker">Pick</span>
      <span class="font-mono text-2xl font-semibold text-warning-text">{choiceCount}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">need candidate pick</span>
    </div>
    <div class="surface-panel flex flex-col gap-1 p-3.5">
      <span class="text-kicker">Providers</span>
      <span class="font-mono text-2xl font-semibold text-text-primary">
        {store.providers.filter((p) => p.installed && p.enabled).length}/{store.providers.length}
      </span>
      <span class="font-mono text-[0.7rem] text-text-muted">active</span>
    </div>
  </div>

  <!-- Plugins strip -->
  <section class="surface-panel overflow-hidden">
    <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
      <Plug class="h-3.5 w-3.5 text-text-accent" />
      <span class="text-kicker text-text-accent">Plugins</span>
      <span class="font-mono text-[0.7rem] text-text-muted">
        {store.providers.filter((p) => p.installed && p.enabled).length} active
      </span>
    </header>
    <div class="grid grid-cols-1 gap-2 p-3 sm:grid-cols-2 lg:grid-cols-3">
      {#each store.providers as provider (provider.id)}
        {@const active = provider.installed && provider.enabled}
        {@const needsAuth = provider.missingAuthKeys.length > 0}
        <div
          class={cn(
            "flex items-center gap-3 rounded-sm border p-2.5 transition-colors",
            active && !needsAuth
              ? "border-border-accent bg-accent-950/30"
              : "border-border-default bg-surface-1",
          )}
        >
          <StatusLed
            status={needsAuth ? "warning" : active ? "accent" : "idle"}
            pulse={active && !needsAuth}
          />
          <div class="min-w-0 flex-1">
            <div class="truncate font-heading text-[0.82rem] font-semibold text-text-primary">
              {provider.name}
            </div>
            <div class="font-mono text-[0.66rem] text-text-muted">
              {provider.supports.map((s) => s.entityKind).join(", ")}
            </div>
          </div>
          <span class="font-mono text-[0.66rem] text-text-disabled">v{provider.version}</span>
        </div>
      {/each}
    </div>
  </section>

  <!-- Queue -->
  {#if store.queue.length > 0}
    <section class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <ScanSearch class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Review queue</span>
        <span class="font-mono text-[0.7rem] text-text-muted">{store.queue.length} items</span>
        <div class="flex-1"></div>
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-2.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/60 disabled:cursor-not-allowed disabled:opacity-40"
          disabled={!hasReviewable}
          onclick={() => store.resumeNext()}
        >
          <Sparkles class="h-3 w-3" />
          Resume next
        </button>
      </header>

      <!-- Queue header -->
      <div class="hidden items-center gap-3 border-b border-border-default bg-surface-2 px-3.5 py-2 md:grid md:grid-cols-[80px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]">
        <span class="text-kicker">State</span>
        <span class="text-kicker">Title</span>
        <span class="text-kicker">Provider</span>
        <span class="text-kicker">Kind</span>
        <span class="text-kicker">Match</span>
        <span class="text-kicker text-right">Action</span>
      </div>

      {#each store.queue as item, i (item.entityId)}
        {@const ledStatus = item.state === "pending-review" ? "accent" : item.state === "pending-choice" ? "warning" : item.state === "complete" ? "active" : item.state === "error" ? "error" : "idle"}
        {@const stateLabel = { "pending-review": "REVIEW", "pending-choice": "PICK", "not-searched": "SEARCH", complete: "DONE", error: "ERROR" }[item.state]}
        <div
          class={cn(
            "grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 border-b border-border-subtle px-3.5 py-2.5 transition-colors last:border-b-0 md:grid-cols-[80px_minmax(0,2fr)_minmax(0,1fr)_90px_80px_100px]",
            i === 0 && "bg-accent-950/20",
          )}
        >
          <div class="flex items-center gap-2">
            <StatusLed status={ledStatus} pulse={item.state === "pending-review"} size="sm" />
            <span
              class={cn(
                "font-mono text-[0.66rem] font-semibold",
                item.state === "pending-review" && "text-text-accent",
                item.state === "pending-choice" && "text-warning-text",
                item.state === "complete" && "text-success-text",
                item.state === "error" && "text-error-text",
                item.state === "not-searched" && "text-text-disabled",
              )}
            >
              {stateLabel}
            </span>
          </div>

          <div class="min-w-0">
            <div class="truncate font-heading text-[0.86rem] text-text-primary">{item.title}</div>
            {#if item.state === "error" && item.errorMessage}
              <div class="truncate font-mono text-[0.66rem] text-error-text">{item.errorMessage}</div>
            {/if}
            <div class="truncate font-mono text-[0.66rem] text-text-muted md:hidden">{item.entityKind}</div>
          </div>

          <div class="hidden items-center gap-2 md:flex">
            {#if item.provider}
              <StatusLed status="active" size="sm" />
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
                item.state === "pending-review"
                  ? "border-border-accent-strong bg-accent-950/40 text-text-accent hover:bg-accent-950/60"
                  : "border-border-default bg-surface-2 text-text-primary hover:bg-surface-3",
              )}
              onclick={() => store.reviewQueueItem(item)}
            >
              {item.state === "pending-review" ? "Review" : item.state === "complete" ? "View" : item.state === "error" ? "Retry" : "Identify"}
              <ChevronRight class="h-3 w-3" />
            </button>
          </div>
        </div>
      {/each}
    </section>
  {/if}

  <!-- Kind nav cards -->
  {#if store.supportedKinds.length > 0}
    <div class="flex items-baseline gap-2.5">
      <span class="text-kicker text-text-accent">Browse by kind</span>
      <span class="font-mono text-[0.7rem] text-text-muted">scope identify to a specific entity kind</span>
    </div>
    <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
      {#each store.supportedKinds as kindInfo (kindInfo.kind)}
        {@const hasPending = kindInfo.pending > 0}
        <button
          type="button"
          class={cn(
            "surface-card flex flex-col gap-2.5 p-3.5 text-left transition-all",
            hasPending && "border-border-accent-strong shadow-glow-accent",
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
              <svelte:component this={entityKindIcon(kindInfo.kind)} class="h-[18px] w-[18px]" />
            </div>
            {#if hasPending}
              <StatusLed status="accent" pulse />
            {/if}
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
              <span class="font-mono text-[0.66rem] text-text-accent">{kindInfo.pending} pending</span>
            {/if}
            <div class="flex-1"></div>
            <ChevronRight class={cn("h-3.5 w-3.5", hasPending ? "text-text-accent" : "text-text-muted")} />
          </div>
        </button>
      {/each}
    </div>
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
