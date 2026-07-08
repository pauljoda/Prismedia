<script lang="ts">
  /**
   * THE acquisition + monitoring surface an entity page mounts — the Acquisition detail tab's body,
   * collapsing everything the request layer knows about an entity: the standing container monitor,
   * the wanted placeholder's "Search for release", the wanted-children roll-up, and the full
   * acquisition management panel (releases, live download, files, cancel). All state lives in the
   * page-owned {@link useEntityAcquisition} composable, whose `visible` also gates the tab itself;
   * this component only renders it. Renders nothing while the state says there is no story.
   */
  import { Bell, BellRing, RefreshCw, Search } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";

  let {
    acq,
    onCancelled,
  }: {
    /** The page-owned acquisition state (from {@link useEntityAcquisition}). */
    acq: EntityAcquisition;
    /**
     * Called after the acquisition is cancelled. A wanted entity's page must navigate away here —
     * cancelling a request deletes its wanted placeholder, so the page it sat on no longer exists.
     */
    onCancelled?: () => void;
  } = $props();

  const hasActions = $derived(
    acq.monitorActive || acq.showMonitor || acq.showSearch || acq.showSearchMissing,
  );
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
            title="Request each missing item individually — every gap gets its own monitored search"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.missingBusy ? "Searching…" : `Search ${acq.missingChildren.length} missing`}
          </Button>
        {/if}
      </div>
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
