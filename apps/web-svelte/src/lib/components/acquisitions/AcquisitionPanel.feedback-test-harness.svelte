<script lang="ts">
  import type { AcquisitionDetail } from "$lib/api/generated/model";
  import AcquisitionPanel from "./AcquisitionPanel.svelte";

  let { initialDetail }: { initialDetail: AcquisitionDetail } = $props();

  // Mirrors EntityAcquisitionCard's production ownership: the panel publishes each refreshed
  // detail object back to the parent, and that parent value supplies both child props again.
  let detail = $derived<AcquisitionDetail | null>(initialDetail);
</script>

{#if detail}
  <AcquisitionPanel
    acquisitionId={detail.summary.id}
    {detail}
    onDetailChange={(nextDetail) => (detail = nextDetail)}
  />
{/if}
