<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    AlertCircle,
    ChevronDown,
    ChevronRight,
    ChevronUp,
    Loader2,
    Search,
    ScanSearch,
  } from "@lucide/svelte";
  import IdentifyProviderSelect from "$lib/components/identify/IdentifyProviderSelect.svelte";
  import IdentifyRejectQueueActions from "$lib/components/identify/IdentifyRejectQueueActions.svelte";
  import IdentifyReviewChoice from "$lib/components/identify/IdentifyReviewChoice.svelte";
  import IdentifyReviewParent from "$lib/components/identify/IdentifyReviewParent.svelte";
  import IdentifyReviewChild from "$lib/components/identify/IdentifyReviewChild.svelte";
  import { shouldShowRouteQueueRejectActions } from "$lib/components/identify/identify-route-actions";
  import { useIdentifyStore } from "$lib/components/identify/identify-store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  const store = useIdentifyStore();
  const appChrome = useAppChrome();
  const entityId = $derived(page.params.entityId ?? "");
  const current = $derived(store.queue.find((item) => item.entityId === entityId) ?? null);
  const providers = $derived(current ? store.providersForKind(current.entityKind) : []);

  const queueIndex = $derived(store.queue.findIndex((item) => item.entityId === entityId));
  const prevQueueItem = $derived(queueIndex > 0 ? store.queue[queueIndex - 1] : null);
  const nextQueueItem = $derived(queueIndex >= 0 && queueIndex < store.queue.length - 1 ? store.queue[queueIndex + 1] : null);
  const nextReviewQueueItem = $derived(current ? store.nextQueueItem(current.entityId) : null);

  let selectedProviderId = $state("");
  let manualTitle = $state("");
  let searching = $state(false);

  const activeProviderId = $derived(selectedProviderId || current?.provider || providers[0]?.id || "");
  const activeProvider = $derived(
    providers.find((provider) => provider.id === activeProviderId) ?? null,
  );
  const currentIdentifyStatus = $derived(
    store.identifyingId === current?.entityId ? store.identifyingStatus : null,
  );
  const isIdentifyingCurrent = $derived(store.identifyingId === current?.entityId);
  const backToSearchDisabled = $derived(isIdentifyingCurrent);
  const activeReviewChild = $derived(
    store.view.kind === "review-child" && store.view.entity.id === entityId ? store.view : null,
  );
  const reviewSurfaceHasRejectFooter = $derived(
    !activeReviewChild &&
      Boolean(
        (current?.state === "proposal" && current.proposal) ||
          (current?.state === "search" && current.candidates.length > 0),
      ),
  );
  const showRouteQueueRejectActions = $derived(
    shouldShowRouteQueueRejectActions({
      current,
      reviewSurfaceHasRejectFooter,
      isIdentifyingCurrent,
    }),
  );

  onMount(async () => {
    const returnId = page.url.searchParams.get("returnId") ?? page.url.searchParams.get("quid");
    const item = await store.seedEntity(entityId, returnId, {
      openExistingOnly: page.url.searchParams.get("queued") === "1",
    });
    if (item?.provider) {
      selectedProviderId = item.provider;
    }
    const queued = store.queue.find((candidate) => candidate.entityId === entityId);
    if (queued) {
      store.reviewResolvedQueueItem(queued);
    }
  });

  $effect(() => {
    return appChrome.setBreadcrumbs([
      { label: "Identify", href: "/identify" },
      { label: current?.title ?? "Review" },
    ]);
  });

  async function runSearch() {
    if (!current || !activeProvider) return;
    searching = true;
    try {
      await store.identifyEntity(current.entity, activeProvider.id, {
        title: manualTitle.trim() || null,
      });
    } finally {
      searching = false;
    }
  }

  async function backToSearch() {
    if (!current || !activeProvider) return;
    await store.backToSearch(current.entity, activeProvider.id);
  }

  function goToQueueItem(item: typeof prevQueueItem) {
    if (item) void goto(`/identify/${item.entityId}`);
  }
</script>

