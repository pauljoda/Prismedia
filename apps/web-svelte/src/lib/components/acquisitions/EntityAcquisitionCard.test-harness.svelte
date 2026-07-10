<script lang="ts">
  import type { AcquisitionDetail } from "$lib/api/generated/model";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";

  let {
    initialAcquisition,
    refresh,
    onImported,
    monitorStopping = false,
    monitorDeletingFiles = false,
    monitorUnknownStatus = false,
    onToggleMonitor,
  }: {
    initialAcquisition: AcquisitionDetail;
    refresh: () => Promise<void>;
    onImported?: () => void | Promise<void>;
    monitorStopping?: boolean;
    monitorDeletingFiles?: boolean;
    monitorUnknownStatus?: boolean;
    onToggleMonitor?: () => void | Promise<void>;
  } = $props();

  let acquisition = $derived<AcquisitionDetail | null>(initialAcquisition);

  const acq: EntityAcquisition = {
    get acquisition() {
      return acquisition;
    },
    set acquisition(value) {
      acquisition = value;
    },
    get monitor() {
      return monitorStopping || monitorDeletingFiles || monitorUnknownStatus ? ({} as never) : null;
    },
    monitorActive: false,
    get monitorStopping() {
      return monitorStopping;
    },
    get monitorDeletingFiles() {
      return monitorDeletingFiles;
    },
    get monitorUnknownStatus() {
      return monitorUnknownStatus;
    },
    trackedVia: "",
    showSync: false,
    get showMonitor() {
      return monitorStopping || monitorDeletingFiles || monitorUnknownStatus;
    },
    showSearch: false,
    showSearchMissing: false,
    visible: true,
    childCards: [],
    missingChildCount: 0,
    monitorBusy: false,
    monitorError: null,
    syncBusy: false,
    searchBusy: false,
    missingBusy: false,
    missingResult: null,
    clearAcquisition() {
      acquisition = null;
    },
    refresh: () => refresh(),
    async toggleMonitor() {
      await onToggleMonitor?.();
    },
    async syncNow() {},
    async searchMissing() {},
    async searchForRelease() {},
    async childMonitoringChanged() {},
  };
</script>

<span data-testid="bound-acquisition-id">{acquisition?.summary.id ?? "none"}</span>
<EntityAcquisitionCard
  {acq}
  {onImported}
/>
