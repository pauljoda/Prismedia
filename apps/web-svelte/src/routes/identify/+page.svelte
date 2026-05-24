<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import {
    AlertCircle,
    Check,
    Home,
    Loader2,
    RefreshCw,
    ScanSearch,
    X,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import {
    IdentifyStore,
    setIdentifyStore,
    useIdentifyStore,
  } from "$lib/components/identify/identify-store.svelte";
  import IdentifyDashboard from "$lib/components/identify/IdentifyDashboard.svelte";
  import IdentifyKindTab from "$lib/components/identify/IdentifyKindTab.svelte";
  import IdentifyReviewChoice from "$lib/components/identify/IdentifyReviewChoice.svelte";
  import IdentifyReviewParent from "$lib/components/identify/IdentifyReviewParent.svelte";
  import IdentifyReviewChild from "$lib/components/identify/IdentifyReviewChild.svelte";
  import { entityKindIcon } from "$lib/components/identify/identify-icons";

  const store = new IdentifyStore();
  setIdentifyStore(store);

  onMount(() => {
    void store.loadInitial();
  });

  onDestroy(() => {
    store.destroy();
  });
</script>

<svelte:head>
  <title>Identify · Prismedia</title>
</svelte:head>

<div class="flex flex-col gap-0 pb-16">
  <!-- ── Header ── -->
  <div class="flex items-start justify-between gap-4">
    <div class="flex items-center gap-3">
      <ScanSearch class="h-5 w-5 text-text-accent" />
      <div>
        <h1 class="flex items-center gap-2">
          Identify
          {#if store.view.kind !== "dashboard"}
            <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.62rem] text-phosphor-600">
              {store.view.kind === "kind-tab" ? store.view.entityKind : "review"}
            </span>
          {/if}
        </h1>
        <p class="mt-0.5 text-[0.78rem] text-text-muted">
          {#if store.view.kind === "dashboard"}
            Identify entities via plugin providers
          {:else if store.view.kind === "kind-tab"}
            Scoped to {store.view.entityKind}
          {:else}
            Reviewing proposal
          {/if}
        </p>
      </div>
    </div>
    <button
      type="button"
      onclick={() => void store.loadInitial()}
      class="flex h-9 w-9 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      aria-label="Refresh"
    >
      {#if store.loading}
        <Loader2 class="h-4 w-4 animate-spin" />
      {:else}
        <RefreshCw class="h-4 w-4" />
      {/if}
    </button>
  </div>

  <!-- ── Tab strip (dashboard + per-kind tabs) ── -->
  {#if store.view.kind === "dashboard" || store.view.kind === "kind-tab"}
    <nav class="mt-4 flex items-stretch overflow-hidden rounded-sm border border-border-subtle bg-gradient-to-b from-surface-2 to-surface-1">
      <button
        type="button"
        class={cn(
          "flex items-center gap-2 border-b-2 border-r border-r-border-subtle px-3.5 py-2.5 font-heading text-[0.78rem] font-semibold transition-colors",
          store.view.kind === "dashboard"
            ? "border-b-accent-500 bg-accent-950/20 text-text-accent-bright"
            : "border-b-transparent text-text-muted hover:bg-surface-2 hover:text-text-primary",
        )}
        onclick={() => store.navigateToDashboard()}
      >
        <Home class="h-3.5 w-3.5" />
        Dashboard
      </button>

      <div class="flex flex-1 items-stretch overflow-x-auto">
        {#each store.supportedKinds as kindInfo (kindInfo.kind)}
          {@const isActive = store.view.kind === "kind-tab" && store.view.entityKind === kindInfo.kind}
          {@const KindIcon = entityKindIcon(kindInfo.kind)}
          <button
            type="button"
            class={cn(
              "flex items-center gap-2 whitespace-nowrap border-b-2 px-3.5 py-2.5 font-heading text-[0.8rem] font-semibold transition-colors",
              isActive
                ? "border-b-accent-500 bg-accent-950/20 text-text-accent-bright"
                : "border-b-transparent text-text-secondary hover:bg-surface-2 hover:text-text-primary",
            )}
            onclick={() => store.navigateToKind(kindInfo.kind)}
          >
            <svelte:component this={KindIcon} class="h-3.5 w-3.5" />
            <span>{kindInfo.label}</span>
            {#if kindInfo.pending > 0}
              <StatusLed status="accent" pulse size="sm" />
            {/if}
          </button>
        {/each}
      </div>
    </nav>
  {/if}

  <!-- ── Notices ── -->
  {#if store.error}
    <div class="mt-4 flex items-center gap-2.5 rounded-xs border border-error/40 bg-surface-1 px-3 py-2.5 text-[0.82rem] text-text-primary" role="alert">
      <AlertCircle class="h-4 w-4 shrink-0 text-error-text" />
      <span class="min-w-0 flex-1">{store.error}</span>
      <button
        type="button"
        class="shrink-0 text-text-disabled transition-colors hover:text-text-primary"
        onclick={() => (store.error = null)}
        aria-label="Dismiss error"
      >
        <X class="h-3.5 w-3.5" />
      </button>
    </div>
  {/if}

  {#if store.message}
    <div class="mt-4 flex items-center gap-2.5 rounded-xs border border-border-accent bg-surface-1 px-3 py-2.5 text-[0.82rem] text-text-primary">
      <Check class="h-4 w-4 shrink-0 text-text-accent" />
      <span class="min-w-0 flex-1">{store.message}</span>
      <button
        type="button"
        class="shrink-0 text-text-disabled transition-colors hover:text-text-primary"
        onclick={() => (store.message = null)}
        aria-label="Dismiss message"
      >
        <X class="h-3.5 w-3.5" />
      </button>
    </div>
  {/if}

  <!-- ── Main content area ── -->
  <div class="mt-4">
    {#if store.loading}
      <div class="flex items-center justify-center py-16">
        <Loader2 class="h-6 w-6 animate-spin text-text-accent" />
      </div>
    {:else if store.view.kind === "dashboard"}
      <IdentifyDashboard />
    {:else if store.view.kind === "kind-tab"}
      <IdentifyKindTab entityKind={store.view.entityKind} />
    {:else if store.view.kind === "review-choice"}
      <IdentifyReviewChoice entity={store.view.entity} candidates={store.view.candidates} />
    {:else if store.view.kind === "review-parent"}
      <IdentifyReviewParent entity={store.view.entity} proposal={store.view.proposal} />
    {:else if store.view.kind === "review-child"}
      <IdentifyReviewChild
        entity={store.view.entity}
        proposal={store.view.proposal}
        parentProposal={store.view.parentProposal}
      />
    {/if}
  </div>
</div>
