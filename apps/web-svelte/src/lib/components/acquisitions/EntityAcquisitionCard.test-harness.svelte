<script lang="ts">
  import type { AcquisitionDetail } from "$lib/api/generated/model";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";

  let {
    initialAcquisition,
    refresh,
    onReverted,
    onDeleted,
    onImported,
  }: {
    initialAcquisition: AcquisitionDetail;
    refresh: () => Promise<void>;
    onReverted: () => void | Promise<void>;
    onDeleted: () => void;
    onImported?: () => void | Promise<void>;
  } = $props();

  let acquisition = $derived<AcquisitionDetail | null>(initialAcquisition);

  const acq: EntityAcquisition = {
    get acquisition() {
      return acquisition;
    },
    set acquisition(value) {
      acquisition = value;
    },
    monitor: null,
    monitorActive: false,
    trackedVia: "",
    showMonitor: false,
    showSearch: false,
    showSearchMissing: false,
    visible: true,
    childStatuses: [],
    childKindLabel: "",
    missingChildren: [],
    monitorBusy: false,
    syncBusy: false,
    searchBusy: false,
    missingBusy: false,
    missingResult: null,
    clearAcquisition() {
      acquisition = null;
    },
    refresh: () => refresh(),
    async toggleMonitor() {},
    async syncNow() {},
    async searchMissing() {},
    async searchForRelease() {},
  };
</script>

<span data-testid="bound-acquisition-id">{acquisition?.summary.id ?? "none"}</span>
<EntityAcquisitionCard
  {acq}
  entity={{ id: "season-1", kind: "video-season", title: "Season 1" }}
  {onDeleted}
  {onReverted}
  {onImported}
/>
