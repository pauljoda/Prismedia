<script lang="ts">
  import { Loader2, RefreshCw } from "@lucide/svelte";
  import { Button, Card, Toggle } from "@prismedia/ui-svelte";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import RequestTargetOptions from "$lib/components/acquisitions/RequestTargetOptions.svelte";
  import type { RequestKindInfo } from "$lib/requests/request-helpers";

  let {
    acq,
    kindInfo,
  }: {
    acq: EntityAcquisition;
    kindInfo?: RequestKindInfo | null;
  } = $props();

  let profileId = $state<string | null>(null);
  let targetLibraryRootId = $state<string | null>(null);
  let synchronizedTargetingKey = $state<string | null>(null);
  $effect(() => {
    const monitor = acq.monitor;
    const nextKey = monitor
      ? `${monitor.id}:${monitor.profileId ?? ""}:${monitor.targetLibraryRootId ?? ""}`
      : "unmonitored";
    if (nextKey === synchronizedTargetingKey) return;

    synchronizedTargetingKey = nextKey;
    profileId = monitor?.profileId ?? null;
    targetLibraryRootId = monitor?.targetLibraryRootId ?? null;
  });
  const checked = $derived(acq.monitorActive || acq.monitorDeletingFiles);
  const targetingDirty = $derived(
    acq.monitorActive
      && (profileId !== (acq.monitor?.profileId ?? null)
        || targetLibraryRootId !== (acq.monitor?.targetLibraryRootId ?? null)),
  );
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
        ? `Checks daily via ${acq.trackedVia}; content grouping follows that provider.`
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

<Card.Root size="sm">
  <Card.Header class={kindInfo ? "border-b border-border-subtle" : undefined}>
    <Card.Title role="heading" aria-level={2} class="flex items-center gap-2">
      Monitoring
      {#if acq.monitorBusy}
        <Loader2 class="animate-spin text-muted-foreground" aria-hidden="true" />
      {/if}
    </Card.Title>
    <Card.Description>{statusText}</Card.Description>
    <Card.Action>
      <Toggle
        {checked}
        {disabled}
        onchange={() => void acq.toggleMonitor({ profileId, targetLibraryRootId })}
        ariaLabel="Monitor"
      />
    </Card.Action>
  </Card.Header>

  {#if acq.monitorStopping || kindInfo}
    <Card.Content class="flex flex-col gap-3">
      {#if acq.monitorStopping}
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={acq.monitorBusy}
          onclick={() => void acq.toggleMonitor()}
          class="no-lift w-full justify-start"
        >
          <RefreshCw data-icon="inline-start" />
          Finish unmonitoring
        </Button>
      {/if}

      {#if kindInfo}
        <RequestTargetOptions
          {kindInfo}
          bind:targetLibraryRootId
          bind:profileId
        >
          {#snippet actions()}
            {#if acq.monitorActive}
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={acq.monitorBusy || !targetingDirty}
                onclick={() => void acq.updateMonitorTargeting({ profileId, targetLibraryRootId })}
                class="no-lift"
              >
                Apply
              </Button>
            {/if}
          {/snippet}
        </RequestTargetOptions>
      {/if}
    </Card.Content>
  {/if}
</Card.Root>
