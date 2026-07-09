<script lang="ts">
  import type { EntityCapability } from "$lib/api/generated/model";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";

  let {
    entityId,
    capabilities = [],
    onChanged,
  }: {
    entityId: string;
    capabilities?: EntityCapability[];
    onChanged?: () => void | Promise<void>;
  } = $props();

  const acq = useEntityAcquisition({
    entityId: () => entityId,
    capabilities: () => capabilities,
    onChanged: () => onChanged?.(),
  });
</script>

<button type="button" onclick={() => void acq.searchForRelease()}>Search for release</button>
<span data-testid="acquisition-id">{acq.acquisition?.summary.id ?? "none"}</span>
