<script lang="ts">
  import { onMount } from "svelte";
  import { ChevronLeft, Disc3, Film, History, Loader2, RefreshCw, Trash2 } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import { REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
  import { deleteRequestHistoryEntry, fetchRequestHistory } from "$lib/api/requests";
  import RequestStatusBadge from "$lib/components/requests/RequestStatusBadge.svelte";
  import type { RequestHistoryEntry } from "$lib/requests/request-model";
  import {
    REQUEST_KIND_LABELS,
    numericValue,
    thumbnailAspectForKind,
  } from "$lib/requests/request-helpers";

  let entries = $state<RequestHistoryEntry[]>([]);
  let providerWarnings = $state<string[]>([]);
  let loading = $state(true);
  let refreshing = $state(false);
  let error = $state<string | null>(null);
  let deletingId = $state<string | null>(null);

  onMount(() => void load());

  async function load(asRefresh = false) {
    if (asRefresh) {
      refreshing = true;
    } else {
      loading = true;
    }
    error = null;
    try {
      const response = await fetchRequestHistory();
      entries = response.entries;
      providerWarnings = response.providerErrors.map(
        (item) => `${item.displayName}: ${item.message}`,
      );
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request history";
    } finally {
      loading = false;
      refreshing = false;
    }
  }

  async function removeEntry(entry: RequestHistoryEntry) {
    deletingId = entry.id;
    error = null;
    try {
      await deleteRequestHistoryEntry(entry.id);
      entries = entries.filter((item) => item.id !== entry.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to delete request history entry";
    } finally {
      deletingId = null;
    }
  }

  function detailHref(entry: RequestHistoryEntry) {
    const params = new URLSearchParams({ source: entry.source });
    if (entry.serviceId) params.set("serviceId", entry.serviceId);
    return `/request/${entry.kind}/${encodeURIComponent(entry.externalId)}?${params.toString()}`;
  }

  function requestedOn(entry: RequestHistoryEntry) {
    return new Date(entry.requestedAt).toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  }

  function selectionSummary(entry: RequestHistoryEntry) {
    const count = numericValue(entry.selectedChildCount) ?? 0;
    if (count === 0) return null;
    const noun = entry.kind === REQUEST_MEDIA_KIND.series ? "season" : "album";
    return `${count} ${noun}${count === 1 ? "" : "s"}`;
  }

  function isMusic(entry: RequestHistoryEntry) {
    return entry.kind === REQUEST_MEDIA_KIND.artist || entry.kind === REQUEST_MEDIA_KIND.album;
  }
</script>

<svelte:head><title>Request History · Prismedia</title></svelte:head>

<div class="space-y-5">
  <a
    href="/request"
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-3.5 w-3.5" />
    Back to request
  </a>

  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5">
        <History class="h-5 w-5 text-text-accent" />
        Request History
      </h1>
      <p class="mt-1 text-[0.78rem] text-text-muted">
        Everything you've requested, with live status from each service
      </p>
    </div>
    <Button
      type="button"
      variant="secondary"
      size="sm"
      disabled={loading || refreshing}
      onclick={() => void load(true)}
      class="no-lift gap-1.5 px-3 py-1.5 text-xs"
    >
      <RefreshCw class={`h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} />
      Refresh
    </Button>
  </div>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
      {error}
    </div>
  {/if}
  {#each providerWarnings as warning}
    <div class="surface-panel border-l-2 border-warning px-4 py-2.5 text-sm text-warning-text">
      {warning}
    </div>
  {/each}

  {#if loading}
    <div class="flex items-center gap-2.5 p-6 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading request history…</span>
    </div>
  {:else if entries.length === 0}
    <div class="empty-rack-slot flex flex-col items-center gap-2 p-10 text-center">
      <History class="h-8 w-8 text-text-disabled" />
      <p class="text-sm font-medium text-text-secondary">No requests yet</p>
      <p class="max-w-md text-[0.78rem] text-text-muted">
        Requests you submit will show up here with their download status.
      </p>
    </div>
  {:else}
    <div class="space-y-2" aria-label="Request history entries">
      {#each entries as entry (entry.id)}
        <div class="surface-card no-lift flex items-center gap-3 p-2.5 sm:gap-3.5 sm:p-3">
          <a
            href={detailHref(entry)}
            class="w-10 shrink-0 self-start overflow-hidden rounded-xs bg-surface-1 sm:w-12"
            style:aspect-ratio={thumbnailAspectForKind(entry.kind)}
            aria-label={entry.title}
          >
            {#if entry.posterUrl}
              <img src={entry.posterUrl} alt="" loading="lazy" class="h-full w-full object-cover" />
            {:else}
              <span class="flex h-full w-full items-center justify-center text-text-disabled">
                {#if isMusic(entry)}
                  <Disc3 class="h-4 w-4" />
                {:else}
                  <Film class="h-4 w-4" />
                {/if}
              </span>
            {/if}
          </a>

          <div class="min-w-0 flex-1 space-y-1">
            <div class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
              <a
                href={detailHref(entry)}
                class="text-[0.88rem] font-medium leading-snug text-text-primary transition-colors hover:text-text-accent"
              >
                {entry.title}
              </a>
              {#if numericValue(entry.year)}
                <span class="font-mono text-[0.7rem] text-text-muted">{entry.year}</span>
              {/if}
            </div>
            <div class="flex flex-wrap items-center gap-1.5">
              <Badge variant="accent">{REQUEST_KIND_LABELS[entry.kind] ?? entry.kind}</Badge>
              <Badge>{entry.serviceName}</Badge>
              {#if selectionSummary(entry)}
                <Badge>{selectionSummary(entry)}</Badge>
              {/if}
              <span class="font-mono text-[0.68rem] text-text-muted">{requestedOn(entry)}</span>
            </div>
            {#if entry.statusMessage}
              <p class="truncate text-[0.72rem] text-text-muted">{entry.statusMessage}</p>
            {/if}
          </div>

          <div class="flex shrink-0 items-center gap-1.5 sm:gap-2.5">
            <RequestStatusBadge status={entry.status} />
            <button
              type="button"
              class="rounded-xs p-1.5 text-text-disabled transition-colors hover:bg-surface-2 hover:text-error-text"
              title="Remove from history"
              aria-label={`Remove ${entry.title} from history`}
              disabled={deletingId === entry.id}
              onclick={() => void removeEntry(entry)}
            >
              {#if deletingId === entry.id}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <Trash2 class="h-3.5 w-3.5" />
              {/if}
            </button>
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>
