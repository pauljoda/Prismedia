<script lang="ts">
  import { Loader2, RefreshCw, Wrench } from "@lucide/svelte";
  import { Panel } from "@prismedia/ui-svelte";
  import { rebuildPreviews, backfillFingerprints } from "$lib/api/jobs";

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

<Panel>
  <div class="p-5 space-y-5">
    <div class="flex items-center gap-2.5">
      <Wrench class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-kicker text-text-primary">Diagnostics</h2>
        <p class="text-[0.68rem] text-text-muted">Maintenance actions for troubleshooting</p>
      </div>
    </div>

    <div class="grid gap-3 sm:grid-cols-2">
      <div class="surface-well p-4 space-y-3">
        <div>
          <p class="text-label font-medium text-status-error-text">
            Force rebuild all previews
          </p>
          <p class="text-[0.68rem] text-text-muted leading-relaxed">
            Queues preview generation for every video, image, book page, and audio track in the
            library. Heavy maintenance job.
          </p>
        </div>
        <div class="flex items-center gap-3">
          <button
            type="button"
            onclick={handleRebuildPreviews}
            disabled={rebuilding}
            class="inline-flex items-center gap-1.5 rounded-xs border border-status-error/25 bg-status-error/[0.12] px-3 py-1.5 text-[0.72rem] font-medium text-status-error-text transition-colors hover:bg-status-error/[0.18] disabled:opacity-50"
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

      <div class="surface-well p-4 space-y-3">
        <div>
          <p class="text-label font-medium text-text-primary">Backfill fingerprints</p>
          <p class="text-[0.68rem] text-text-muted leading-relaxed">
            Queues MD5 and oshash fingerprint generation for entities that do not yet have
            stored fingerprints. Required for deduplication and external database matching.
          </p>
        </div>
        <div class="flex items-center gap-3">
          <button
            type="button"
            onclick={handleBackfillFingerprints}
            disabled={backfilling}
            class="inline-flex items-center gap-1.5 rounded-xs border border-border-accent/40 bg-accent-950/30 px-3 py-1.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/50 hover:shadow-[var(--shadow-glow-accent)] disabled:opacity-50"
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
  </div>
</Panel>
