<script lang="ts">
  import { Loader2, RefreshCw } from "@lucide/svelte";
  import { Button, Toggle } from "@prismedia/ui-svelte";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";

  let {
    acq,
  }: {
    acq: EntityAcquisition;
  } = $props();

  const checked = $derived(acq.monitorActive || acq.monitorDeletingFiles);
  const disabled = $derived(
    acq.monitorBusy
      || acq.monitorStopping
      || acq.monitorDeletingFiles
      || acq.monitorUnknownStatus,
  );
  const statusText = $derived.by(() => {
    if (acq.monitorStopping) {
      return "Monitoring is off, but cleanup still needs attention.";
    }
    if (acq.monitorDeletingFiles) {
      return "Monitoring stays on while managed files are deleted.";
    }
    if (acq.monitorUnknownStatus) {
      return "Refreshing an unfamiliar monitor status before changes are allowed.";
    }
    if (acq.monitorBusy) {
      return checked ? "Updating monitoring…" : "Turning on monitoring…";
    }
    if (acq.monitorActive && acq.showSync) {
      return acq.trackedVia
        ? `Checks daily for new content via ${acq.trackedVia}.`
        : "Checks daily for new content.";
    }
    if (acq.monitorActive) {
      return acq.trackedVia
        ? `Monitoring via ${acq.trackedVia}.`
        : "Actively monitoring this item.";
    }
    if (acq.monitor) {
      return "Paused. Turn Monitor on to resume.";
    }
    return acq.trackedVia
      ? `Available via ${acq.trackedVia}.`
      : "Off";
  });
</script>

<div class="monitor-control">
  <div class="min-w-0 flex-1">
    <div class="flex items-center gap-2">
      <h2 class="text-[0.88rem] font-medium text-text-primary">Monitor</h2>
      {#if acq.monitorBusy}
        <Loader2 class="h-3.5 w-3.5 animate-spin text-text-muted" aria-hidden="true" />
      {/if}
    </div>
    <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{statusText}</p>
    {#if acq.monitorStopping}
      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={acq.monitorBusy}
        onclick={() => void acq.toggleMonitor()}
        class="no-lift mt-2 gap-1.5"
      >
        <RefreshCw class="h-3.5 w-3.5" />
        Finish unmonitoring
      </Button>
    {/if}
  </div>
  <Toggle
    {checked}
    {disabled}
    onchange={() => void acq.toggleMonitor()}
    ariaLabel="Monitor"
  />
</div>

<style>
  .monitor-control {
    align-items: center;
    background: color-mix(in srgb, var(--color-surface-1) 72%, transparent);
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    display: flex;
    gap: 1rem;
    justify-content: space-between;
    min-width: 0;
    padding: 0.8rem 0.9rem;
  }
</style>
