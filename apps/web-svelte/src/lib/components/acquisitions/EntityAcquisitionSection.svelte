<script lang="ts">
  /**
   * The inline acquisition surface an entity page mounts when its entity has an acquisition —
   * the shared section chrome around AcquisitionPanel, so wanted books, movies, albums, seasons,
   * and episodes all manage their downloads identically on their own pages.
   */
  import { CloudDownload } from "@lucide/svelte";
  import type { AcquisitionDetail } from "$lib/api/generated/model";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";

  interface Props {
    acquisition: AcquisitionDetail;
    onCancelled?: () => void;
  }
  let { acquisition, onCancelled }: Props = $props();
</script>

<section class="acquisition-section surface-panel">
  <h2 class="flex items-center gap-2 text-kicker text-text-primary">
    <CloudDownload class="h-3.5 w-3.5 text-text-accent" />
    Acquisition
  </h2>
  <AcquisitionPanel acquisitionId={acquisition.summary.id} detail={acquisition} {onCancelled} />
</section>

<style>
  .acquisition-section {
    display: grid;
    gap: 0.9rem;
    padding: 1rem 1.1rem;
    min-width: 0;
  }
</style>
