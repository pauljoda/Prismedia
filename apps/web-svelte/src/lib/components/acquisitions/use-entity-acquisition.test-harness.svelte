<script lang="ts">
  import type { EntityCapability } from "$lib/api/generated/model";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  let {
    entityId,
    capabilities = [],
    childCards = [],
    onChanged,
    onPruned,
  }: {
    entityId: string;
    capabilities?: EntityCapability[];
    childCards?: EntityThumbnailCard[];
    onChanged?: () => void | Promise<void>;
    onPruned?: () => void | Promise<void>;
  } = $props();

  const acq = useEntityAcquisition({
    entityId: () => entityId,
    capabilities: () => capabilities,
    childCards: () => childCards,
    onChanged: () => onChanged?.(),
    onPruned: () => onPruned?.(),
  });
</script>

<button type="button" onclick={() => void acq.searchForRelease()}>Search for release</button>
{#if acq.showMonitor}
  <button
    type="button"
    disabled={acq.monitorDeletingFiles || acq.monitorUnknownStatus}
    onclick={() => void acq.toggleMonitor()}
  >
    {acq.monitorStopping
      ? "Finish unmonitoring"
      : acq.monitorDeletingFiles
        ? "Deleting files…"
        : acq.monitorUnknownStatus
          ? "Updating…"
          : acq.monitorActive ? "Monitoring" : "Monitor"}
  </button>
{/if}
<span data-testid="show-sync">{acq.showSync ? "yes" : "no"}</span>
<span data-testid="show-search-missing">{acq.showSearchMissing ? "yes" : "no"}</span>
<span data-testid="missing-count">{acq.missingChildCount}</span>
<span data-testid="visible">{acq.visible ? "yes" : "no"}</span>
{#if acq.monitorError}<p role="alert">{acq.monitorError}</p>{/if}
<span data-testid="acquisition-id">{acq.acquisition?.summary.id ?? "none"}</span>
