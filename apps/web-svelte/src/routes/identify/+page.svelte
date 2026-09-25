<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import {
    AlertCircle,
    Check,
    ChevronLeft,
    Loader2,
    ScanSearch,
    Sparkles,
    X,
  } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { fetchEntities } from "$lib/api/entities";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";
  import {
    useIdentifyStore,
  } from "$lib/components/identify/identify-store.svelte";
  import IdentifyDashboard from "$lib/components/identify/IdentifyDashboard.svelte";
  import IdentifyKindTab from "$lib/components/identify/IdentifyKindTab.svelte";
  import IdentifyReviewChoice from "$lib/components/identify/IdentifyReviewChoice.svelte";
  import IdentifyReviewParent from "$lib/components/identify/IdentifyReviewParent.svelte";
  import IdentifyReviewChild from "$lib/components/identify/IdentifyReviewChild.svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  const store = useIdentifyStore();
  const appChrome = useAppChrome();
  const nsfw = useNsfw();
  const numberFormat = new Intl.NumberFormat();

  let unidentified = $state<Record<string, number>>({});
  let countedFor = "";

  /** Counts files that are not organized yet, one `limit: 1` request per identifiable kind. */
  $effect(() => {
    const kinds = store.supportedKinds.map((entry) => entry.kind);
    const hideNsfw = nsfw.mode === "off";
    const key = `${hideNsfw}:${kinds.join(",")}`;
    if (kinds.length === 0 || key === countedFor) return;
    countedFor = key;
    unidentified = {};
    for (const kind of kinds) {
      fetchEntities({ kind, organized: false, hasFile: true, wanted: false, limit: 1, hideNsfw }).then(
        (response) => (unidentified = { ...unidentified, [kind]: Number(response.totalCount) || 0 }),
        () => undefined,
      );
    }
  });

  const totalUnidentified = $derived(Object.values(unidentified).reduce((sum, count) => sum + count, 0));

  onMount(() => {
    const entityId = page.url.searchParams.get("entity");
    const returnId = page.url.searchParams.get("returnId");
    if (entityId) {
      const query = returnId ? `?returnId=${encodeURIComponent(returnId)}` : "";
      void goto(`/identify/${entityId}${query}`);
    } else {
      void store.enterDashboardRoute();
    }
  });

  $effect(() => {
    const view = store.view;
    return appChrome.setBreadcrumbs(
      view.kind === "kind-tab"
        ? [{ label: "Identify" }, { label: labelForEntityKind(view.entityKind) }]
        : [{ label: "Identify" }],
    );
  });
</script>

<svelte:head>
  <title>Identify · Prismedia</title>
</svelte:head>

<div class="flex flex-col gap-0 pb-16">
  <ManagePageHeader icon={ScanSearch} title="Identify">
    {#snippet status()}
      <span class="flex flex-wrap gap-x-3 font-mono text-[0.72rem] text-text-muted">
        <span class={store.reviewableCount > 0 ? "text-text-primary" : undefined}>{store.reviewableCount} to review</span>
        {#if store.queuedCount > 0}<span>{store.queuedCount} queued</span>{/if}
        {#if store.searchingCount > 0}<span>{store.searchingCount} searching</span>{/if}
        {#if totalUnidentified > 0}<span>{numberFormat.format(totalUnidentified)} unidentified</span>{/if}
      </span>
    {/snippet}
    {#snippet actions()}
      <Button variant="secondary" size="sm" disabled={store.reviewableCount === 0} onclick={() => store.resumeNext()}>
        <Sparkles aria-hidden="true" />
        Review next
      </Button>
    {/snippet}
  </ManagePageHeader>

  {#if store.view.kind === "kind-tab"}
    <div class="mt-4">
      <Button variant="ghost" size="sm" class="-ml-2" onclick={() => store.navigateToDashboard()}>
        <ChevronLeft aria-hidden="true" />
        Families
      </Button>
    </div>
  {/if}

  <!-- ── Notices ── -->
  {#if store.error}
    <div class="mt-4 flex items-center gap-2.5 rounded-xs border border-error/40 bg-surface-1 px-3 py-2.5 text-[0.82rem] text-text-primary" role="alert">
      <AlertCircle class="h-4 w-4 shrink-0 text-error-text" />
      <span class="min-w-0 flex-1">{store.error}</span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        class="shrink-0 text-text-disabled transition-colors hover:text-text-primary"
        onclick={() => (store.error = null)}
        aria-label="Dismiss error"
      >
        <X class="h-3.5 w-3.5" />
      </Button>
    </div>
  {/if}

  {#if store.message}
    <div class="mt-4 flex items-center gap-2.5 rounded-xs border border-border-accent bg-surface-1 px-3 py-2.5 text-[0.82rem] text-text-primary">
      <Check class="h-4 w-4 shrink-0 text-text-accent" />
      <span class="min-w-0 flex-1">{store.message}</span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        class="shrink-0 text-text-disabled transition-colors hover:text-text-primary"
        onclick={() => (store.message = null)}
        aria-label="Dismiss message"
      >
        <X class="h-3.5 w-3.5" />
      </Button>
    </div>
  {/if}

  <!-- ── Main content area ── -->
  <div class="mt-4">
    <svelte:boundary onerror={(error) => console.error("[identify] review render failed", error)}>
    {#if store.loading}
      <div class="flex items-center justify-center py-16">
        <Loader2 class="h-6 w-6 animate-spin text-text-accent" />
      </div>
    {:else if store.view.kind === "dashboard"}
      <IdentifyDashboard {unidentified} />
    {:else if store.view.kind === "kind-tab"}
      <IdentifyKindTab entityKind={store.view.entityKind} />
    {:else if store.view.kind === "review-choice"}
      <IdentifyReviewChoice entity={store.view.entity} candidates={store.view.candidates} />
    {:else if store.view.kind === "review-parent"}
      <IdentifyReviewParent entity={store.view.entity} proposal={store.view.proposal} detail={store.view.detail} />
    {:else if store.view.kind === "review-child"}
      <IdentifyReviewChild
        entity={store.view.entity}
        proposal={store.view.proposal}
        parentProposal={store.view.parentProposal}
      />
    {/if}

    {#snippet failed(error, reset)}
      <div class="flex flex-col items-center justify-center gap-4 rounded-sm border border-error/40 bg-surface-1 px-4 py-12 text-center">
        <AlertCircle class="h-7 w-7 text-error-text" />
        <div class="space-y-1">
          <p class="font-heading text-[0.9rem] font-semibold text-text-primary">This view couldn't be displayed</p>
          <p class="mx-auto max-w-md font-mono text-[0.72rem] text-text-muted">
            {error instanceof Error ? error.message : String(error)}
          </p>
        </div>
        <div class="flex items-center gap-2">
          <Button
            type="button"
            variant="secondary"
            onclick={reset}
          >
            Try again
          </Button>
          <Button
            type="button"
            variant="primary"
            onclick={() => { reset(); store.navigateToDashboard(); }}
          >
            Back to dashboard
          </Button>
        </div>
      </div>
    {/snippet}
    </svelte:boundary>
  </div>
</div>
