<script lang="ts">
  import { Loader2, RefreshCw, Repeat2, Search, Upload, X } from "@lucide/svelte";
  import { Button, SearchInput } from "@prismedia/ui-svelte";
  import type { AcquisitionDetail, ManualReplacementSearchResult, ReleaseCandidateView } from "$lib/api/generated/model";
  import {
    queueManualReplacement,
    searchManualReplacement,
    uploadAcquisitionContent,
  } from "$lib/api/acquisitions";
  import ReleaseTable from "$lib/components/acquisitions/ReleaseTable.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";

  let {
    entityId,
    canReplace,
    canUpload,
    onStarted,
  }: {
    entityId: string;
    canReplace: boolean;
    canUpload: boolean;
    onStarted: (detail: AcquisitionDetail) => void | Promise<void>;
  } = $props();

  let review = $state<ManualReplacementSearchResult | null>(null);
  let reviewOpen = $state(false);
  let customQuery = $state("");
  let busy = $state(false);
  let uploadProgress = $state<number | null>(null);
  let error = $state<string | null>(null);

  async function search(query?: string) {
    if (busy) return;
    busy = true;
    reviewOpen = true;
    error = null;
    try {
      review = await searchManualReplacement(entityId, query);
    } catch (reason) {
      error = reason instanceof Error ? reason.message : "Failed to search for replacements";
    } finally {
      busy = false;
    }
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (busy || !review) return;
    busy = true;
    error = null;
    try {
      const detail = await queueManualReplacement(entityId, review.searchId, candidate.id);
      closeReview();
      await onStarted(detail);
    } catch (reason) {
      error = reason instanceof Error ? reason.message : "Failed to queue replacement";
    } finally {
      busy = false;
    }
  }

  async function upload(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (files.length === 0 || busy) return;
    busy = true;
    uploadProgress = 0;
    error = null;
    try {
      const detail = await uploadAcquisitionContent(entityId, files, (value) => (uploadProgress = value));
      await onStarted(detail);
    } catch (reason) {
      error = reason instanceof Error ? reason.message : "Failed to upload content";
    } finally {
      busy = false;
      uploadProgress = null;
      input.value = "";
    }
  }

  function closeReview() {
    reviewOpen = false;
    review = null;
    customQuery = "";
    error = null;
  }
</script>

{#if canReplace || canUpload}
  <div class="manual-actions">
    <div class="flex flex-wrap items-center gap-2">
      {#if canReplace}
        <Button type="button" size="sm" variant="secondary" class="no-lift gap-1.5" disabled={busy} onclick={() => void search()}>
          <Repeat2 class="h-3.5 w-3.5" />
          Replace
        </Button>
      {/if}
      {#if canUpload}
        <label class:disabled={busy} class="upload-trigger">
          <Upload class="h-3.5 w-3.5" />
          Upload content
          <input type="file" multiple class="hidden" disabled={busy} onchange={upload} />
        </label>
      {/if}
    </div>

    {#if uploadProgress !== null}
      <div class="upload-progress" role="status" aria-label={`Uploading ${Math.round(uploadProgress * 100)}%`}>
        <div class="flex items-center justify-between gap-3 text-xs">
          <span class="flex items-center gap-2 text-text-secondary"><Loader2 class="h-3.5 w-3.5 animate-spin" />Uploading content</span>
          <span class="font-mono text-text-accent">{Math.round(uploadProgress * 100)}%</span>
        </div>
        <div class="h-1.5 overflow-hidden rounded-full bg-surface-3">
          <div class="h-full rounded-full bg-accent-500 transition-all" style:width={`${uploadProgress * 100}%`}></div>
        </div>
      </div>
    {/if}

    {#if reviewOpen}
      <section class="replacement-review">
        <div class="flex items-center justify-between gap-3">
          <div>
            <h3 class="text-sm font-semibold text-text-primary">Choose a replacement</h3>
            <p class="text-[0.72rem] text-text-muted">Nothing changes until you select a release.</p>
          </div>
          <Button type="button" size="sm" variant="ghost" aria-label="Close replacement review" onclick={closeReview}>
            <X class="h-3.5 w-3.5" />
          </Button>
        </div>
        <form
          class="flex flex-col gap-2 sm:flex-row"
          onsubmit={(event) => {
            event.preventDefault();
            void search(customQuery);
          }}
        >
          <SearchInput
            bind:value={customQuery}
            ariaLabel="Custom replacement search term"
            placeholder="Try an exact title, edition, group, or quality…"
            loading={busy}
            class="min-w-0 flex-1"
          />
          <Button type="submit" size="sm" variant="secondary" disabled={busy || !customQuery.trim()} class="gap-1.5">
            <Search class="h-3.5 w-3.5" />Search term
          </Button>
        </form>
        {#if busy && !review}
          <StatePlaceholder icon={RefreshCw} title="Searching for replacements" description="Checking indexers without changing the current copy." busy />
        {:else if review && review.candidates.length > 0}
          <ReleaseTable candidates={review.candidates} canChoose {busy} onQueue={queue} />
        {:else if review}
          <StatePlaceholder icon={Search} title="No replacement releases found" description="Adjust the search term or upload content directly." />
        {/if}
      </section>
    {/if}

    {#if error}<p role="alert" class="text-[0.72rem] text-error-text">{error}</p>{/if}
  </div>
{/if}

<style>
  .manual-actions { display: grid; gap: 0.75rem; }
  .upload-trigger {
    display: inline-flex;
    cursor: pointer;
    align-items: center;
    gap: 0.375rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    padding: 0.375rem 0.625rem;
    color: var(--color-text-secondary);
    font-size: 0.75rem;
    font-weight: 600;
  }
  .upload-trigger:hover { color: var(--color-text-primary); }
  .upload-trigger.disabled { cursor: not-allowed; opacity: 0.5; }
  .upload-progress,
  .replacement-review {
    display: grid;
    gap: 0.75rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
    padding: 0.8rem;
  }
</style>
