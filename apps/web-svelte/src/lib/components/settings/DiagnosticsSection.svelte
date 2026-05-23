<script lang="ts">
  import { Loader2, RefreshCw, Wrench } from "@lucide/svelte";
  import { rebuildPreviews, backfillFingerprints } from "$lib/api/prismedia";

  let rebuilding = $state(false);
  let result = $state<string | null>(null);
  let backfilling = $state(false);
  let backfillResult = $state<string | null>(null);

  async function handleRebuildPreviews() {
    rebuilding = true;
    result = null;
    try {
      const res = await rebuildPreviews();
      result = `Queued ${res.enqueued} ${res.enqueued === 1 ? "entity" : "entities"} for preview regeneration${res.skipped > 0 ? ` (${res.skipped} already pending)` : ""}`;
    } catch {
      result = "Failed to queue rebuild";
    } finally {
      rebuilding = false;
    }
  }

  async function handleBackfillFingerprints() {
    backfilling = true;
    backfillResult = null;
    try {
      const res = await backfillFingerprints();
      backfillResult = `Queued ${res.enqueued} ${res.enqueued === 1 ? "entity" : "entities"} for fingerprint generation${res.skipped > 0 ? ` (${res.skipped} already have fingerprints)` : ""}`;
    } catch {
      backfillResult = "Failed to queue backfill";
    } finally {
      backfilling = false;
    }
  }
</script>

<section class="space-y-3">
  <div class="flex items-center gap-2.5 px-1">
    <Wrench class="h-4 w-4 text-text-accent" />
    <div>
      <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
        Diagnostics
      </h2>
      <p class="text-[0.68rem] text-text-muted">Maintenance actions for troubleshooting</p>
    </div>
  </div>
  <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
    <div class="surface-card p-3 space-y-2">
      <div>
        <p class="text-[0.78rem] font-medium text-status-error-text">
          Force rebuild all previews
        </p>
        <p class="text-[0.68rem] text-text-muted">
          Queues preview generation for every video, image, book page, and audio track in the
          library. Use this after replacing source files, after quality setting changes, or to
          fix corrupt thumbnails and sprites. Heavy maintenance job.
        </p>
      </div>
      <div class="flex items-center gap-3">
        <button
          type="button"
          onclick={handleRebuildPreviews}
          disabled={rebuilding}
          class="inline-flex items-center gap-1.5 border border-status-error/25 bg-status-error/[0.12] px-3 py-1.5 text-[0.72rem] font-medium text-status-error-text transition-colors hover:bg-status-error/[0.18] disabled:opacity-50"
        >
          {#if rebuilding}
            <Loader2 class="h-3 w-3 animate-spin" />
          {:else}
            <RefreshCw class="h-3 w-3" />
          {/if}
          {rebuilding ? "Queuing..." : "Force rebuild previews"}
        </button>
        {#if result}
          <p class="text-[0.68rem] text-text-muted">{result}</p>
        {/if}
      </div>
    </div>
    <div class="surface-card p-3 space-y-2">
      <div>
        <p class="text-[0.78rem] font-medium text-text-primary">Backfill fingerprints</p>
        <p class="text-[0.68rem] text-text-muted">
          Queues MD5 and oshash fingerprint generation for every video, image, and audio track
          that does not yet have a stored fingerprint. Required for deduplication and external
          database matching.
        </p>
      </div>
      <div class="flex items-center gap-3">
        <button
          type="button"
          onclick={handleBackfillFingerprints}
          disabled={backfilling}
          class="inline-flex items-center gap-1.5 border border-border-accent/40 bg-accent-950/30 px-3 py-1.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/50 hover:shadow-[var(--shadow-glow-accent)] disabled:opacity-50"
        >
          {#if backfilling}
            <Loader2 class="h-3 w-3 animate-spin" />
          {:else}
            <RefreshCw class="h-3 w-3" />
          {/if}
          {backfilling ? "Queuing..." : "Backfill fingerprints"}
        </button>
        {#if backfillResult}
          <p class="text-[0.68rem] text-text-muted">{backfillResult}</p>
        {/if}
      </div>
    </div>
  </div>
</section>
