<script lang="ts">
  /**
   * THE acquisition + monitoring surface an entity page mounts — the Acquisition detail tab's body,
   * collapsing everything the request layer knows about an entity: the standing container monitor,
   * the wanted placeholder's "Search for release", the wanted-children roll-up, and the full
   * acquisition management panel (releases, live download, files, cancel). All state lives in the
   * page-owned {@link useEntityAcquisition} composable, whose `visible` also gates the tab itself;
   * this component only renders it. Renders nothing while the state says there is no story.
   */
  import { Bell, BellRing, RefreshCw, Search, Trash2 } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import { deleteMediaEntity, isDeletableMediaKind } from "$lib/api/entity-deletion";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";

  let {
    acq,
    onCancelled,
    entity,
    onDeleted,
    onReverted,
  }: {
    /** The page-owned acquisition state (from {@link useEntityAcquisition}). */
    acq: EntityAcquisition;
    /**
     * Called after the acquisition is cancelled, so the page can refresh. Cancel stops the download
     * only — the wanted placeholder and any monitoring stay, and the page keeps existing.
     */
    onCancelled?: () => void;
    /**
     * The entity this card manages, enabling the destructive "Delete files" action for file-backed
     * media kinds. Deleting is monitor-aware: content under active monitoring reverts to Wanted (files
     * removed, library entry kept, monitoring untouched — it will be re-acquired); unmonitored content
     * is removed from the library entirely. Omit to hide the action.
     */
    entity?: { id: string; kind: string; title: string };
    /** Called after the entity was fully removed — the page must navigate away (it no longer exists). */
    onDeleted?: () => void;
    /** Called after the entity reverted to Wanted instead (still exists) — the page should refresh. */
    onReverted?: () => void;
  } = $props();

  let confirmDeleteOpen = $state(false);

  const canDelete = $derived(Boolean(entity && onDeleted && isDeletableMediaKind(entity.kind)));
  const hasActions = $derived(
    acq.monitorActive || acq.showMonitor || acq.showSearch || acq.showSearchMissing || canDelete,
  );

  async function handleConfirmDelete() {
    if (!entity) return;
    const result = await deleteMediaEntity(entity.id, true);
    if ((Number(result.reverted) || 0) > 0) {
      // Reverted to Wanted: the page still exists — refresh it so the wanted state shows.
      await acq.refresh();
      onReverted?.();
    } else {
      onDeleted?.();
    }
  }
</script>

