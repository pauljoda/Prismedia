<script lang="ts">
  import { onMount, onDestroy } from "svelte";
  import { Badge, Button, TextInput } from "@prismedia/ui-svelte";
  import { Download, Loader, RefreshCw, X, BookPlus } from "@lucide/svelte";
  import { ACQUISITION_STATUS, type AcquisitionStatusCode } from "$lib/api/generated/codes";
  import type { AcquisitionDetail, AcquisitionSummary, ReleaseCandidateView } from "$lib/api/generated/model";
  import {
    cancelAcquisition,
    createAcquisition,
    fetchAcquisition,
    fetchAcquisitions,
    queueAcquisitionCandidate,
  } from "$lib/api/acquisitions";

  let acquisitions = $state<AcquisitionSummary[]>([]);
  let selectedId = $state<string | null>(null);
  let detail = $state<AcquisitionDetail | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let busy = $state(false);

  // New-book request form.
  let title = $state("");
  let author = $state("");
  let year = $state("");

  let pollTimer: ReturnType<typeof setInterval> | null = null;

  const ACTIVE: AcquisitionStatusCode[] = [
    ACQUISITION_STATUS.pending,
    ACQUISITION_STATUS.searching,
    ACQUISITION_STATUS.queued,
    ACQUISITION_STATUS.downloading,
    ACQUISITION_STATUS.importing,
  ];

  const STATUS_LABEL: Record<AcquisitionStatusCode, string> = {
    [ACQUISITION_STATUS.pending]: "Pending",
    [ACQUISITION_STATUS.searching]: "Searching",
    [ACQUISITION_STATUS.awaitingSelection]: "Awaiting selection",
    [ACQUISITION_STATUS.queued]: "Queued",
    [ACQUISITION_STATUS.downloading]: "Downloading",
    [ACQUISITION_STATUS.downloaded]: "Downloaded",
    [ACQUISITION_STATUS.importing]: "Importing",
    [ACQUISITION_STATUS.imported]: "Imported",
    [ACQUISITION_STATUS.failed]: "Failed",
    [ACQUISITION_STATUS.cancelled]: "Cancelled",
    [ACQUISITION_STATUS.manualImportRequired]: "Manual import",
  };

  function statusVariant(status: AcquisitionStatusCode): "default" | "success" | "warning" | "error" | "accent" {
    if (status === ACQUISITION_STATUS.imported) return "success";
    if (status === ACQUISITION_STATUS.failed) return "error";
    if (status === ACQUISITION_STATUS.manualImportRequired) return "warning";
    if (status === ACQUISITION_STATUS.awaitingSelection) return "accent";
    return "default";
  }

  function formatSize(bytes: number): string {
    if (!bytes || bytes <= 1) return "—";
    const mb = bytes / 1_000_000;
    return mb >= 1000 ? `${(mb / 1000).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }

  function rejectionText(reasons: ReleaseCandidateView["rejections"]): string {
    return reasons.map((r) => String(r).replace(/-/g, " ")).join(", ");
  }

  async function loadList() {
    try {
      acquisitions = await fetchAcquisitions();
      error = null;
    } catch (e) {
      error = e instanceof Error ? e.message : "Failed to load acquisitions";
    } finally {
      loading = false;
    }
  }

  async function select(id: string) {
    selectedId = id;
    detail = null;
    await refreshDetail();
  }

  async function refreshDetail() {
    if (!selectedId) return;
    try {
      detail = await fetchAcquisition(selectedId);
    } catch (e) {
      error = e instanceof Error ? e.message : "Failed to load acquisition";
    }
  }

  async function submitRequest(event: Event) {
    event.preventDefault();
    if (!title.trim() || busy) return;
    busy = true;
    try {
      const summary = await createAcquisition({
        title: title.trim(),
        author: author.trim() || null,
        series: null,
        year: year.trim() ? Number(year) : null,
        posterUrl: null,
        pluginId: null,
        pluginItemId: null,
        requestHistoryId: null,
      });
      title = "";
      author = "";
      year = "";
      await loadList();
      await select(summary.id);
    } catch (e) {
      error = e instanceof Error ? e.message : "Failed to start acquisition";
    } finally {
      busy = false;
    }
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (!selectedId || busy) return;
    busy = true;
    try {
      detail = await queueAcquisitionCandidate(selectedId, candidate.id);
      await loadList();
    } catch (e) {
      error = e instanceof Error ? e.message : "Failed to queue release";
    } finally {
      busy = false;
    }
  }

  async function cancel() {
    if (!selectedId || busy) return;
    busy = true;
    try {
      detail = await cancelAcquisition(selectedId);
      await loadList();
    } catch (e) {
      error = e instanceof Error ? e.message : "Failed to cancel";
    } finally {
      busy = false;
    }
  }

  // Poll the list (and open detail) while anything is mid-flight.
  $effect(() => {
    const anyActive =
      acquisitions.some((a) => ACTIVE.includes(a.status)) ||
      (detail ? ACTIVE.includes(detail.summary.status) : false);
    if (anyActive && !pollTimer) {
      pollTimer = setInterval(async () => {
        await loadList();
        await refreshDetail();
      }, 3000);
    } else if (!anyActive && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });

  onMount(loadList);
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });
</script>

<div class="space-y-6">
  <header class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5 text-xl font-semibold text-text-primary">
        <Download class="h-5 w-5 text-text-accent" />
        Acquire
      </h1>
      <p class="mt-1 text-sm text-text-muted">
        Request a book; Prismedia searches your indexers, downloads the chosen release, and imports it.
      </p>
    </div>
    <Button variant="ghost" size="sm" onclick={loadList} disabled={loading}>
      <RefreshCw class="h-4 w-4" />
      Refresh
    </Button>
  </header>

  {#if error}
    <div class="rounded-lg border border-error/40 bg-error-muted/20 px-4 py-2 text-sm text-error-text">{error}</div>
  {/if}

  <!-- New book request -->
  <form class="flex flex-wrap items-end gap-3 rounded-xl border border-border-subtle bg-surface-2/40 p-4" onsubmit={submitRequest}>
    <div class="flex-1 min-w-[12rem]">
      <label class="mb-1 block text-xs font-medium text-text-muted" for="acq-title">Title</label>
      <TextInput id="acq-title" value={title} oninput={(e) => (title = e.currentTarget.value)} placeholder="Book title" />
    </div>
    <div class="flex-1 min-w-[10rem]">
      <label class="mb-1 block text-xs font-medium text-text-muted" for="acq-author">Author</label>
      <TextInput id="acq-author" value={author} oninput={(e) => (author = e.currentTarget.value)} placeholder="Author (optional)" />
    </div>
    <div class="w-24">
      <label class="mb-1 block text-xs font-medium text-text-muted" for="acq-year">Year</label>
      <TextInput id="acq-year" value={year} oninput={(e) => (year = e.currentTarget.value)} placeholder="Year" />
    </div>
    <Button type="submit" disabled={!title.trim() || busy}>
      <BookPlus class="h-4 w-4" />
      Request
    </Button>
  </form>

  <div class="grid gap-6 lg:grid-cols-[20rem_1fr]">
    <!-- Acquisition list -->
    <aside class="space-y-2">
      {#if loading}
        <p class="text-sm text-text-muted">Loading…</p>
      {:else if acquisitions.length === 0}
        <p class="text-sm text-text-muted">No acquisitions yet.</p>
      {:else}
        {#each acquisitions as item (item.id)}
          <button
            type="button"
            class="w-full rounded-lg border px-3 py-2 text-left transition-colors {selectedId === item.id ? 'border-text-accent/60 bg-surface-2' : 'border-border-subtle hover:bg-surface-2/50'}"
            onclick={() => select(item.id)}
          >
            <div class="flex items-center justify-between gap-2">
              <span class="truncate text-sm font-medium text-text-primary">{item.title}</span>
              <Badge variant={statusVariant(item.status)}>{STATUS_LABEL[item.status]}</Badge>
            </div>
            <div class="mt-0.5 truncate text-xs text-text-muted">
              {item.author ?? "Unknown author"}{item.year ? ` · ${item.year}` : ""}
              {#if item.progress != null && ACTIVE.includes(item.status)}
                · {Math.round(Number(item.progress) * 100)}%
              {/if}
            </div>
          </button>
        {/each}
      {/if}
    </aside>

    <!-- Release review -->
    <section class="min-w-0">
      {#if !detail}
        <p class="text-sm text-text-muted">Select an acquisition to review releases.</p>
      {:else}
        <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div class="flex items-center gap-2">
            <Badge variant={statusVariant(detail.summary.status)}>{STATUS_LABEL[detail.summary.status]}</Badge>
            <span class="text-sm text-text-muted">{detail.summary.statusMessage ?? ""}</span>
          </div>
          {#if ACTIVE.includes(detail.summary.status) || detail.summary.status === ACQUISITION_STATUS.awaitingSelection}
            <Button variant="ghost" size="sm" onclick={cancel} disabled={busy}>
              <X class="h-4 w-4" />
              Cancel
            </Button>
          {/if}
        </div>

        {#if detail.summary.status === ACQUISITION_STATUS.searching}
          <p class="flex items-center gap-2 text-sm text-text-muted">
            <Loader class="h-4 w-4 animate-spin" /> Searching indexers…
          </p>
        {:else if detail.candidates.length === 0}
          <p class="text-sm text-text-muted">No release candidates.</p>
        {:else}
          <div class="overflow-x-auto rounded-xl border border-border-subtle">
            <table class="w-full text-sm">
              <thead class="bg-surface-2/50 text-left text-xs uppercase tracking-wide text-text-muted">
                <tr>
                  <th class="px-3 py-2">Release</th>
                  <th class="px-3 py-2">Indexer</th>
                  <th class="px-3 py-2 text-right">Size</th>
                  <th class="px-3 py-2 text-right">Seeders</th>
                  <th class="px-3 py-2 text-right">Score</th>
                  <th class="px-3 py-2"></th>
                </tr>
              </thead>
              <tbody>
                {#each detail.candidates as c (c.id)}
                  <tr class="border-t border-border-subtle {c.accepted ? '' : 'opacity-55'}">
                    <td class="px-3 py-2">
                      <div class="max-w-[28rem] truncate text-text-primary" title={c.title}>{c.title}</div>
                      {#if !c.accepted}
                        <div class="text-xs text-warning-text">{rejectionText(c.rejections)}</div>
                      {/if}
                    </td>
                    <td class="px-3 py-2 text-text-muted">{c.indexerName}</td>
                    <td class="px-3 py-2 text-right text-text-muted">{formatSize(Number(c.sizeBytes))}</td>
                    <td class="px-3 py-2 text-right text-text-muted">{c.seeders ?? "—"}</td>
                    <td class="px-3 py-2 text-right font-mono text-xs text-text-muted">{Number(c.score).toFixed(0)}</td>
                    <td class="px-3 py-2 text-right">
                      {#if c.accepted && detail.summary.status === ACQUISITION_STATUS.awaitingSelection}
                        <Button size="sm" onclick={() => queue(c)} disabled={busy}>
                          <Download class="h-3.5 w-3.5" />
                          Download
                        </Button>
                      {/if}
                    </td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        {/if}
      {/if}
    </section>
  </div>
</div>
