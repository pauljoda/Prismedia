<script lang="ts">
  import { onDestroy } from "svelte";
  import { ChevronLeft, Download, ExternalLink, FileText, Loader2, Upload, X } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import { page } from "$app/state";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import PieceStateBar from "$lib/components/acquisitions/PieceStateBar.svelte";
  import type { EntityDetailActionButton } from "$lib/components/entities/entity-detail-types";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import type {
    AcquisitionDetail,
    AcquisitionFilesView,
    AcquisitionTransferView,
    ReleaseCandidateView,
  } from "$lib/api/generated/model";
  import {
    cancelAcquisition,
    fetchAcquisition,
    fetchAcquisitionFiles,
    fetchAcquisitionTransfer,
    queueAcquisitionCandidate,
    uploadManualTorrent,
  } from "$lib/api/acquisitions";
  import { acquisitionToDetailCard } from "$lib/requests/acquisition-entity-card";
  import { ACTIVE_ACQUISITION_STATUSES, acquisitionStatusLabel } from "$lib/requests/review-cards";

  const id = $derived(page.params.id ?? "");

  let detail = $state<AcquisitionDetail | null>(null);
  let transfer = $state<AcquisitionTransferView | null>(null);
  let files = $state<AcquisitionFilesView | null>(null);
  let error = $state<string | null>(null);
  let busy = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  const card = $derived(detail ? acquisitionToDetailCard(detail.summary) : null);
  const status = $derived(detail?.summary.status ?? null);
  const isActive = $derived(status ? ACTIVE_ACQUISITION_STATUSES.includes(status) : false);
  const canChoose = $derived(status === ACQUISITION_STATUS.awaitingSelection);
  const isDownloading = $derived(status === ACQUISITION_STATUS.queued || status === ACQUISITION_STATUS.downloading);
  const isDone = $derived(
    status === ACQUISITION_STATUS.downloaded ||
      status === ACQUISITION_STATUS.importing ||
      status === ACQUISITION_STATUS.imported,
  );

  async function load() {
    if (!id) return;
    try {
      detail = await fetchAcquisition(id);
      error = null;
      // Pull the status-appropriate detail.
      if (isDownloading) {
        transfer = await fetchAcquisitionTransfer(id);
      } else {
        transfer = null;
      }
      if (isDownloading || isDone) {
        files = await fetchAcquisitionFiles(id);
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load acquisition";
    }
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (busy) return;
    busy = true;
    try {
      detail = await queueAcquisitionCandidate(id, candidate.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue release";
    } finally {
      busy = false;
    }
  }

  async function cancel() {
    if (busy) return;
    busy = true;
    try {
      detail = await cancelAcquisition(id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel";
    } finally {
      busy = false;
    }
  }

  async function onUpload(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || busy) return;
    busy = true;
    try {
      detail = await uploadManualTorrent(id, file);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to upload torrent";
    } finally {
      busy = false;
      input.value = "";
    }
  }

  function formatBytes(bytes: number): string {
    if (!bytes || bytes <= 0) return "—";
    const mb = bytes / 1_000_000;
    return mb >= 1000 ? `${(mb / 1000).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }
  function formatSpeed(bps: number): string {
    return bps > 0 ? `${formatBytes(bps)}/s` : "—";
  }
  function formatEta(seconds: number): string {
    if (!seconds || seconds <= 0 || seconds >= 8640000) return "—";
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return h > 0 ? `${h}h ${m}m` : `${m}m`;
  }

  const actionButtons = $derived<EntityDetailActionButton[]>(
    isActive || canChoose
      ? [{ id: "cancel", label: "Cancel", icon: X, variant: "danger", onClick: cancel, disabled: busy }]
      : [],
  );

  $effect(() => {
    if (id) void load();
  });
  $effect(() => {
    if (isActive && !pollTimer) {
      pollTimer = setInterval(load, 3000);
    } else if (!isActive && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });
</script>

<svelte:head><title>{detail?.summary.title ?? "Acquisition"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <a
    href="/request"
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-3.5 w-3.5" />
    Back to Request
  </a>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
  {/if}

  {#if !card || !detail}
    <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading…</span>
    </div>
  {:else}
    <EntityDetail {card} posterSize="large" showFlagActions={false} {actionButtons}>
      {#snippet heroBadges()}
        {#if detail}
          <Badge variant={status === ACQUISITION_STATUS.imported ? "success" : status === ACQUISITION_STATUS.failed ? "error" : "accent"}>
            {acquisitionStatusLabel(detail.summary.status)}
          </Badge>
          {#if detail.summary.statusMessage}
            <span class="text-sm text-text-muted">{detail.summary.statusMessage}</span>
          {/if}
        {/if}
      {/snippet}

      {#snippet afterBody()}
        {#if detail}
          {#if status === ACQUISITION_STATUS.searching}
            <p class="flex items-center gap-2 text-sm text-text-muted">
              <Loader2 class="h-4 w-4 animate-spin" /> Searching indexers…
            </p>

          {:else if isDownloading}
            <!-- ── Live transfer ── -->
            <section class="space-y-3">
              <h2 class="text-kicker text-text-primary">Download</h2>
              {#if transfer}
                <div class="space-y-2 rounded-sm border border-border-subtle bg-surface-1 p-3">
                  <div class="h-2 w-full overflow-hidden rounded-full bg-surface-3">
                    <div class="h-full rounded-full bg-accent-500 transition-all" style:width={`${Math.round(Number(transfer.progress) * 100)}%`}></div>
                  </div>
                  <div class="flex flex-wrap gap-x-5 gap-y-1 font-mono text-[0.72rem] text-text-muted">
                    <span>{Math.round(Number(transfer.progress) * 100)}%</span>
                    <span>↓ {formatSpeed(Number(transfer.downloadSpeedBytesPerSecond))}</span>
                    <span>ETA {formatEta(Number(transfer.etaSeconds))}</span>
                    <span>{transfer.seeds} seeds · {transfer.peers} peers</span>
                    <span>{formatBytes(Number(transfer.totalSizeBytes))}</span>
                    {#if transfer.state}<span>{transfer.state}</span>{/if}
                  </div>
                  <PieceStateBar pieces={transfer.pieceStates.map(Number)} />
                </div>
              {:else}
                <p class="text-sm text-text-muted">Waiting for the download client to report progress…</p>
              {/if}
            </section>

          {:else if isDone}
            <!-- ── Imported / downloaded files ── -->
            <section class="space-y-2">
              <h2 class="text-kicker text-text-primary">
                Files
                {#if files}<span class="ml-1.5 font-mono text-[0.68rem] font-normal text-text-muted">{files.files.length}</span>{/if}
              </h2>
              {#if files && files.files.length > 0}
                <div class="overflow-hidden rounded-sm border border-border-subtle">
                  {#each files.files as f (f.name)}
                    <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0">
                      <span class="flex min-w-0 items-center gap-2 text-sm text-text-primary">
                        <FileText class="h-3.5 w-3.5 shrink-0 text-text-muted" />
                        <span class="truncate">{f.name}</span>
                      </span>
                      <span class="shrink-0 font-mono text-[0.72rem] text-text-muted">{formatBytes(Number(f.sizeBytes))}</span>
                    </div>
                  {/each}
                </div>
              {:else}
                <p class="text-sm text-text-muted">No files to show yet.</p>
              {/if}
            </section>

          {:else}
            <!-- ── Release review (awaiting selection / failed) ── -->
            <section class="space-y-3">
              <h2 class="text-kicker text-text-primary">
                Releases
                <span class="ml-1.5 font-mono text-[0.68rem] font-normal text-text-muted">{detail.candidates.length}</span>
              </h2>

              {#if detail.candidates.length === 0}
                <p class="text-sm text-text-muted">No release candidates found.</p>
              {:else}
                <div class="overflow-x-auto rounded-sm border border-border-subtle">
                  <table class="w-full text-sm">
                    <thead class="bg-surface-1 text-left text-[0.7rem] uppercase tracking-wide text-text-muted">
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
                            <div class="max-w-[26rem] truncate text-text-primary" title={c.title}>{c.title}</div>
                            {#if !c.accepted && c.rejections.length > 0}
                              <div class="text-[0.7rem] text-warning-text">{c.rejections.map((r) => String(r).replace(/-/g, " ")).join(", ")}</div>
                            {/if}
                          </td>
                          <td class="px-3 py-2 text-text-muted">{c.indexerName}</td>
                          <td class="px-3 py-2 text-right text-text-muted">{formatBytes(Number(c.sizeBytes))}</td>
                          <td class="px-3 py-2 text-right text-text-muted">{c.seeders ?? "—"}</td>
                          <td class="px-3 py-2 text-right font-mono text-[0.72rem] text-text-muted">{Number(c.score).toFixed(0)}</td>
                          <td class="px-3 py-2">
                            <div class="flex items-center justify-end gap-1.5">
                              {#if c.infoUrl}
                                <a href={c.infoUrl} target="_blank" rel="noopener" title="Open release page" class="inline-flex items-center text-text-muted transition-colors hover:text-text-accent">
                                  <ExternalLink class="h-3.5 w-3.5" />
                                </a>
                              {/if}
                              {#if c.accepted && canChoose}
                                <Button size="sm" onclick={() => queue(c)} disabled={busy} class="gap-1.5">
                                  <Download class="h-3.5 w-3.5" />
                                  Download
                                </Button>
                              {/if}
                            </div>
                          </td>
                        </tr>
                      {/each}
                    </tbody>
                  </table>
                </div>
              {/if}

              {#if canChoose}
                <!-- ── Manual .torrent fallback ── -->
                <div class="flex flex-wrap items-center gap-3 rounded-sm border border-dashed border-border-subtle bg-surface-1 p-3">
                  <div class="min-w-0 flex-1">
                    <p class="text-sm font-medium text-text-primary">Have a .torrent file?</p>
                    <p class="text-[0.72rem] text-text-muted">Open a release page above, download its .torrent, then upload it here to download directly.</p>
                  </div>
                  <label class="inline-flex cursor-pointer items-center gap-1.5 rounded-xs border border-border-subtle bg-surface-2 px-3 py-1.5 text-[0.75rem] font-medium text-text-secondary transition-colors hover:text-text-primary">
                    <Upload class="h-3.5 w-3.5" />
                    Upload .torrent
                    <input type="file" accept=".torrent,application/x-bittorrent" class="hidden" onchange={onUpload} disabled={busy} />
                  </label>
                </div>
              {/if}
            </section>
          {/if}
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
</div>