{#if acq.visible}
  <section class="acquisition-card">
    {#if hasActions}
      <div class="flex flex-wrap items-center gap-2">
        {#if acq.monitorActive}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={acq.syncBusy}
            onclick={() => void acq.syncNow()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Re-sync from the provider now instead of waiting for the daily sweep"
          >
            <RefreshCw class="h-3.5 w-3.5" />
            {acq.syncBusy ? "Checking…" : "Check for new works"}
          </Button>
        {/if}
        {#if acq.showMonitor}
          <Button
            type="button"
            variant={acq.monitorActive ? "primary" : "secondary"}
            size="sm"
            disabled={acq.monitorBusy}
            onclick={() => void acq.toggleMonitor()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title={acq.monitorActive
              ? "Watching for new works daily — click to stop"
              : "Watch this for new works; they appear as Wanted placeholders"}
          >
            {#if acq.monitorActive}
              <BellRing class="h-3.5 w-3.5" />
              Monitoring
            {:else}
              <Bell class="h-3.5 w-3.5" />
              {acq.monitor ? "Resume monitoring" : "Monitor"}
            {/if}
          </Button>
        {/if}
        {#if acq.showSearch}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.searchBusy}
            onclick={() => void acq.searchForRelease()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.searchBusy ? "Searching…" : "Search for release"}
          </Button>
        {/if}
        {#if acq.showSearchMissing}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.missingBusy}
            onclick={() => void acq.searchMissing()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Sweep for anything missing at any depth — every gap gets its own monitored search"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.missingBusy
              ? "Searching…"
              : acq.missingChildren.length > 0
                ? `Search ${acq.missingChildren.length} missing`
                : "Search missing content"}
          </Button>
        {/if}
        {#if canDelete && entity}
          <Button
            type="button"
            variant="danger"
            size="sm"
            onclick={() => (confirmDeleteOpen = true)}
            class="no-lift ml-auto gap-1.5 px-2.5 py-1 text-xs"
            title="Permanently delete this item's files on disk. Monitored content goes back to Wanted and will be re-acquired; unmonitored content is removed from the library."
          >
            <Trash2 class="h-3.5 w-3.5" />
            Delete files
          </Button>
        {/if}
      </div>
    {/if}

    {#if acq.missingResult}
      <p class="text-[0.72rem] text-text-muted">{acq.missingResult}</p>
    {/if}

    {#if acq.showMonitor && acq.trackedVia}
      <p class="text-[0.72rem] text-text-muted">
        {acq.monitorActive
          ? `Watching for new works daily via ${acq.trackedVia}.`
          : `Monitoring available — tracked via ${acq.trackedVia}.`}
      </p>
    {/if}

    {#if acq.showSearch}
      <p class="text-[0.72rem] text-text-muted">
        No file yet. Searching starts an auto-grabbing, monitored acquisition for this item.
      </p>
    {/if}

    {#if acq.childStatuses.length > 0}
      <div class="child-roll">
        <h3 class="child-roll-title">{acq.childKindLabel} in progress</h3>
        <ul class="child-list">
          {#each acq.childStatuses as child (child.id)}
            {@const Icon = child.display.icon}
            <svelte:element
              this={child.href ? "a" : "div"}
              href={child.href}
              role={child.href ? "link" : undefined}
              class={`child-row tone-${child.display.tone}`}
            >
              <span class="child-status" aria-hidden="true"><Icon size={13} /></span>
              <span class="child-title">{child.title}</span>
              <span class="child-label">{child.display.label}</span>
            </svelte:element>
          {/each}
        </ul>
      </div>
    {/if}

    {#if acq.acquisition}
      <AcquisitionPanel acquisitionId={acq.acquisition.summary.id} bind:detail={acq.acquisition} {onCancelled} />
    {/if}
  </section>

  {#if canDelete && entity}
    <ConfirmDialog
      open={confirmDeleteOpen}
      title={`Delete the files for "${entity.title}"?`}
      message="This permanently deletes its files from disk — including seasons, episodes, or other contents — and cannot be undone. While it (or its series/author) is actively monitored it goes back to Wanted and will be re-downloaded automatically; otherwise it is removed from the library. Monitoring itself is never changed by a delete — use the Monitor toggle for that."
      confirmLabel="Delete files"
      danger
      onConfirm={handleConfirmDelete}
      onClose={() => (confirmDeleteOpen = false)}
    />
  {/if}
{/if}

<style>
  /* Frameless: the detail tab panel supplies the surface, border, and padding. */
  .acquisition-card {
    display: grid;
    gap: 0.9rem;
    min-width: 0;
  }

  .child-roll {
    display: grid;
    gap: 0.4rem;
  }
  .child-roll-title {
    font-size: 0.66rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--color-text-muted, rgb(196 201 212 / 0.6));
  }
  .child-list {
    display: grid;
    gap: 0.3rem;
    margin: 0;
    padding: 0;
    list-style: none;
  }
  .child-row {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    padding: 0.4rem 0.55rem;
    border: 1px solid rgb(255 255 255 / 0.07);
    border-radius: var(--radius-sm, 6px);
    background: rgb(255 255 255 / 0.02);
    text-decoration: none;
    color: inherit;
    transition: background 120ms ease, border-color 120ms ease;
  }
  a.child-row:hover {
    background: rgb(255 255 255 / 0.05);
    border-color: rgb(255 255 255 / 0.14);
  }
  .child-status {
    display: grid;
    place-items: center;
    width: 1.5rem;
    height: 1.5rem;
    flex: 0 0 auto;
    border-radius: var(--radius-xs, 4px);
  }
  .child-title {
    flex: 1 1 auto;
    min-width: 0;
    font-size: 0.82rem;
    font-weight: 500;
    color: rgb(244 239 230 / 0.92);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .child-label {
    flex: 0 0 auto;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    font-weight: 600;
  }
  /* Tone the status glyph + label per state, matching the thumbnail badge vocabulary. */
  .tone-downloading .child-status { color: #f2c26a; background: rgb(60 44 16 / 0.5); }
  .tone-downloading .child-label { color: #f2c26a; }
  .tone-searching .child-status { color: #e7d3af; background: rgb(48 40 22 / 0.5); }
  .tone-searching .child-label { color: #e7d3af; }
  .tone-queued .child-status { color: rgb(214 219 228 / 0.85); background: rgb(255 255 255 / 0.05); }
  .tone-queued .child-label { color: rgb(214 219 228 / 0.85); }
  .tone-attention .child-status { color: #f2c26a; background: rgb(58 38 12 / 0.5); }
  .tone-attention .child-label { color: #f2c26a; }
  .tone-failed .child-status { color: #ff9a86; background: rgb(48 18 14 / 0.5); }
  .tone-failed .child-label { color: #ff9a86; }
  .tone-wanted .child-status { color: rgb(242 194 106 / 0.9); background: rgb(39 29 12 / 0.6); }
  .tone-wanted .child-label { color: rgb(242 194 106 / 0.9); }
</style>