<svelte:head>
  <title>Identify Entity · Prismedia</title>
</svelte:head>

<div class="flex flex-col gap-4 pb-16">
  <!-- Breadcrumb row -->
  <div class="flex items-center gap-1.5 text-[0.78rem]">
    <a
      href="/identify"
      class="text-text-muted transition-colors hover:text-text-accent"
    >
      Identify
    </a>
    {#if current}
      <ChevronRight class="h-3 w-3 text-text-disabled" />
      <span class="truncate font-heading font-semibold text-text-primary">{current.title}</span>
    {/if}
  </div>

  <!-- Controls row -->
  <div class="flex flex-col gap-2 md:flex-row md:items-center md:gap-3">
    <!-- Queue position nav -->
    {#if store.queue.length > 1 && queueIndex >= 0}
      <div class="flex items-center gap-1.5">
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!prevQueueItem}
          onclick={() => goToQueueItem(prevQueueItem)}
          aria-label="Previous queue item"
        >
          <ChevronUp class="h-3.5 w-3.5" />
        </button>
        <span class="font-mono text-[0.72rem] text-text-muted">
          {queueIndex + 1}/{store.queue.length}
        </span>
        <button
          type="button"
          class="inline-flex h-8 w-8 items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:bg-surface-3 disabled:opacity-30 md:h-7 md:w-7"
          disabled={!nextQueueItem}
          onclick={() => goToQueueItem(nextQueueItem)}
          aria-label="Next queue item"
        >
          <ChevronDown class="h-3.5 w-3.5" />
        </button>
      </div>
    {/if}

    {#if current && current.state === "proposal" && activeProvider}
      <button
        type="button"
        class="inline-flex h-10 w-full items-center justify-center gap-1.5 rounded-xs border border-border-accent bg-accent-950/30 px-3 text-[0.8rem] font-medium text-text-accent shadow-[0_0_18px_rgba(242,194,106,0.10)] transition-colors hover:bg-accent-950/45 disabled:cursor-not-allowed disabled:opacity-40 md:hidden"
        disabled={backToSearchDisabled}
        onclick={backToSearch}
      >
        <Search class="h-3.5 w-3.5" />
        Back to Search
      </button>
    {/if}

    {#if current && showRouteQueueRejectActions}
      <IdentifyRejectQueueActions
        entityId={current.entityId}
        showNext={Boolean(nextReviewQueueItem)}
        compact
        class="md:hidden"
      />
    {/if}

    <div class="hidden flex-1 md:block"></div>

    {#if current}
      {#if current.state === "proposal" && activeProvider}
        <button
          type="button"
          class="hidden h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.76rem] text-text-muted transition-colors hover:border-border-accent hover:text-text-accent disabled:cursor-not-allowed disabled:opacity-40 md:inline-flex"
          disabled={backToSearchDisabled}
          onclick={backToSearch}
        >
          <Search class="h-3.5 w-3.5" />
          Back to Search
        </button>
      {/if}
      {#if showRouteQueueRejectActions}
        <IdentifyRejectQueueActions
          entityId={current.entityId}
          showNext={Boolean(nextReviewQueueItem)}
          compact
          class="hidden md:flex"
        />
      {/if}
    {/if}
  </div>

  {#if store.error}
    <div class="flex items-center gap-2.5 rounded-xs border border-error/40 bg-surface-1 px-3 py-2.5 text-[0.82rem] text-text-primary" role="alert">
      <AlertCircle class="h-4 w-4 shrink-0 text-error-text" />
      <span class="min-w-0 flex-1">{store.error}</span>
    </div>
  {/if}

  <svelte:boundary onerror={(error) => console.error("[identify] review render failed", error)}>
  {#if store.loading || !current}
    <div class="flex flex-col items-center justify-center gap-3 py-16 text-center">
      <Loader2 class="h-6 w-6 animate-spin text-text-accent" />
      <div class="space-y-1">
        <p class="font-heading text-[0.86rem] font-semibold text-text-primary">
          {store.identifyingStatus ?? "Preparing identify review"}
        </p>
        <p class="font-mono text-[0.7rem] text-text-muted">
          Plugin searches can take a moment when a provider checks related metadata.
        </p>
      </div>
    </div>
  {:else if activeReviewChild}
    <IdentifyReviewChild
      entity={activeReviewChild.entity}
      proposal={activeReviewChild.proposal}
      parentProposal={activeReviewChild.parentProposal}
      ancestors={activeReviewChild.ancestors}
    />
  {:else if current.state === "proposal" && current.proposal}
    <IdentifyReviewParent entity={current.entity} proposal={current.proposal} detail={current.detail} />
  {:else if current.state === "search" && current.candidates.length > 0}
    <IdentifyReviewChoice
      entity={current.entity}
      candidates={current.candidates}
      providerId={current.provider}
    />
  {:else}
    <section class="surface-panel overflow-visible">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Search class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Search</span>
        <span class="font-mono text-[0.7rem] text-text-muted">select a provider and query</span>
      </header>
      <div class="flex flex-col gap-4 p-4">
        {#if providers.length > 0}
          <IdentifyProviderSelect
            {providers}
            selectedId={activeProviderId}
            onChange={(providerId) => (selectedProviderId = providerId)}
          />
          <div class="flex flex-wrap items-center gap-2">
            <input
              type="text"
              class="allow-compact-input-text min-w-[14rem] flex-1 rounded-xs border border-border-default bg-surface-1 px-2.5 py-1.5 text-[0.82rem] text-text-primary outline-none transition-colors focus:border-border-accent"
              placeholder={current.title}
              bind:value={manualTitle}
              onkeydown={(event) => {
                if (event.key === "Enter") void runSearch();
              }}
            />
            <button
              type="button"
              class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-3 text-[0.78rem] text-text-accent transition-colors hover:bg-accent-950/60 disabled:cursor-not-allowed disabled:opacity-40"
              disabled={searching || !activeProvider}
              onclick={runSearch}
            >
              {#if searching || store.identifyingId === current.entityId}
                <Loader2 class="h-4 w-4 animate-spin" />
              {:else}
                <Search class="h-4 w-4" />
              {/if}
              Search
            </button>
          </div>
          {#if currentIdentifyStatus}
            <div class="flex items-center gap-2 rounded-xs border border-border-subtle bg-surface-1 px-3 py-2 font-mono text-[0.72rem] text-text-muted">
              <Loader2 class="h-3.5 w-3.5 animate-spin text-text-accent" />
              <span>{currentIdentifyStatus}</span>
            </div>
          {/if}
        {:else}
          <div class="rounded-xs border border-warning/30 bg-warning-muted px-3 py-2.5 text-[0.82rem] text-warning-text">
            No enabled provider supports {current.entityKind}.
          </div>
        {/if}

        {#if current.state === "error" && current.errorMessage}
          <div class="rounded-xs border border-error/40 bg-surface-1 px-3 py-2.5 text-[0.82rem] text-error-text">
            {current.errorMessage}
          </div>
        {/if}
      </div>
    </section>
  {/if}

  {#snippet failed(error, reset)}
    <div class="flex flex-col items-center justify-center gap-4 rounded-sm border border-error/40 bg-surface-1 px-4 py-12 text-center">
      <AlertCircle class="h-7 w-7 text-error-text" />
      <div class="space-y-1">
        <p class="font-heading text-[0.9rem] font-semibold text-text-primary">This proposal couldn't be displayed</p>
        <p class="mx-auto max-w-md font-mono text-[0.72rem] text-text-muted">
          {error instanceof Error ? error.message : String(error)}
        </p>
      </div>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-3 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3"
          onclick={reset}
        >
          Try again
        </button>
        <a
          href="/identify"
          class="inline-flex h-9 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-3 text-[0.78rem] text-text-accent transition-colors hover:bg-accent-950/60"
        >
          Back to dashboard
        </a>
      </div>
    </div>
  {/snippet}
  </svelte:boundary>
</div>
